using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.GovernmentReports;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using PeopleCore.Infrastructure.Persistence;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Payroll;

/// <summary>
/// A December payroll that converts unused year-end leave, through the real repositories and
/// Postgres: created, approved and paid, then reported on the 2316 and the 1601-C.
/// <para>
/// Maria Santos earns 36,500 a month, paid monthly: 36,500 x 12 / 365 = 1,200.00 a day.
/// Contributions on 36,500: SSS 1,750 + PhilHealth 912.50 + Pag-IBIG 200 = 2,862.50, leaving a
/// withholding base of 33,637.50 a month - 403,650 a year, 22,500 + 20% x 3,650 = 23,230, or
/// 1,935.83 a month. November is already Paid with exactly those figures.
/// </para>
/// </summary>
public class YearEndLeaveConversionDbTests : DatabaseTestBase
{
    public YearEndLeaveConversionDbTests(PostgresFixture fixture) : base(fixture) { }

    private static readonly DateOnly DecemberStart = new(2026, 12, 1);
    private static readonly DateOnly DecemberEnd = new(2026, 12, 31);
    private static readonly DateOnly DecemberPayDate = new(2026, 12, 29);

    private static PayrollRunService PayrollRuns(AppDbContext context) => new(
        new PayrollRunRepository(context), new EmployeeCompensationRepository(context),
        new EmployeeAllowanceRepository(context), new EmployeeLoanRepository(context),
        new PayrollSettingsRepository(context), new PayrollComputationService(), NoAttendance(),
        new EmployeeRepository(context), new SeparationRepository(context),
        NullLogger<PayrollRunService>.Instance, finalPay: null,
        yearEndLeave: new YearEndLeaveConversion(new LeaveBalanceRepository(context),
            new LeaveRequestRepository(context), new LeaveTypeRepository(context)));

