using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PeopleCore.Application.Payroll.DTOs;
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
/// A December payroll through the real repositories and Postgres, for an employee whose January to
/// October were paid before PeopleCore: her opening balance counts toward the 13th month and the
/// de minimis leave days, and paying the run re-reads the same figures.
/// <para>
/// Maria Santos earns 36,500 a month, paid monthly: 1,200.00 a day. November is Paid on PeopleCore.
/// Her 2026 opening balance: 365,000 basic, a 10,000 13th month and 8 leave days converted as de
/// minimis.
/// </para>
/// </summary>
public class PayrollYearToDateDbTests : DatabaseTestBase
{
    public PayrollYearToDateDbTests(PostgresFixture fixture) : base(fixture) { }

    private static PayrollRunService PayrollRuns(AppDbContext context)
    {
        var bridge = new Mock<IPayrollAttendanceBridge>();
        bridge.Setup(b => b.BuildAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(),
                                       It.IsAny<CancellationToken>()))
              .ReturnsAsync(new AttendanceBridgeResult(new Dictionary<Guid, PayrollAttendanceInput>(), []));
        var runs = new PayrollRunRepository(context);
        return new PayrollRunService(
            runs, new EmployeeCompensationRepository(context), new EmployeeAllowanceRepository(context),
            new EmployeeLoanRepository(context), new PayrollSettingsRepository(context), new PayrollComputationService(),
            bridge.Object, new EmployeeRepository(context), new SeparationRepository(context),
            NullLogger<PayrollRunService>.Instance, finalPay: null,
            yearEndLeave: new YearEndLeaveConversion(new LeaveBalanceRepository(context),
                new LeaveRequestRepository(context), new LeaveTypeRepository(context)),
            yearToDate: new PayrollYearToDate(runs, new PayrollOpeningBalanceRepository(context)));
    }

    private async Task<Employee> SeedAsync()
    {
        var maria = AnEmployee("Santos", "Maria");
        Context.Employees.Add(maria);
        Context.Companies.Add(ACompany());
        Context.EmployeeCompensations.Add(new EmployeeCompensation
        {
            EmployeeId = maria.Id, BasicSalary = 36_500m, PayFrequency = PayFrequency.Monthly,
        });
        var sil = new LeaveType
        {
            Name = "Service Incentive Leave", Code = "SIL", MaxDaysPerYear = 5m,
            ConvertsAtYearEnd = true, CountsAsVacationForDeMinimis = true,
        };
        Context.LeaveTypes.Add(sil);
        Context.LeaveBalances.Add(new LeaveBalance { EmployeeId = maria.Id, LeaveTypeId = sil.Id, Year = 2026, TotalDays = 5m, UsedDays = 2m });

        var november = ARun("PAY-2026-022", new(2026, 11, 1), new(2026, 11, 30), new(2026, 11, 30));
        november.Frequency = PayFrequency.Monthly;
        Context.PayrollRuns.Add(november);
        Context.PayrollRunEmployees.Add(AnEntry(november.Id, maria.Id, regularPay: 36_500m));

        Context.PayrollOpeningBalances.Add(new PayrollOpeningBalance
        {
            EmployeeId = maria.Id, Year = 2026, ThroughDate = new DateOnly(2026, 10, 31),
            BasicSalary = 365_000m, ThirteenthMonthPaid = 10_000m, DeMinimisLeaveDays = 8m,
        });
        await Context.SaveChangesAsync();
        return maria;
    }

    [Fact]
    public async Task ADecemberRun_CountsTheOpeningBalance_AndIsPaidOnTheSameFigures()
    {
        var maria = await SeedAsync();

        Guid runId;
        await using (var context = NewContext())
            runId = (await PayrollRuns(context).CreateAsync(new CreatePayrollRunRequest(
                new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 31), new DateOnly(2026, 12, 29), PayFrequency.Monthly,
                [new PayrollRunEmployeeInput(maria.Id, IncludeThirteenthMonth: true)], IncludeLeaveConversion: true))).Id;
        await using (var context = NewContext())
            await PayrollRuns(context).ApproveAsync(runId);
        await using (var context = NewContext())
            await PayrollRuns(context).MarkPaidAsync(runId);

        await using var reader = NewContext();
        var run = (await new PayrollRunRepository(reader).GetWithEntriesAsync(runId))!;
        run.Status.Should().Be(PayrollRunStatus.Paid);
        var entry = run.Employees.Single();
        // 13th month: (365,000 balance + 36,500 November + 36,500 December) / 12 = 438,000 / 12 =
        // 36,500, less the balance's 10,000 already paid = 26,500.
        entry.ThirteenthMonth.Should().Be(26_500m);
        entry.ThirteenthMonthPaidEarlierInYear.Should().Be(10_000m);
        entry.BasicEarnedEarlierInYear.Should().Be(401_500m);
        entry.ExemptUsedEarlierInYear.Should().Be(10_000m);
        // 3 SIL days x 1,200 = 3,600; 10 - 8 = 2 de minimis days left: 2,400, and 1,200 other benefits.
        entry.LeaveConversionPay.Should().Be(3_600m);
        entry.LeaveConversionNonTaxable.Should().Be(2_400m);
        entry.LeaveConversionOtherBenefits.Should().Be(1_200m);
    }

    [Fact]
    public async Task ADecemberRun_WhoseOpeningBalancesBasicChangedAfterCompute_IsNotPaid()
    {
        var maria = await SeedAsync();

        Guid runId;
        await using (var context = NewContext())
            runId = (await PayrollRuns(context).CreateAsync(new CreatePayrollRunRequest(
                new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 31), new DateOnly(2026, 12, 29), PayFrequency.Monthly,
                [new PayrollRunEmployeeInput(maria.Id, IncludeThirteenthMonth: true)]))).Id;
        await using (var context = NewContext())
            await PayrollRuns(context).ApproveAsync(runId);
        await using (var context = NewContext())
        {
            var balance = await context.PayrollOpeningBalances.SingleAsync(b => b.EmployeeId == maria.Id);
            balance.BasicSalary = 400_000m;
            await context.SaveChangesAsync();
        }

        await using (var context = NewContext())
        {
            var act = () => PayrollRuns(context).MarkPaidAsync(runId);
            await act.Should().ThrowAsync<DomainException>().WithMessage(
                "Maria Santos's pay before PeopleCore has changed since this payroll was computed; recompute it before paying.");
        }
        await using var reader = NewContext();
        (await new PayrollRunRepository(reader).GetWithEntriesAsync(runId))!.Status.Should().Be(PayrollRunStatus.Approved);
    }

    [Fact]
    public async Task ForAsync_ReadsTheRunsAndTheBalanceFromPostgres()
    {
        var maria = await SeedAsync();

        await using var context = NewContext();
        var sut = new PayrollYearToDate(new PayrollRunRepository(context), new PayrollOpeningBalanceRepository(context));
        var ytd = await sut.ForAsync([maria.Id], 2026, excludeRunId: null);

        // 36,500 November + 365,000 = 401,500 basic; the balance's 10,000 13th month, all of the
        // exemption used; its 8 days.
        ytd[maria.Id].Should().Be(new YearToDate(401_500m, 10_000m, 10_000m, 8m));
        (await sut.ForEmployeeAsync(maria.Id, 2026, excludeRunId: null)).Should().Be(ytd[maria.Id]);
        (await sut.ForAsync([maria.Id], 2025, excludeRunId: null))[maria.Id].Should().Be(YearToDate.None);
    }
}
