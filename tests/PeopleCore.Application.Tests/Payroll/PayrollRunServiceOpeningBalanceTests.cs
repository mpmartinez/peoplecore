using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using Xunit;
using static PeopleCore.Application.Tests.Payroll.OpeningBalanceFakes;

namespace PeopleCore.Application.Tests.Payroll;

/// <summary>
/// A regular run's 13th month, its tax and the year-end leave conversion's de minimis days count
/// what the employee was paid before PeopleCore - her opening balance for the pay year.
/// </summary>
public partial class PayrollRunServiceTests
{
    /// <summary>The service as the app builds it, reading earlier-this-year figures with <paramref name="balances"/>.</summary>
    private PayrollRunService WithOpeningBalances(params PayrollOpeningBalance[] balances) => new(
        _runRepo.Object, _compensationRepo.Object, _allowanceRepo.Object, _loanRepo.Object, _settingsRepo.Object,
        new PayrollComputationService(), _attendanceBridge.Object, _employeeRepo.Object, _separations.Object,
        NullLogger<PayrollRunService>.Instance, _finalPay.Object, _yearEnd.Object, _clock,
        yearToDate: new PayrollYearToDate(_runRepo.Object, Holding(balances).Object));

    /// <summary>
    /// The employee of CreateAsync_Pays13thMonthFromTheYearsBasicLessWhatWasAlreadyPaid - 120,000 a
    /// month, paid semi-monthly - with one Paid 2026 run of 690,000 basic; the rest of the year's
    /// pay is on her opening balance.
    /// </summary>
    private (Guid EmployeeId, Func<PayrollRun?> SavedRun) At120000WithAPaidRun()
    {
        var employeeId = Guid.NewGuid();
        var savedRun = SetupRoundTripRepositories(new EmployeeCompensation
        {
            EmployeeId = employeeId, BasicSalary = 120_000m, PayFrequency = PayFrequency.SemiMonthly, TaxCode = "S"
        });
        var earlier = new PayrollRun
        {
            RunNumber = "PAY-2026-000", Status = PayrollRunStatus.Paid, PayDate = new DateOnly(2026, 1, 5)
        };
        earlier.Employees.Add(new PayrollRunEmployee { EmployeeId = employeeId, RegularPay = 690_000m });
        _runRepo.Setup(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync([earlier]);
        return (employeeId, savedRun);
    }

    [Fact]
    public async Task CreateAsync_Pays13thMonthFromTheBasicAndThe13thMonthOnTheOpeningBalanceToo()
    {
        var (employeeId, savedRun) = At120000WithAPaidRun();
        var sut = WithOpeningBalances(OpeningBalance(employeeId, basicSalary: 690_000m, thirteenthMonthPaid: 60_000m));

        await sut.CreateAsync(With13thMonth(RoundTripRequest(employeeId)), CancellationToken.None);

        // Basic earned earlier: 690,000 on the run + 690,000 on the balance = 1,380,000; this
        // half-month's 60,000 makes 1,440,000 / 12 = 120,000 due, less the balance's 60,000 already
        // paid: 60,000. The exemption used is the balance's 60,000, so 30,000 of it is left and the
        // other 30,000 is taxed at 25%: 7,500, + 10,381.25 on the regular half-month = 17,881.25 -
        // the figures of CreateAsync_Pays13thMonthFromTheYearsBasicLessWhatWasAlreadyPaid, where the
        // same amounts were all on Paid runs.
        var entry = savedRun()!.Employees.Single();
        entry.ThirteenthMonth.Should().Be(60_000m);
        entry.WithholdingTax.Should().Be(17_881.25m);
        entry.ThirteenthMonthPaidEarlierInYear.Should().Be(60_000m);
    }

    [Fact]
    public async Task CreateAsync_TaxesThe13thMonthPastTheExemptionTheOpeningBalancesOtherBenefitsUsed()
    {
        var (employeeId, savedRun) = At120000WithAPaidRun();
        var sut = WithOpeningBalances(OpeningBalance(employeeId, basicSalary: 690_000m, thirteenthMonthPaid: 60_000m,
            otherBenefitsPaid: 30_000m));

        await sut.CreateAsync(With13thMonth(RoundTripRequest(employeeId)), CancellationToken.None);

        // 13th month as above: 60,000. The balance used 60,000 + 30,000 = 90,000 of the exemption -
        // all of it - so the whole 60,000 is taxed at 25%: 15,000 + 10,381.25 = 25,381.25 (the
        // arithmetic of CreateAsync_TaxesThe13thMonthPastTheExemptionTheYearsOtherBenefitsAlsoUsed).
        var entry = savedRun()!.Employees.Single();
        entry.ThirteenthMonth.Should().Be(60_000m);
        entry.WithholdingTax.Should().Be(25_381.25m);
    }

    [Fact]
    public async Task CreateAsync_WithOnlyAnotherYearsOrAnotherEmployeesBalance_PaysWhatTheRunsAloneGive()
    {
        var (employeeId, savedRun) = At120000WithAPaidRun();
        var sut = WithOpeningBalances(
            OpeningBalance(employeeId, year: 2025, basicSalary: 690_000m, thirteenthMonthPaid: 60_000m),
            OpeningBalance(Guid.NewGuid(), basicSalary: 690_000m, thirteenthMonthPaid: 60_000m));

        await sut.CreateAsync(With13thMonth(RoundTripRequest(employeeId)), CancellationToken.None);

        // Neither balance is hers for 2026, so the run alone counts: (690,000 + this half-month's
        // 60,000) / 12 = 62,500 due, nothing paid. All 62,500 is inside the 90,000 exemption, so
        // the tax is only the half-month's 10,381.25.
        var entry = savedRun()!.Employees.Single();
        entry.ThirteenthMonth.Should().Be(62_500m);
        entry.WithholdingTax.Should().Be(10_381.25m);
        entry.ThirteenthMonthPaidEarlierInYear.Should().Be(0m);
    }

    [Fact]
    public async Task CreateAsync_ADecemberConversion_CountsTheOpeningBalancesDeMinimisDaysAsUsed()
    {
        // Maria at 1,200 a day converts 5 SIL days in December 2026. An earlier 2026 Paid run
        // converted 2 days as de minimis; her opening balance says 6 more were converted before
        // PeopleCore.
        var (maria, savedRun) = MariaAt36500();
        _runRepo.Setup(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>()))
                .ReturnsAsync([PaidConversion(maria.Id, new DateOnly(2026, 6, 30), new DateOnly(2026, 6, 30), deMinimisDays: 2m)]);
        ConvertibleDays(maria.Id, new LeavePaidOut(SilBalance(maria.Id, 5m), 5m));
        var sut = WithOpeningBalances(OpeningBalance(maria.Id, deMinimisLeaveDays: 6m));

        await sut.CreateAsync(DecemberRequest(maria.Id));

        // 10 - 2 - 6 = 2 days left: 2 x 1,200 = 2,400 de minimis; the other 3 days, 3,600, are
        // other benefits. 5 x 1,200 = 6,000 in all.
        var entry = savedRun()!.Employees.Single();
        entry.LeaveConversionPay.Should().Be(6_000m);
        entry.LeaveConversionNonTaxable.Should().Be(2_400m);
        entry.LeaveConversionOtherBenefits.Should().Be(3_600m);
    }

