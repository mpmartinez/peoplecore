using FluentAssertions;
using Moq;
using PeopleCore.Application.Payroll.OpeningBalances;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using Xunit;
using static PeopleCore.Application.Tests.Payroll.OpeningBalanceFakes;

namespace PeopleCore.Application.Tests.Payroll;

/// <summary>
/// The 2316 certifies what the employee was paid before PeopleCore too: her opening balance for the
/// year adds to the boxes the spec lists, and the 13th month and other benefits on it share the
/// ₱90,000 exemption with the year's runs.
/// </summary>
public partial class Bir2316ServiceTests
{
    /// <summary>The service as the app builds it, reading opening balances from <paramref name="balances"/>.</summary>
    private Bir2316Service WithOpeningBalances(Mock<IPayrollOpeningBalanceRepository> balances)
        => new(_runRepo.Object, _employeeRepo.Object, _companyRepo.Object, _inputsRepo.Object, balances.Object);

    private Bir2316Service WithOpeningBalances(params PayrollOpeningBalance[] balances)
        => WithOpeningBalances(Holding(balances));

    /// <summary>
    /// April's run: 30,000 basic, 1,000 overtime, 500 holiday, 200 night differential, 800 taxable
    /// and 1,000 non-taxable allowances, 1,950 of contributions (1,000 + 750 + 200), 3,000 withheld.
    /// </summary>
    private void AprilIsPaid() => PaidRunsAre(
        Run(payDate: new DateOnly(2026, 4, 15), status: PayrollRunStatus.Paid, entries:
        [
            Entry(_employeeId, regularPay: 30_000m, overtimePay: 1_000m, holidayPay: 500m, nightDiffPay: 200m,
                taxableAllowances: 800m, nonTaxableAllowances: 1_000m, sss: 1_000m, philHealth: 750m, pagIbig: 200m,
                withholdingTax: 3_000m)
        ]));

    /// <summary>January to March, before PeopleCore.</summary>
    private PayrollOpeningBalance JanuaryToMarch() => OpeningBalance(_employeeId,
        basicSalary: 90_000m, thirteenthMonthPaid: 5_000m, otherBenefitsPaid: 10_000m, otherTaxablePay: 3_000m,
        deMinimis: 2_000m, otherNonTaxable: 1_500m, employeeContributions: 5_850m, taxWithheld: 9_000m,
        throughDate: new DateOnly(2026, 3, 31));

    [Fact]
    public async Task GetPreviewAsync_WithAnOpeningBalance_AddsItBoxByBox()
    {
        AprilIsPaid();
        var sut = WithOpeningBalances(JanuaryToMarch());

        var result = (await sut.GetPreviewAsync(_employeeId, 2026, CancellationToken.None))!;

        // Section A, non-taxable.
        result.Item34_ThirteenthMonthAndBenefits.Should().Be(15_000m);      // 5,000 + 10,000, inside the 90,000
        result.Item35_DeMinimis.Should().Be(2_000m);                        // 0 on the run + 2,000
        result.Item36_SssPhicPagibigContributions.Should().Be(7_800m);      // 1,950 + 5,850
        result.Item37_SalariesOtherForms.Should().Be(2_500m);               // 1,000 allowances + 1,500
        result.Item38_TotalNonTaxable.Should().Be(27_300m);                 // 15,000 + 2,000 + 7,800 + 2,500

        // Section B, taxable.
        result.Item39_BasicSalary.Should().Be(112_200m);                    // (30,000 - 1,950) + (90,000 - 5,850)
        result.Item44A_OtherAmount.Should().Be(500m);                       // the run's holiday pay alone
        result.Item44B_OtherAmount.Should().Be(200m);                       // the run's night differential alone
        result.Item48_TaxableThirteenthMonth.Should().Be(0m);
        result.Item50_OvertimePay.Should().Be(1_000m);                      // the run's overtime alone
        result.Item51A_OtherAmount.Should().Be(3_800m);                     // 800 allowances + 3,000 other taxable pay
        result.Item52_TotalTaxableCompensation.Should().Be(117_700m);       // 112,200 + 500 + 200 + 1,000 + 3,800

        // Item 19 is everything paid in the year: the run's 30,000 + 1,000 + 500 + 200 + 800 + 1,000
        // = 33,500, and the balance's 90,000 + 5,000 + 10,000 + 3,000 + 2,000 + 1,500 = 111,500.
        result.Item19_GrossCompensation.Should().Be(145_000m).And.Be(33_500m + 111_500m);
        result.Item21_TaxableFromPresent.Should().Be(117_700m);
        result.Item23_GrossTaxable.Should().Be(117_700m);                   // no previous employer
        result.Item25A_PresentTaxWithheld.Should().Be(12_000m);             // 3,000 + 9,000
        result.Item26_TotalTaxWithheld.Should().Be(12_000m);
        result.Item24_TaxDue.Should().Be(0m);                               // 117,700 is under 250,000

        result.OpeningBalanceThrough.Should().Be(new DateOnly(2026, 3, 31));
        // The certificate covers the months the balance does: from January, not April's period.
        result.PeriodFrom.Should().Be("01/01");
        result.PeriodTo.Should().Be("04/14");
    }

