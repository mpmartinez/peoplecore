using FluentAssertions;
using Moq;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Application.Payroll.OpeningBalances;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using Xunit;
using static PeopleCore.Application.Tests.Payroll.OpeningBalanceFakes;

namespace PeopleCore.Application.Tests.Payroll;

/// <summary>
/// PayrollYearToDate: the year's Paid runs plus the opening balance, the one source every
/// "earlier this year" figure reads.
/// </summary>
public class PayrollYearToDateTests
{
    private readonly Mock<IPayrollRunRepository> _runs = new();
    private readonly Guid _maria = Guid.NewGuid();
    private readonly Guid _juan = Guid.NewGuid();

    private static PayrollRun Paid(string runNumber, DateOnly payDate, params PayrollRunEmployee[] entries)
    {
        var run = new PayrollRun { RunNumber = runNumber, PayDate = payDate, Status = PayrollRunStatus.Paid };
        run.Employees.AddRange(entries);
        return run;
    }

    private static PayrollRunEmployee Entry(Guid employeeId, decimal regularPay = 0m, decimal thirteenthMonth = 0m,
        decimal dailyRate = 0m, decimal leavePay = 0m, decimal leaveDeMinimis = 0m) => new()
    {
        EmployeeId = employeeId, RegularPay = regularPay, ThirteenthMonth = thirteenthMonth, DailyRate = dailyRate,
        LeaveConversionPay = leavePay, LeaveConversionNonTaxable = leaveDeMinimis,
    };

    [Fact]
    public async Task ForAsync_AddsTheOpeningBalanceToTheYearsPaidRuns()
    {
        // Maria's 2026 Paid runs: April 36,500 basic; May 36,500 basic, a 5,000 13th month and 3
        // leave days converted at 1,200 of which 2 were de minimis (2,400) and 1 other benefits.
        _runs.Setup(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            Paid("PAY-2026-004", new(2026, 4, 30), Entry(_maria, regularPay: 36_500m)),
            Paid("PAY-2026-005", new(2026, 5, 31), Entry(_maria, regularPay: 36_500m, thirteenthMonth: 5_000m,
                dailyRate: 1_200m, leavePay: 3_600m, leaveDeMinimis: 2_400m)),
        ]);
        // Before PeopleCore (Jan-Mar): 109,500 basic, 3,000 13th month, 7,000 other benefits, 1.5
        // leave days as de minimis.
        var balances = Holding(OpeningBalance(_maria, basicSalary: 109_500m, thirteenthMonthPaid: 3_000m,
            otherBenefitsPaid: 7_000m, deMinimisLeaveDays: 1.5m));
        var sut = new PayrollYearToDate(_runs.Object, balances.Object);

        var ytd = (await sut.ForAsync([_maria], 2026, excludeRunId: null))[_maria];