    [Fact]
    public async Task MarkPaidAsync_OnAConversionComputedWithTheOpeningBalance_StillPrices_AndPays()
    {
        // As above, but with no earlier run: 10 - 6 = 4 days left, 4,800 de minimis and 1,200
        // other benefits. Paying it re-prices the days with the same 4 days left.
        var (maria, savedRun) = MariaAt36500();
        ConvertibleDays(maria.Id, new LeavePaidOut(SilBalance(maria.Id, 5m), 5m));
        var sut = WithOpeningBalances(OpeningBalance(maria.Id, deMinimisLeaveDays: 6m));
        await sut.CreateAsync(DecemberRequest(maria.Id));
        var run = savedRun()!;
        run.Employees.Single().LeaveConversionNonTaxable.Should().Be(4_800m);
        run.Status = PayrollRunStatus.Approved;

        await sut.MarkPaidAsync(run.Id);

        run.Status.Should().Be(PayrollRunStatus.Paid);
    }

    [Fact]
    public async Task MarkPaidAsync_WhenA13thMonthWasPaidElsewhereSinceCompute_CountsTheOpeningBalanceToo()
    {
        // Computed with 10,000 paid earlier: 6,000 on PAY-2026-012 and 4,000 on her opening
        // balance. Since then PAY-2026-020 paid 3,000 more: 6,000 + 3,000 + 4,000 = 13,000 > 10,000.
        // (The Paid runs alone, 9,000, would still look like no more than the entry netted.)
        var (run, maria) = ApprovedDecemberWithThe13thMonth(paidEarlier: 10_000m);
        _runRepo.Setup(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            PaidThirteenthMonth(maria.Id, "PAY-2026-012", 6_000m, paidAt: new DateTime(2026, 6, 30)),
            PaidThirteenthMonth(maria.Id, "PAY-2026-020", 3_000m, paidAt: new DateTime(2026, 11, 30)),
        ]);
        var sut = WithOpeningBalances(OpeningBalance(maria.Id, thirteenthMonthPaid: 4_000m));

