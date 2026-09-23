using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.FinalPay;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Application.Scheduling.DTOs;
using PeopleCore.Application.Scheduling.Interfaces;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Infrastructure.Persistence;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Payroll;

/// <summary>
/// FinalPayService through the real repositories and Postgres. What these catch that the unit
/// tests cannot: the run, its entry, its inputs with their deductions, and the separation's link
/// are all new rows with keys already set, hung off entities EF is already tracking - exactly the
/// shape EF mistakes for existing rows and turns into UPDATEs that match nothing.
/// </summary>
public class FinalPayServiceDbTests : DatabaseTestBase
{
    public FinalPayServiceDbTests(PostgresFixture fixture) : base(fixture) { }

    private static readonly DateOnly LastDay = new(2026, 3, 13);

    private static FinalPayService Service(AppDbContext context)
    {
        // No shift assigned (Monday to Friday) and no attendance - the rest is real.
        var shifts = new Mock<IShiftService>();
        shifts.Setup(s => s.ResolveShiftForDayAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((DailyScheduleDto?)null);
        var attendance = new Mock<IPayrollAttendanceBridge>();
        attendance.Setup(b => b.BuildAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(),
                                           It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new AttendanceBridgeResult(new Dictionary<Guid, PayrollAttendanceInput>(), []));

        var runs = new PayrollRunRepository(context);
        return new FinalPayService(
            new SeparationRepository(context), runs, new EmployeeCompensationRepository(context),
            new EmployeeLoanRepository(context), new EmployeeAllowanceRepository(context),
            new LeaveBalanceRepository(context), shifts.Object,
            attendance.Object, new PayrollSettingsRepository(context),
            new Bir2316Service(runs, new EmployeeRepository(context), new CompanyRepository(context),
                               new Bir2316InputsRepository(context)),
            new PayrollComputationService(), TimeProvider.System);
    }

    private async Task<Separation> SeedAsync()
    {
        var employee = AnEmployee("Santos", "Maria");
        employee.HireDate = new DateOnly(2021, 3, 1);
        employee.DateOfBirth = new DateOnly(1985, 6, 15);
        Context.Employees.Add(employee);
        Context.Companies.Add(ACompany());
        Context.EmployeeCompensations.Add(new EmployeeCompensation
        {
            EmployeeId = employee.Id, BasicSalary = 36_500m, PayFrequency = PayFrequency.Monthly,
        });
        Context.EmployeeLoans.Add(new EmployeeLoan
        {
            EmployeeId = employee.Id, LoanType = LoanType.SSSLoan, TotalAmount = 12_000m,
            MonthlyDeduction = 1_000m, RemainingBalance = 3_000m, StartDate = new DateOnly(2025, 6, 1),
        });
        var vacation = new LeaveType { Name = "Vacation Leave", Code = "VL", MaxDaysPerYear = 15m, IsConvertibleToCash = true };
        Context.LeaveTypes.Add(vacation);
        Context.LeaveBalances.Add(new LeaveBalance
        {
            EmployeeId = employee.Id, LeaveTypeId = vacation.Id, Year = 2026, TotalDays = 5m,
        });

        var february = ARun("PAY-2026-002", new(2026, 2, 1), new(2026, 2, 28), new(2026, 2, 28));
        february.Frequency = PayFrequency.Monthly;
        Context.PayrollRuns.Add(february);
        var februaryEntry = AnEntry(february.Id, employee.Id, regularPay: 36_500m);
        februaryEntry.WithholdingTax = 2_000m;
        Context.PayrollRunEmployees.Add(februaryEntry);

        var separation = new Separation
        {
            EmployeeId = employee.Id,
            Type = SeparationType.AuthorizedCause,
            AuthorizedCause = AuthorizedCause.Redundancy,
            NoticeDate = LastDay.AddDays(-30),
            LastWorkingDay = LastDay,
            Status = SeparationStatus.Separated,
            RecordedBy = "hr@company.test",
            ClearanceItems = [new SeparationClearanceItem { Name = "Finance", SortOrder = 1 }],
        };
        Context.Separations.Add(separation);
        await Context.SaveChangesAsync();
        return separation;
    }

    /// <summary>Another separated employee with pay on file - nothing else - for numbering tests.</summary>
    private async Task<Separation> SeedAnotherSeparationAsync(string lastName)
    {
        var employee = AnEmployee(lastName, "Ana");
        Context.Employees.Add(employee);
        Context.EmployeeCompensations.Add(new EmployeeCompensation
        {
            EmployeeId = employee.Id, BasicSalary = 30_000m, PayFrequency = PayFrequency.Monthly,
        });
        var separation = new Separation
        {
            EmployeeId = employee.Id,
            Type = SeparationType.Resignation,
            NoticeDate = LastDay.AddDays(-30),
            LastWorkingDay = LastDay,
            Status = SeparationStatus.Separated,
            RecordedBy = "hr@company.test",
        };
        Context.Separations.Add(separation);
        await Context.SaveChangesAsync();
        return separation;
    }

    private static FinalPayRequest Request(params FinalPayDeductionDto[] deductions)
        => new(new DateOnly(2026, 3, 31), null, null, null, null, deductions);

    [Fact]
    public async Task CreateAsync_SavesTheRunItsInputsAndTheSeparationLink_InOneGo()
    {
        var seeded = await SeedAsync();

        // A fresh context, as a request would have: the separation is loaded and tracked by it.
        await using (var context = NewContext())
        {
            await Service(context).CreateAsync(seeded.Id, Request(new FinalPayDeductionDto("Unreturned laptop", 25_000m)));
        }

        await using var reader = NewContext();
        var separation = await new SeparationRepository(reader).GetAsync(seeded.Id);
        separation!.FinalPayRunId.Should().NotBeNull();
        separation.FinalPayRun!.RunNumber.Should().Be("FP-2026-001");

        var run = await new PayrollRunRepository(reader).GetWithEntriesAsync(separation.FinalPayRunId!.Value);
        run!.RunType.Should().Be(PayrollRunType.FinalPay);
        run.PeriodStart.Should().Be(new DateOnly(2026, 3, 1));
        run.PeriodEnd.Should().Be(LastDay);
        run.FinalPayInputs!.SeparationId.Should().Be(seeded.Id);
        run.FinalPayInputs.WorkingDays.Should().Be(13m);   // Mar 1-13, calendar days on the 365 factor
        run.FinalPayInputs.Deductions.Select(d => (d.Label, d.Amount)).Should().Equal(("Unreturned laptop", 25_000m));

        // The worked example's figures (FinalPayServiceTests), now read back from Postgres.
        var entry = run.Employees.Single();
        entry.RegularPay.Should().Be(15_600m);           // 1,200 x 13
        entry.ThirteenthMonth.Should().Be(4_341.67m);    // (36,500 + 15,600) / 12
        entry.LeaveConversionPay.Should().Be(6_000m);
        entry.SeparationPay.Should().Be(182_500m);
        entry.WithholdingTax.Should().Be(-2_000m);
        entry.LoanDeductions.Should().Be(3_000m);
        entry.LoanDeductionLines.Should().ContainSingle().Which.Amount.Should().Be(3_000m);
        entry.OtherDeductions.Should().Be(25_000m);
        entry.NetPay.Should().Be(179_579.17m);   // 204,579.17 less the 25,000 laptop
    }

    [Fact]
    public async Task UpdateThenCompute_ReplaceTheDeductionsAndEntry_AndReproduceTheFigures()
    {
        var seeded = await SeedAsync();
        Guid runId;
        await using (var context = NewContext())
        {
            runId = (await Service(context).CreateAsync(seeded.Id,
                Request(new FinalPayDeductionDto("Unreturned laptop", 25_000m)))).RunId;
        }

        // Update: one deduction removed, one added - on a run EF loads and tracks with its inputs.
        await using (var context = NewContext())
        {
            await Service(context).UpdateAsync(seeded.Id, Request(new FinalPayDeductionDto("Cash advance", 1_500m)));
        }

        // Recompute through the ordinary run endpoint.
        await using (var context = NewContext())
        {
            var runs = new PayrollRunRepository(context);
            var payrollRuns = new PayrollRunService(
                runs, new EmployeeCompensationRepository(context), new EmployeeAllowanceRepository(context),
                new EmployeeLoanRepository(context), new PayrollSettingsRepository(context),
                new PayrollComputationService(), Mock.Of<IPayrollAttendanceBridge>(),
                new EmployeeRepository(context), NullLogger<PayrollRunService>.Instance, Service(context));
            await payrollRuns.ComputeAsync(runId);
        }

        await using var reader = NewContext();
        var run = await new PayrollRunRepository(reader).GetWithEntriesAsync(runId);
        run!.Status.Should().Be(PayrollRunStatus.Draft);
        run.FinalPayInputs!.Deductions.Select(d => (d.Label, d.Amount)).Should().Equal(("Cash advance", 1_500m));
        reader.FinalPayDeductions.Count().Should().Be(1);
        var entry = run.Employees.Should().ContainSingle().Subject;
        entry.OtherDeductions.Should().Be(1_500m);
        entry.WithholdingTax.Should().Be(-2_000m);
        entry.NetPay.Should().Be(203_079.17m);   // 204,579.17 less the 1,500 cash advance
    }

    [Fact]
    public async Task AddFinalPayRun_RefusesASecondRunForASeparationAnotherRequestAlreadyLinked()
    {
        var seeded = await SeedAsync();

        // Request A loads the separation while it has no final pay yet...
        await using var contextA = NewContext();
        var separationA = await new SeparationRepository(contextA).GetAsync(seeded.Id);
        separationA!.FinalPayRunId.Should().BeNull();

        // ...request B creates one in the meantime...
        Guid firstRunId;
        await using (var contextB = NewContext())
        {
            firstRunId = (await Service(contextB).CreateAsync(seeded.Id, Request())).RunId;
        }

        // ...and request A's save must not add a second run or move the link.
        var second = new PayrollRun
        {
            RunNumber = "FP-2026-002", RunType = PayrollRunType.FinalPay, Frequency = PayFrequency.Monthly,
            PeriodStart = new DateOnly(2026, 3, 1), PeriodEnd = LastDay, PayDate = new DateOnly(2026, 3, 31),
        };
        second.FinalPayInputs = new FinalPayInputs { PayrollRunId = second.Id, SeparationId = seeded.Id, WorkingDays = 10m };
        second.Employees = [AnEntry(second.Id, seeded.EmployeeId, regularPay: 12_000m)];
        separationA.FinalPayRunId = second.Id;

        var act = () => new PayrollRunRepository(contextA).AddFinalPayRunAsync(second, separationA);

        await act.Should().ThrowAsync<PeopleCore.Domain.Exceptions.DomainException>()
            .WithMessage("This separation already has a final-pay run.");

        await using var reader = NewContext();
        reader.PayrollRuns.Count(r => r.RunType == PayrollRunType.FinalPay).Should().Be(1);
        reader.FinalPayInputs.Count().Should().Be(1);
        (await new SeparationRepository(reader).GetAsync(seeded.Id))!.FinalPayRunId.Should().Be(firstRunId);
    }

    [Fact]
    public async Task CreateAsync_AfterARunMovedToAnotherYear_DrawsTheNextUnusedNumber()
    {
        // FP-2026-001 (A) and FP-2026-002 (B) exist; A's pay date moves into 2027, so A becomes
        // FP-2027-001 and 2026 is left with one run - numbered 002. Counting that year's runs
        // would hand the next 2026 final pay 002 again and trip the unique index on RunNumber.
        var a = await SeedAsync();
        var b = await SeedAnotherSeparationAsync("Reyes");
        var c = await SeedAnotherSeparationAsync("Garcia");

        await using (var context = NewContext())
            (await Service(context).CreateAsync(a.Id, Request())).RunNumber.Should().Be("FP-2026-001");
        await using (var context = NewContext())
            (await Service(context).CreateAsync(b.Id, Request())).RunNumber.Should().Be("FP-2026-002");
        await using (var context = NewContext())
        {
            var moved = await Service(context).UpdateAsync(a.Id,
                new FinalPayRequest(new DateOnly(2027, 1, 5), null, null, null, null, []));
            moved.RunNumber.Should().Be("FP-2027-001");
        }

        await using (var context = NewContext())
            (await Service(context).CreateAsync(c.Id, Request())).RunNumber.Should().Be("FP-2026-003");
    }
}