        // Basic: 36,500 + 36,500 + 109,500 = 182,500.
        // 13th month: 5,000 + 3,000 = 8,000.
        // Exemption used: the runs' 13th month and other benefits (5,000 + 1,200) + the balance's
        //   3,000 + 7,000 = 16,200.
        // De minimis days: 2,400 / 1,200 = 2 + 1.5 = 3.5, leaving 6.5.
        ytd.Should().Be(new YearToDate(182_500m, 8_000m, 16_200m, 3.5m));
        ytd.DeMinimisLeaveDaysLeft.Should().Be(6.5m);
    }

    [Fact]
    public async Task ForAsync_LeavesOutTheExcludedRunAndOtherEmployees_AndGivesEveryoneAskedFor()
    {
        var current = Paid("PAY-2026-006", new(2026, 6, 30), Entry(_maria, regularPay: 99_999m));
        _runs.Setup(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            Paid("PAY-2026-004", new(2026, 4, 30), Entry(_maria, regularPay: 36_500m), Entry(Guid.NewGuid(), regularPay: 50_000m)),
            current,
        ]);
        var nobody = Guid.NewGuid();
        var sut = new PayrollYearToDate(_runs.Object, Holding(OpeningBalance(_juan, basicSalary: 20_000m)).Object);

        var ytd = await sut.ForAsync([_maria, _juan, nobody], 2026, excludeRunId: current.Id);

        // Maria: only April's 36,500 - the excluded run and the other employee don't count.
        ytd[_maria].Should().Be(new YearToDate(36_500m, 0m, 0m, 0m));
        // Juan: no runs, his balance alone. Nobody: nothing at all, still listed.
        ytd[_juan].Should().Be(new YearToDate(20_000m, 0m, 0m, 0m));
        ytd[nobody].Should().Be(YearToDate.None);
        ytd[nobody].DeMinimisLeaveDaysLeft.Should().Be(10m);
        ytd.Should().HaveCount(3);
    }

    [Fact]
    public async Task ForAsync_ReadsTheRunsAndTheBalancesOnce_ForTheYearAskedFor()
    {
        _runs.Setup(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var balances = Holding(OpeningBalance(_maria, year: 2025, basicSalary: 400_000m), OpeningBalance(_maria, basicSalary: 1_000m));
        var sut = new PayrollYearToDate(_runs.Object, balances.Object);

        var ytd = await sut.ForAsync([_maria, _juan], 2026, excludeRunId: null);

        // 2025's balance is another year's.
        ytd[_maria].BasicEarned.Should().Be(1_000m);
        _runs.Verify(r => r.GetPaidRunsInYearAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        balances.Verify(b => b.GetForEmployeesAsync(It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2), 2026,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ForAsync_WithoutOpeningBalances_IsThePaidRunsAlone()
    {
        _runs.Setup(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync(
            [Paid("PAY-2026-004", new(2026, 4, 30), Entry(_maria, regularPay: 36_500m, thirteenthMonth: 1_000m))]);

        var ytd = await new PayrollYearToDate(_runs.Object).ForAsync([_maria], 2026, excludeRunId: null);

        ytd[_maria].Should().Be(new YearToDate(36_500m, 1_000m, 1_000m, 0m));
    }

    [Fact]
    public async Task ForAsync_ToleratesARepositoryThatReturnsNothing()
    {
        var sut = new PayrollYearToDate(_runs.Object, new Mock<IPayrollOpeningBalanceRepository>().Object);

        var ytd = await sut.ForAsync([_maria], 2026, excludeRunId: null);

        ytd[_maria].Should().Be(YearToDate.None);
    }

    [Fact]
    public async Task ForEmployeeAsync_ReadsHerRunsAlone_KeepsOnlyPaidRunsOfTheYear_AndAddsHerBalance()
    {
        var current = Paid("FP-2026-001", new(2026, 3, 31), Entry(_maria, regularPay: 15_600m));
        var draft = Paid("PAY-2026-002", new(2026, 2, 28), Entry(_maria, regularPay: 1m));
        draft.Status = PayrollRunStatus.Approved;
        _runs.Setup(r => r.GetPaidRunsForEmployeeInYearAsync(_maria, 2026, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            Paid("PAY-2026-001", new(2026, 1, 31), Entry(_maria, regularPay: 36_500m, thirteenthMonth: 500m)),
            Paid("PAY-2025-024", new(2025, 12, 31), Entry(_maria, regularPay: 2m)),
            draft,
            current,
        ]);
        var sut = new PayrollYearToDate(_runs.Object,
            Holding(OpeningBalance(_maria, basicSalary: 10_000m, thirteenthMonthPaid: 250m, deMinimisLeaveDays: 4m)).Object);

        var ytd = await sut.ForEmployeeAsync(_maria, 2026, excludeRunId: current.Id);

        // January's 36,500 + 10,000 = 46,500 basic; 500 + 250 = 750 13th month, which is also all
        // of the exemption used; the balance's 4 de minimis days.
        ytd.Should().Be(new YearToDate(46_500m, 750m, 750m, 4m));
        _runs.Verify(r => r.GetPaidRunsInYearAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PaidBeforeAsync_CountsOnlyRunsPaidBeforeTheDate_PlusTheYearsBalance()
    {
        _runs.Setup(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            Paid("PAY-2026-010", new(2026, 5, 20), Entry(_maria, thirteenthMonth: 10_000m)),
            Paid("PAY-2026-024", new(2026, 12, 1), Entry(_maria, thirteenthMonth: 60_000m)),
        ]);
        var sut = new PayrollYearToDate(_runs.Object,
            Holding(OpeningBalance(_maria, thirteenthMonthPaid: 20_000m, otherBenefitsPaid: 5_000m)).Object);

        var ytd = await sut.PaidBeforeAsync([_maria], new DateOnly(2026, 12, 1));

        // May's 10,000 only (December's run is paid on the 1st, not before it), + 20,000 + 5,000.
        ytd[_maria].ExemptUsed.Should().Be(35_000m);
        ytd[_maria].ThirteenthMonthPaid.Should().Be(30_000m);
    }

    [Theory]
    [InlineData(11, 30, true)]    // through November: before December
    [InlineData(12, 1, false)]    // through a day in December: December's own month
    [InlineData(12, 31, false)]
    public async Task PaidBeforeAsync_CountsTheBalance_OnlyForAMonthAfterItsThroughDatesMonth(int month, int day,
        bool counted)
    {
        var sut = new PayrollYearToDate(_runs.Object, Holding(OpeningBalance(_maria, thirteenthMonthPaid: 20_000m,
            otherBenefitsPaid: 5_000m, throughDate: new DateOnly(2026, month, day))).Object);

        var ytd = await sut.PaidBeforeAsync([_maria], new DateOnly(2026, 12, 1));

        ytd[_maria].ExemptUsed.Should().Be(counted ? 25_000m : 0m);
    }
}