        var act = () => sut.MarkPaidAsync(run.Id);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(
            "Maria Santos's 13th month was paid on PAY-2026-020 after this payroll was computed; recompute it before paying.");
        run.Status.Should().Be(PayrollRunStatus.Approved);
        VerifyNothingSavedAsPaid();
    }

    [Fact]
    public async Task MarkPaidAsync_WhenThe13thMonthOnTheRunsAndTheBalanceIsWhatTheEntryNetted_PaysIt()
    {
        // 6,000 on PAY-2026-012 + 4,000 on the balance = the 10,000 the entry was computed with.
        var (run, maria) = ApprovedDecemberWithThe13thMonth(paidEarlier: 10_000m);
        _runRepo.Setup(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync(
            [PaidThirteenthMonth(maria.Id, "PAY-2026-012", 6_000m, paidAt: new DateTime(2026, 6, 30))]);
        var sut = WithOpeningBalances(OpeningBalance(maria.Id, thirteenthMonthPaid: 4_000m));

        await sut.MarkPaidAsync(run.Id);

        run.Status.Should().Be(PayrollRunStatus.Paid);
    }

    [Fact]
    public async Task MarkPaidAsync_WhenTheOpeningBalances13thMonthRoseSinceCompute_IsRefused()
    {
        // Computed with nothing paid earlier; since then HR recorded 4,000 of 13th month paid before
        // PeopleCore. No Paid run paid any, so there is no run to name.
        var (run, _) = ApprovedDecemberWithThe13thMonth(paidEarlier: 0m);
        var sut = WithOpeningBalances(OpeningBalance(run.Employees.Single().EmployeeId, thirteenthMonthPaid: 4_000m));

        var act = () => sut.MarkPaidAsync(run.Id);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(
            "Maria Santos's 13th month paid before PeopleCore has changed since this payroll was computed; recompute it before paying.");
        VerifyNothingSavedAsPaid();
    }

    [Fact]
    public async Task MarkPaidAsync_WhenTheOpeningBalances13thMonthWasLoweredSinceCompute_IsRefused()
    {
        // Computed with 10,000 paid earlier: 6,000 on PAY-2026-012 and 4,000 on her opening
        // balance. HR has since corrected the balance to 1,000: 6,000 + 1,000 = 7,000, not the
        // 10,000 the entry netted, so it would now underpay 3,000. PAY-2026-012 paid before the
        // compute, so it isn't the run to name.
        var (run, maria) = ApprovedDecemberWithThe13thMonth(paidEarlier: 10_000m);
        _runRepo.Setup(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync(
            [PaidThirteenthMonth(maria.Id, "PAY-2026-012", 6_000m, paidAt: new DateTime(2026, 6, 30))]);
        var balance = OpeningBalance(maria.Id, thirteenthMonthPaid: 4_000m);
        var sut = WithOpeningBalances(balance);
        balance.ThirteenthMonthPaid = 1_000m;

        var act = () => sut.MarkPaidAsync(run.Id);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(
            "Maria Santos's 13th month paid before PeopleCore has changed since this payroll was computed; recompute it before paying.");
        run.Status.Should().Be(PayrollRunStatus.Approved);
        VerifyNothingSavedAsPaid();
    }

    [Fact]
    public async Task MarkPaidAsync_WhenTheOpeningBalancesDeMinimisDaysChangedSinceCompute_IsRefused()
    {
        // The entry converted 3 SIL days as 3,600 of de minimis, computed with all ten days left.
        // HR has since recorded 8 days converted before PeopleCore: only 2 are left, so the same
        // 3 days now split 2,400 de minimis and 1,200 other benefits.
        var (run, maria, loan) = ApprovedDecemberConversion();
        ConvertibleDays(maria.Id, new LeavePaidOut(SilBalance(maria.Id, 3m), 3m));
        var sut = WithOpeningBalances(OpeningBalance(maria.Id, deMinimisLeaveDays: 8m));

        var act = () => sut.MarkPaidAsync(run.Id);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be(
            "Maria Santos's convertible leave has changed since this payroll was computed; recompute it before paying.");
        run.Status.Should().Be(PayrollRunStatus.Approved);
        loan.RemainingBalance.Should().Be(5_000m);
    }
}