    [Fact]
    public async Task GetPreviewAsync_WithAnOpeningBalanceAndAPreviousEmployer_Item23AddsItem22()
    {
        AprilIsPaid();
        _inputsRepo.Setup(r => r.GetAsync(_employeeId, 2026, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new Bir2316Inputs
                   {
                       EmployeeId = _employeeId, Year = 2026,
                       Item22_PrevTaxableCompensation = 200_000m, Item25B_PrevTaxWithheld = 4_000m,
                   });
        var sut = WithOpeningBalances(JanuaryToMarch());

        var result = (await sut.GetPreviewAsync(_employeeId, 2026, CancellationToken.None))!;

        // Item 23 = 117,700 + 200,000 = 317,700 -> (317,700 - 250,000) x 15% = 10,155 due.
        result.Item21_TaxableFromPresent.Should().Be(117_700m);
        result.Item23_GrossTaxable.Should().Be(317_700m);
        result.Item24_TaxDue.Should().Be(10_155m);
        // Item 26 = 12,000 (3,000 + the balance's 9,000) + 4,000 from the previous employer.
        result.Item26_TotalTaxWithheld.Should().Be(16_000m);
    }

    [Theory]
    // balance 13th, balance other benefits, runs' 13th month -> Item 34, Item 48
    [InlineData(30_000, 25_000, 50_000, 90_000, 15_000)]   // 105,000 in all: 90,000 exempt, 15,000 over
    [InlineData(60_000, 20_000, 0, 80_000, 0)]             // the balance alone, inside the cap
    [InlineData(70_000, 30_000, 5_000, 90_000, 15_000)]    // the balance alone already past the cap
    [InlineData(0, 0, 100_000, 90_000, 10_000)]            // a balance with none: the runs alone
    public async Task GetPreviewAsync_SplitsTheBalancesAndTheRuns13thMonthAndOtherBenefitsAtThe90000Together(
        decimal balanceThirteenth, decimal balanceOtherBenefits, decimal runsThirteenth,
        decimal expectedNonTaxable, decimal expectedTaxable)
    {
        PaidRunsAre(
            Run(payDate: new DateOnly(2026, 12, 15), status: PayrollRunStatus.Paid, entries:
                [Entry(_employeeId, regularPay: 10_000m, thirteenthMonth: runsThirteenth)]));
        var sut = WithOpeningBalances(OpeningBalance(_employeeId,
            thirteenthMonthPaid: balanceThirteenth, otherBenefitsPaid: balanceOtherBenefits));

        var result = (await sut.GetPreviewAsync(_employeeId, 2026, CancellationToken.None))!;

        result.Item34_ThirteenthMonthAndBenefits.Should().Be(expectedNonTaxable);
        result.Item48_TaxableThirteenthMonth.Should().Be(expectedTaxable);
        (result.Item34_ThirteenthMonthAndBenefits + result.Item48_TaxableThirteenthMonth)
            .Should().Be(balanceThirteenth + balanceOtherBenefits + runsThirteenth);
        // Item 52 = 10,000 basic + the taxable part.
        result.Item52_TotalTaxableCompensation.Should().Be(10_000m + expectedTaxable);
    }

    [Fact]
    public async Task GetPreviewAsync_WithoutABalanceForTheEmployeeAndYear_IsTheCertificateTheRunsAloneGive()
    {
        AprilIsPaid();
        var withoutRepository = (await _sut.GetPreviewAsync(_employeeId, 2026, CancellationToken.None))!;
        var sut = WithOpeningBalances(
            OpeningBalance(_employeeId, year: 2025, basicSalary: 90_000m, taxWithheld: 9_000m),
            OpeningBalance(_otherEmployeeId, basicSalary: 90_000m, taxWithheld: 9_000m));

        var result = (await sut.GetPreviewAsync(_employeeId, 2026, CancellationToken.None))!;

        result.Should().BeEquivalentTo(withoutRepository);
        result.OpeningBalanceThrough.Should().BeNull();
        result.Item39_BasicSalary.Should().Be(28_050m);                     // 30,000 - 1,950
        result.Item25A_PresentTaxWithheld.Should().Be(3_000m);
        result.PeriodFrom.Should().Be("03/31");                             // April's period, as before
    }

    [Fact]
    public async Task GetPreviewAsync_WithABalanceButNoPaidRuns_IsStillNothingToCertify()
    {
        // Paid before PeopleCore only: the certificate is the earlier system's to issue.
        PaidRunsAre();
        var sut = WithOpeningBalances(JanuaryToMarch());

        (await sut.GetPreviewAsync(_employeeId, 2026, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task GetPreviewAsync_WithABalance_StartsThePeriodAtAHireDateLaterInTheYear()
    {
        // Hired February 2: the balance covers February and March, so the period starts there.
        AprilIsPaid();
        _employeeRepo.Setup(r => r.GetByIdAsync(_employeeId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(() =>
                     {
                         var employee = TheEmployee();
                         employee.HireDate = new DateOnly(2026, 2, 2);
                         return employee;
                     });
        var sut = WithOpeningBalances(JanuaryToMarch());

        var result = (await sut.GetPreviewAsync(_employeeId, 2026, CancellationToken.None))!;

        result.PeriodFrom.Should().Be("02/02");
    }

    [Fact]
    public async Task BuildWithDraftEntryAsync_AddsTheOpeningBalance()
    {
        // A final pay's certificate: the balance, February's Paid run and the draft entry.
        PaidRunsAre(
            Run(payDate: new DateOnly(2026, 2, 28), status: PayrollRunStatus.Paid, entries:
                [Entry(_employeeId, regularPay: 20_000m, withholdingTax: 1_000m)]));
        var sut = WithOpeningBalances(OpeningBalance(_employeeId, basicSalary: 20_000m, employeeContributions: 1_600m,
            taxWithheld: 700m, throughDate: new DateOnly(2026, 1, 31)));
        var draftRun = Run(payDate: new DateOnly(2026, 3, 31), status: PayrollRunStatus.Draft, entries: []);
        var draftEntry = Entry(_employeeId, regularPay: 10_000m, withholdingTax: 300m);

        var result = (await sut.BuildWithDraftEntryAsync(_employeeId, 2026, draftRun, draftEntry, CancellationToken.None))!;

        result.Item39_BasicSalary.Should().Be(48_400m);                     // 20,000 + 10,000 + (20,000 - 1,600)
        result.Item36_SssPhicPagibigContributions.Should().Be(1_600m);
        result.Item25A_PresentTaxWithheld.Should().Be(2_000m);              // 1,000 + 300 + 700
        result.OpeningBalanceThrough.Should().Be(new DateOnly(2026, 1, 31));
    }

    [Fact]
    public async Task BuildAllAsync_AddsEachEmployeesOwnBalance_ReadOnceForTheYear()
    {
        var run = Run(payDate: new DateOnly(2026, 4, 15), status: PayrollRunStatus.Paid, entries:
        [
            Entry(_otherEmployeeId, regularPay: 15_000m, withholdingTax: 800m),
            Entry(_employeeId, regularPay: 30_000m, withholdingTax: 2_000m)
        ]);
        PaidRunsInYearAre([_otherEmployeeId, _employeeId], run);
        EmployeesAre(AnEmployee(_otherEmployeeId, "Reyes", "Ana"), TheEmployee());
        var balances = Holding(
            OpeningBalance(_employeeId, basicSalary: 90_000m, employeeContributions: 5_000m, taxWithheld: 9_000m),
            OpeningBalance(_employeeId, year: 2025, basicSalary: 1_000_000m));
        var sut = WithOpeningBalances(balances);

        var result = await sut.BuildAllAsync(2026, CancellationToken.None);

        // Ana has no balance: her run alone.
        result[0].Item39_BasicSalary.Should().Be(15_000m);
        result[0].Item25A_PresentTaxWithheld.Should().Be(800m);
        result[0].OpeningBalanceThrough.Should().BeNull();
        // Juan: 30,000 + (90,000 - 5,000) = 115,000; 2,000 + 9,000 withheld. 2025's balance isn't his 2026.
        result[1].Item39_BasicSalary.Should().Be(115_000m);
        result[1].Item36_SssPhicPagibigContributions.Should().Be(5_000m);
        result[1].Item25A_PresentTaxWithheld.Should().Be(11_000m);
        result[1].OpeningBalanceThrough.Should().Be(new DateOnly(2026, 3, 31));

        balances.Verify(b => b.GetForEmployeesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), 2026, It.IsAny<CancellationToken>()), Times.Once);
        balances.Verify(b => b.GetAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