    /// <summary>After the year ends, so the November and December reports can be built.</summary>
    private sealed class JanuaryClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2027, 1, 15, 0, 0, 0, TimeSpan.Zero);
    }

    private static IPayrollAttendanceBridge NoAttendance()
    {
        var bridge = new Mock<IPayrollAttendanceBridge>();
        bridge.Setup(b => b.BuildAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(),
                                       It.IsAny<CancellationToken>()))
              .ReturnsAsync(new AttendanceBridgeResult(new Dictionary<Guid, PayrollAttendanceInput>(), []));
        return bridge.Object;
    }

    private record Seeded(Employee Maria, LeaveBalance Sil, EmployeeLoan Loan);

    /// <summary>Maria, 3 unused SIL days for 2026 (unless told otherwise), an SSS loan, and a Paid November.</summary>
    private async Task<Seeded> SeedAsync(decimal silTotal = 5m, decimal silUsed = 2m)
    {
        var maria = AnEmployee("Santos", "Maria");
        Context.Employees.Add(maria);
        Context.Companies.Add(ACompany());
        Context.EmployeeCompensations.Add(new EmployeeCompensation
        {
            EmployeeId = maria.Id, BasicSalary = 36_500m, PayFrequency = PayFrequency.Monthly,
        });
        var loan = new EmployeeLoan
        {
            EmployeeId = maria.Id, LoanType = LoanType.SSSLoan, TotalAmount = 12_000m,
            MonthlyDeduction = 1_000m, RemainingBalance = 3_000m, StartDate = new DateOnly(2026, 1, 1),
        };
        Context.EmployeeLoans.Add(loan);

        var sil = new LeaveType
        {
            Name = "Service Incentive Leave", Code = "SIL", MaxDaysPerYear = 5m,
            ConvertsAtYearEnd = true, CountsAsVacationForDeMinimis = true,
        };
        Context.LeaveTypes.Add(sil);
        var balance = new LeaveBalance { EmployeeId = maria.Id, LeaveTypeId = sil.Id, Year = 2026, TotalDays = silTotal, UsedDays = silUsed };
        Context.LeaveBalances.Add(balance);

        var november = ARun("PAY-2026-022", new(2026, 11, 1), new(2026, 11, 30), new(2026, 11, 30));
        november.Frequency = PayFrequency.Monthly;
        Context.PayrollRuns.Add(november);
        var entry = AnEntry(november.Id, maria.Id, regularPay: 36_500m);
        entry.SSSEmployee = 1_750m;
        entry.PhilHealthEmployee = 912.50m;
        entry.PagIbigEmployee = 200m;
        entry.WithholdingTax = 1_935.83m;
        Context.PayrollRunEmployees.Add(entry);

        await Context.SaveChangesAsync();
        return new Seeded(maria, balance, loan);
    }

    private static CreatePayrollRunRequest December(Guid employeeId, bool thirteenthMonth = true) => new(
        DecemberStart, DecemberEnd, DecemberPayDate, PayFrequency.Monthly,
        [new PayrollRunEmployeeInput(employeeId, IncludeThirteenthMonth: thirteenthMonth)],
        IncludeLeaveConversion: true);

    private async Task<Guid> CreateAsync(Guid employeeId, bool thirteenthMonth = true)
    {
        await using var context = NewContext();
        return (await PayrollRuns(context).CreateAsync(December(employeeId, thirteenthMonth))).Id;
    }

    private async Task<Guid> CreateAndApproveAsync(Guid employeeId)
    {
        var runId = await CreateAsync(employeeId);
        await StepAsync(s => s.ApproveAsync(runId));
        return runId;
    }

    /// <summary>One request's worth of work, in its own context as a request would have.</summary>
    private async Task StepAsync(Func<PayrollRunService, Task> step)
    {
        await using var context = NewContext();
        await step(PayrollRuns(context));
    }

    /// <summary>Maria files a Pending request for one SIL day in December, which holds that day.</summary>
    private async Task FileOneSilDayAsync(Seeded seeded)
    {
        await using var context = NewContext();
        context.LeaveRequests.Add(new LeaveRequest
        {
            EmployeeId = seeded.Maria.Id, LeaveTypeId = seeded.Sil.LeaveTypeId,
            StartDate = new DateOnly(2026, 12, 21), EndDate = new DateOnly(2026, 12, 21),
            TotalDays = 1m, DaysInStartYear = 1m, Status = LeaveStatus.Pending,
        });
        await context.SaveChangesAsync();
    }

    private const string LeaveChanged =
        "Maria Santos's convertible leave has changed since this payroll was computed; recompute it before paying.";

    [Fact]
    public async Task AFlaggedDecemberRun_WithThe13thMonth_IsPaid_AndIts2316And1601CReconcile()
    {
        var seeded = await SeedAsync();
        var runId = await CreateAndApproveAsync(seeded.Maria.Id);

        await using (var context = NewContext())
            await PayrollRuns(context).MarkPaidAsync(runId);

        await using var reader = NewContext();
        var run = (await new PayrollRunRepository(reader).GetWithEntriesAsync(runId))!;
        run.Status.Should().Be(PayrollRunStatus.Paid);
        run.IncludesLeaveConversion.Should().BeTrue();

        // December: 36,500 regular; 13th month (36,500 + 36,500) / 12 = 6,083.33; 3 SIL days x
        // 1,200 = 3,600, all de minimis. The 13th month is well inside the 90,000 exemption, so the
        // withholding is the regular month's 1,935.83. Gross 36,500 + 6,083.33 + 3,600 = 46,183.33.
        var entry = run.Employees.Single();
        entry.ThirteenthMonth.Should().Be(6_083.33m);
        entry.LeaveConversionPay.Should().Be(3_600m);
        entry.LeaveConversionNonTaxable.Should().Be(3_600m);
        entry.FinalPayNonTaxable.Should().Be(3_600m);
        entry.WithholdingTax.Should().Be(1_935.83m);
        entry.GrossPay.Should().Be(46_183.33m);

        // Paying used the 3 converted days: 2 + 3 = 5 used of 5, nothing left to carry.
        var sil = (await new LeaveBalanceRepository(reader).GetByEmployeeAsync(seeded.Maria.Id, 2026)).Single();
        sil.UsedDays.Should().Be(5m);
        sil.RemainingDays.Should().Be(0m);
        (await reader.EmployeeLoans.FindAsync(seeded.Loan.Id))!.RemainingBalance.Should().Be(2_000m);

        // The 2316 over November and December:
        //   Item 19 gross 36,500 + 46,183.33 = 82,683.33;
        //   Item 34 13th month 6,083.33; Item 35 de minimis 3,600; Item 36 shares 2 x 2,862.50 = 5,725;
        //   Item 52 taxable 82,683.33 - 6,083.33 - 3,600 - 5,725 = 67,275.00;
        //   Item 25A withheld 2 x 1,935.83 = 3,871.66.
        var bir2316 = new Bir2316Service(new PayrollRunRepository(reader), new EmployeeRepository(reader),
            new CompanyRepository(reader), new Bir2316InputsRepository(reader));
        var cert = (await bir2316.BuildAsync(seeded.Maria.Id, 2026, new Bir2316ManualInputs()))!;
        cert.Item19_GrossCompensation.Should().Be(82_683.33m);
        cert.Item34_ThirteenthMonthAndBenefits.Should().Be(6_083.33m);
        cert.Item35_DeMinimis.Should().Be(3_600m);
        cert.Item52_TotalTaxableCompensation.Should().Be(67_275m);
        cert.Item25A_PresentTaxWithheld.Should().Be(3_871.66m);

        // The 1601-Cs for November and December add up to the same year: December's taxable is
        // 46,183.33 - 6,083.33 - 3,600 - 2,862.50 = 33,637.50, the same as November's.
        var reports = new GovernmentReportService(new PayrollRunRepository(reader), new CompanyRepository(reader),
            new PayrollSettingsRepository(reader), new JanuaryClock(), Mock.Of<IBir2316Service>(),
            new EmployeeRepository(reader));
        var nov = await reports.BuildAsync("1601c", 2026, 11);
        var dec = await reports.BuildAsync("1601c", 2026, 12);
        decimal Line(GovernmentReportDto report, string label) => report.Summary.Single(l => l.Label == label).Amount;

        Line(dec, "Total amount of compensation").Should().Be(46_183.33m);
        Line(dec, "13th month pay and other benefits").Should().Be(6_083.33m);
        Line(dec, "De minimis benefits").Should().Be(3_600m);
        Line(dec, "Total taxable compensation").Should().Be(33_637.50m);

        (Line(nov, "Total amount of compensation") + Line(dec, "Total amount of compensation"))
            .Should().Be(cert.Item19_GrossCompensation);
        (Line(nov, "Total taxable compensation") + Line(dec, "Total taxable compensation"))
            .Should().Be(cert.Item52_TotalTaxableCompensation);
        (Line(nov, "Total taxes withheld") + Line(dec, "Total taxes withheld"))
            .Should().Be(cert.Item25A_PresentTaxWithheld);
    }

    [Fact]
    public async Task MarkPaid_AfterTheLeaveChanged_IsRefused_AndChangesNothing()
    {
        var seeded = await SeedAsync();
        var runId = await CreateAndApproveAsync(seeded.Maria.Id);

        // A leave request filed after the run was approved holds one of the three days.
        await FileOneSilDayAsync(seeded);

        var act = () => StepAsync(s => s.MarkPaidAsync(runId));
        await act.Should().ThrowAsync<DomainException>().WithMessage(LeaveChanged);

        await using var reader = NewContext();
        (await new PayrollRunRepository(reader).GetWithEntriesAsync(runId))!.Status.Should().Be(PayrollRunStatus.Approved);
        (await reader.LeaveBalances.FindAsync(seeded.Sil.Id))!.UsedDays.Should().Be(2m);
        var loan = (await reader.EmployeeLoans.FindAsync(seeded.Loan.Id))!;
        loan.RemainingBalance.Should().Be(3_000m);
        loan.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task ARequestFiledAfterCompute_IsCaughtAtApproval_AndARecomputeLetsTheRunBePaid()
    {
        var seeded = await SeedAsync();
        var runId = await CreateAsync(seeded.Maria.Id);   // 3 SIL days: 3,600
        await FileOneSilDayAsync(seeded);                   // now 2 are left to convert

        var approve = () => StepAsync(s => s.ApproveAsync(runId));
        await approve.Should().ThrowAsync<DomainException>().WithMessage(LeaveChanged);

        await StepAsync(s => s.ComputeAsync(runId));
        await StepAsync(s => s.ApproveAsync(runId));
        await StepAsync(s => s.MarkPaidAsync(runId));

        // Recomputed at 2 x 1,200 = 2,400, and paying used exactly those 2: 2 + 2 = 4 used, and
        // the one day left is the one the request holds.
        await using var reader = NewContext();
        var run = (await new PayrollRunRepository(reader).GetWithEntriesAsync(runId))!;
        run.Status.Should().Be(PayrollRunStatus.Paid);
        run.Employees.Single().LeaveConversionPay.Should().Be(2_400m);
        var sil = (await reader.LeaveBalances.FindAsync(seeded.Sil.Id))!;
        sil.UsedDays.Should().Be(4m);
        sil.RemainingDays.Should().Be(1m);
    }

    [Fact]
    public async Task AnApprovedRunRefusedAtMarkPaid_IsRecomputedBackToDraft_ReapprovedAndPaid()
    {
        var seeded = await SeedAsync();
        var runId = await CreateAndApproveAsync(seeded.Maria.Id);
        await FileOneSilDayAsync(seeded);

        var pay = () => StepAsync(s => s.MarkPaidAsync(runId));
        await pay.Should().ThrowAsync<DomainException>().WithMessage(LeaveChanged);

        // An approved regular run that converts leave can be recomputed; it goes back to Draft.
        await StepAsync(s => s.ComputeAsync(runId));
        await using (var reader = NewContext())
        {
            var recomputed = (await new PayrollRunRepository(reader).GetWithEntriesAsync(runId))!;
            recomputed.Status.Should().Be(PayrollRunStatus.Draft);
            recomputed.Employees.Single().LeaveConversionPay.Should().Be(2_400m);
        }

        await StepAsync(s => s.ApproveAsync(runId));
        await StepAsync(s => s.MarkPaidAsync(runId));

        await using var check = NewContext();
        (await new PayrollRunRepository(check).GetWithEntriesAsync(runId))!.Status.Should().Be(PayrollRunStatus.Paid);
        (await check.LeaveBalances.FindAsync(seeded.Sil.Id))!.UsedDays.Should().Be(4m);
        (await check.EmployeeLoans.FindAsync(seeded.Loan.Id))!.RemainingBalance.Should().Be(2_000m);
    }

    [Fact]
    public async Task TurningTheConversionOffAnAbandonedRun_LetsAnotherRunConvert()
    {
        var seeded = await SeedAsync();
        var abandoned = await CreateAsync(seeded.Maria.Id);

        PayrollRunDto turnedOff = null!;
        await StepAsync(async s => turnedOff = await s.SetLeaveConversionAsync(abandoned, include: false));
        turnedOff.IncludesLeaveConversion.Should().BeFalse();
        turnedOff.Employees.Single().LeaveConversionPay.Should().Be(0m);
        turnedOff.Employees.Single().EmployeeName.Should().Be("Maria Santos");

        // The abandoned run still carries Maria's 13th month, unpaid, so the replacement leaves it out.
        var replacement = await CreateAsync(seeded.Maria.Id, thirteenthMonth: false);

        await using var reader = NewContext();
        (await new PayrollRunRepository(reader).GetWithEntriesAsync(abandoned))!.IncludesLeaveConversion.Should().BeFalse();
        (await new PayrollRunRepository(reader).GetWithEntriesAsync(replacement))!
            .Employees.Single().LeaveConversionPay.Should().Be(3_600m);
    }

    [Fact]
    public async Task LeaveBeyondDeMinimis_OnARegularRun_ReconcilesThrough2316Items34And48_AndThe1601C13thMonthLine()
    {
        // 12 unused SIL days: the first 10 are de minimis (12,000), the other 2 other benefits
        // (2,400). In May Maria was paid an 88,000 13th month advance, leaving 2,000 of the 90,000.
        var seeded = await SeedAsync(silTotal: 12m, silUsed: 0m);
        var may = ARun("PAY-2026-010", new(2026, 5, 1), new(2026, 5, 31), new(2026, 5, 31));
        may.Frequency = PayFrequency.Monthly;
        Context.PayrollRuns.Add(may);
        var mayEntry = AnEntry(may.Id, seeded.Maria.Id, regularPay: 36_500m);
        mayEntry.ThirteenthMonth = 88_000m;
        mayEntry.SSSEmployee = 1_750m;
        mayEntry.PhilHealthEmployee = 912.50m;
        mayEntry.PagIbigEmployee = 200m;
        mayEntry.WithholdingTax = 1_935.83m;
        Context.PayrollRunEmployees.Add(mayEntry);
        await Context.SaveChangesAsync();

        var runId = await CreateAndApproveAsync(seeded.Maria.Id);
        await StepAsync(s => s.MarkPaidAsync(runId));

        await using var reader = NewContext();
        // December: the 13th month due is (3 x 36,500) / 12 = 9,125 less the 88,000 paid: none.
        // Of the 2,400 other benefits 2,000 is exempt and 400 taxed at the 20% margin (403,650 +
        // 400 is still in that bracket): 80.00 on top of 1,935.83 = 2,015.83.
        // Gross 36,500 + 14,400 = 50,900.
        var entry = (await new PayrollRunRepository(reader).GetWithEntriesAsync(runId))!.Employees.Single();
        entry.ThirteenthMonth.Should().Be(0m);
        entry.LeaveConversionPay.Should().Be(14_400m);
        entry.LeaveConversionNonTaxable.Should().Be(12_000m);
        entry.LeaveConversionOtherBenefits.Should().Be(2_400m);
        entry.WithholdingTax.Should().Be(2_015.83m);
        entry.GrossPay.Should().Be(50_900m);

        // 2316 over May, November and December:
        //   Item 19 gross (36,500 + 88,000) + 36,500 + 50,900 = 211,900;
        //   13th month and other benefits 88,000 + 2,400 = 90,400: Item 34 90,000, Item 48 400;
        //   Item 35 de minimis 12,000; Item 36 shares 3 x 2,862.50 = 8,587.50;
        //   Item 52 taxable 211,900 - 90,000 - 12,000 - 8,587.50 = 101,312.50
        //     (= 3 x 33,637.50 + the 400 in Item 48);
        //   Item 25A withheld 1,935.83 + 1,935.83 + 2,015.83 = 5,887.49.
        var bir2316 = new Bir2316Service(new PayrollRunRepository(reader), new EmployeeRepository(reader),
            new CompanyRepository(reader), new Bir2316InputsRepository(reader));
        var cert = (await bir2316.BuildAsync(seeded.Maria.Id, 2026, new Bir2316ManualInputs()))!;
        cert.Item19_GrossCompensation.Should().Be(211_900m);
        cert.Item34_ThirteenthMonthAndBenefits.Should().Be(90_000m);
        cert.Item48_TaxableThirteenthMonth.Should().Be(400m);
        cert.Item35_DeMinimis.Should().Be(12_000m);
        cert.Item52_TotalTaxableCompensation.Should().Be(101_312.50m);
        cert.Item25A_PresentTaxWithheld.Should().Be(5_887.49m);

        // The 1601-Cs: May's 13th month line is 88,000; December's is the 2,000 of the other
        // benefits the exemption still covered, and its taxable 50,900 - 2,000 - 12,000 - 2,862.50
        // = 34,037.50 carries the 400 past it.
        var reports = new GovernmentReportService(new PayrollRunRepository(reader), new CompanyRepository(reader),
            new PayrollSettingsRepository(reader), new JanuaryClock(), Mock.Of<IBir2316Service>(),
            new EmployeeRepository(reader));
        var months = new[]
        {
            await reports.BuildAsync("1601c", 2026, 5),
            await reports.BuildAsync("1601c", 2026, 11),
            await reports.BuildAsync("1601c", 2026, 12),
        };
        decimal Line(GovernmentReportDto report, string label) => report.Summary.Single(l => l.Label == label).Amount;
        decimal Year(string label) => months.Sum(m => Line(m, label));

        Line(months[2], "13th month pay and other benefits").Should().Be(2_000m);
        Line(months[2], "De minimis benefits").Should().Be(12_000m);
        Line(months[2], "Total taxable compensation").Should().Be(34_037.50m);

        Year("Total amount of compensation").Should().Be(cert.Item19_GrossCompensation);
        Year("13th month pay and other benefits").Should().Be(cert.Item34_ThirteenthMonthAndBenefits);
        Year("De minimis benefits").Should().Be(cert.Item35_DeMinimis);
        Year("Total taxable compensation").Should().Be(cert.Item52_TotalTaxableCompensation);
        Year("Total taxes withheld").Should().Be(cert.Item25A_PresentTaxWithheld);
    }

    [Fact]
    public async Task SavePaid_WritesTheRunTheLoansAndTheBalances_InOneSave()
    {
        var seeded = await SeedAsync();
        var runId = await CreateAndApproveAsync(seeded.Maria.Id);

        await using (var context = NewContext())
        {
            var runs = new PayrollRunRepository(context);
            var run = (await runs.GetWithEntriesAsync(runId))!;
            var loan = (await context.EmployeeLoans.FindAsync(seeded.Loan.Id))!;
            var balance = (await context.LeaveBalances.FindAsync(seeded.Sil.Id))!;
            run.Status = PayrollRunStatus.Paid;
            loan.RemainingBalance = 2_000m;
            balance.UsedDays = 5m;

            var saves = 0;
            context.SavedChanges += (_, _) => saves++;
            await runs.SavePaidAsync(run, [loan], [balance]);
            saves.Should().Be(1);
        }

        await using var reader = NewContext();
        (await new PayrollRunRepository(reader).GetWithEntriesAsync(runId))!.Status.Should().Be(PayrollRunStatus.Paid);
        (await reader.EmployeeLoans.FindAsync(seeded.Loan.Id))!.RemainingBalance.Should().Be(2_000m);
        (await reader.LeaveBalances.FindAsync(seeded.Sil.Id))!.UsedDays.Should().Be(5m);
    }

    [Fact]
    public async Task ASecondFlaggedRunInTheYear_ForSomeoneAlreadyConverted_IsRefused()
    {
        var seeded = await SeedAsync();
        string firstRunNumber;
        await using (var context = NewContext())
            firstRunNumber = (await PayrollRuns(context).CreateAsync(December(seeded.Maria.Id))).RunNumber;

        // Without the 13th month, which the first run already carries unpaid.
        await using (var context = NewContext())
        {
            var act = () => PayrollRuns(context).CreateAsync(December(seeded.Maria.Id, thirteenthMonth: false));
            await act.Should().ThrowAsync<DomainException>()
                .WithMessage($"Maria Santos's leave for 2026 was already converted in {firstRunNumber}.");
        }
    }

    [Fact]
    public async Task GetLeaveConversionsInYear_FindsOnlyOtherRegularRunsOfThatYear_ThatConvertedTheEmployeesLeave()
    {
        var maria = AnEmployee("Santos", "Maria");
        var ana = AnEmployee("Reyes", "Ana");
        Context.Employees.AddRange(maria, ana);

        PayrollRun Run(string number, DateOnly periodEnd, PayrollRunType type = PayrollRunType.Regular,
                       PayrollRunStatus status = PayrollRunStatus.Draft)
        {
            var run = ARun(number, periodEnd.AddDays(-14), periodEnd, periodEnd, status);
            run.RunType = type;
            Context.PayrollRuns.Add(run);
            return run;
        }
        void Entry(PayrollRun run, Employee employee, decimal conversion)
        {
            var entry = AnEntry(run.Id, employee.Id);
            entry.LeaveConversionPay = conversion;
            Context.PayrollRunEmployees.Add(entry);
        }

        var counted = Run("PAY-2026-023", new(2026, 12, 15), status: PayrollRunStatus.Paid);
        Entry(counted, maria, 3_600m);
        var thisRun = Run("PAY-2026-024", new(2026, 12, 31));
        Entry(thisRun, maria, 3_600m);
        var lastYear = Run("PAY-2025-024", new(2025, 12, 31));
        Entry(lastYear, maria, 3_600m);
        var finalPay = Run("FP-2026-001", new(2026, 12, 10), PayrollRunType.FinalPay);
        Entry(finalPay, maria, 3_600m);
        var nothingConverted = Run("PAY-2026-021", new(2026, 11, 15));
        Entry(nothingConverted, maria, 0m);
        Entry(counted, ana, 1_200m);
        await Context.SaveChangesAsync();

        await using var reader = NewContext();
        var found = await new PayrollRunRepository(reader).GetLeaveConversionsInYearAsync(2026, [maria.Id], thisRun.Id);

        found.Should().Equal(new LeaveConvertedInRun(maria.Id, "PAY-2026-023"));
    }
}
