using FluentAssertions;
using Moq;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Organization.Interfaces;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Organization;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using PeopleCore.Domain.Payroll;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class Bir2316ServiceTests
{
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _otherEmployeeId = Guid.NewGuid();

    private readonly Mock<IPayrollRunRepository> _runRepo = new();
    private readonly Mock<IEmployeeRepository> _employeeRepo = new();
    private readonly Mock<ICompanyRepository> _companyRepo = new();
    private readonly Mock<IBir2316InputsRepository> _inputsRepo = new();
    private readonly Bir2316Service _sut;

    public Bir2316ServiceTests()
    {
        _employeeRepo.Setup(r => r.GetByIdAsync(_employeeId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(() => TheEmployee());
        _companyRepo.Setup(r => r.GetDefaultAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(() => TheCompany());
        // Default: nothing saved. Tests that need saved inputs override this per-employee stub
        // (GetAsync) or replace this one (GetForYearAsync) directly.
        _inputsRepo.Setup(r => r.GetForYearAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new Dictionary<Guid, Bir2316Inputs>());

        // Default: nothing paid. Tests that need runs call PaidRunsAre.
        PaidRunsAre();

        _sut = new Bir2316Service(_runRepo.Object, _employeeRepo.Object, _companyRepo.Object, _inputsRepo.Object);
    }

    // ------------------------------------------------------------------
    // Aggregation across an employee's paid runs
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetPreviewAsync_SumsRegularPayOvertimeAndWithholdingAcrossPaidRuns()
    {
        PaidRunsAre(
            Run(payDate: new DateOnly(2026, 1, 15), status: PayrollRunStatus.Paid, entries:
            [
                Entry(_employeeId,
                    regularPay: 20_000m, overtimePay: 1_500m, withholdingTax: 2_000m,
                    sss: 900m, philHealth: 500m, pagIbig: 100m),
                // Another employee's entry, riding along in the same run. GetPaidRunsForEmployeeInYearAsync
                // returns whole runs (see its remarks), so the aggregation has to pick out only its
                // own line - exactly as PayslipService.GetMyPayslipsAsync already does.
                Entry(_otherEmployeeId,
                    regularPay: 99_000m, overtimePay: 9_900m, withholdingTax: 9_900m,
                    sss: 9_900m, philHealth: 9_900m, pagIbig: 9_900m)
            ]),
            Run(payDate: new DateOnly(2026, 1, 31), status: PayrollRunStatus.Paid, entries:
            [
                Entry(_employeeId,
                    regularPay: 21_000m, overtimePay: 500m, withholdingTax: 2_200m,
                    sss: 900m, philHealth: 500m, pagIbig: 100m)
            ]));

        var result = await _sut.GetPreviewAsync(_employeeId, 2026, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Year.Should().Be(2026);
        // Basic is certified net of the employee's contributions, which Item 36 reports instead:
        // (20,000 - 1,500) + (21,000 - 1,500).
        result.Item39_BasicSalary.Should().Be(38_000m);
        result.Item50_OvertimePay.Should().Be(2_000m);             // 1,500 + 500
        result.Item25A_PresentTaxWithheld.Should().Be(4_200m);     // 2,000 + 2,200
        result.Item36_SssPhicPagibigContributions.Should().Be(3_000m); // (900+500+100) x 2
        result.Item19_GrossCompensation.Should().Be(43_000m);      // 41,000 basic + 2,000 overtime

        // Not one centavo of the other employee's line leaked in.
        result.Item39_BasicSalary.Should().NotBe(140_000m);
    }

    [Fact]
    public async Task GetPreviewAsync_OverAYearOfEngineComputedPay_CountsContributionsOnce_AndTaxesWhatTheEngineTaxed()
    {
        // Twelve monthly runs computed by the real payroll engine: basic 50,000, a taxable
        // allowance of 2,000 and a non-taxable one of 1,500. SSS is pinned at 5% (the table
        // moves), PhilHealth and Pag-IBIG are the statutory defaults:
        //   SSS 50,000 x 5% = 2,500; PhilHealth 50,000 x 5% / 2 = 1,250; Pag-IBIG 10,000 x 2% = 200
        //   => 3,950 of employee contributions a month.
        var engine = new PayrollComputationService();
        var rates = new ContributionRates { SSSEmployeeRate = 0.05m, SSSEmployerRate = 0.10m };
        var compensation = new EmployeeCompensation
        {
            EmployeeId = _employeeId, BasicSalary = 50_000m, PayFrequency = PayFrequency.Monthly,
            Allowances =
            [
                new EmployeeAllowance { EmployeeId = _employeeId, Amount = 2_000m, IsTaxable = true },
                new EmployeeAllowance { EmployeeId = _employeeId, Amount = 1_500m, IsTaxable = false },
            ],
        };
        var runs = Enumerable.Range(1, 12).Select(month =>
        {
            var start = new DateOnly(2026, month, 1);
            var run = new PayrollRun
            {
                RunNumber = $"PAY-2026-{month:D3}", PeriodStart = start, PeriodEnd = start.AddMonths(1).AddDays(-1),
                PayDate = start.AddMonths(1).AddDays(-1), Frequency = PayFrequency.Monthly, Status = PayrollRunStatus.Paid,
            };
            run.Employees.Add(engine.Compute(compensation, run, rates: rates));
            return run;
        }).ToArray();
        PaidRunsAre(runs);

        // The engine withheld against 50,000 + 2,000 - 3,950 = 48,050 a month: annualised,
        // 576,600 -> 22,500 + 20% x 176,600 = 57,820 a year -> 4,818.33 a month.
        var entries = runs.Select(r => r.Employees.Single()).ToList();
        entries.Should().AllSatisfy(e =>
        {
            (e.SSSEmployee + e.PhilHealthEmployee + e.PagIbigEmployee).Should().Be(3_950m);
            e.WithholdingTax.Should().Be(engine.ComputeWithholdingTax(48_050m, PayFrequency.Monthly)).And.Be(4_818.33m);
        });

        var result = (await _sut.GetPreviewAsync(_employeeId, 2026, CancellationToken.None))!;

        // Item 19 is what was actually paid - 12 x (50,000 + 2,000 + 1,500) - with the
        // contributions inside the basic counted once, in Item 36, and not again in Item 39.
        result.Item19_GrossCompensation.Should().Be(642_000m).And.Be(entries.Sum(e => e.GrossPay));
        result.Item36_SssPhicPagibigContributions.Should().Be(47_400m);   // 12 x 3,950
        result.Item38_TotalNonTaxable.Should().Be(65_400m);               // 12 x (1,500 + 3,950)
        result.Item39_BasicSalary.Should().Be(552_600m);                  // 12 x (50,000 - 3,950)

        // Item 23 is the engine's withholding base over the year: 12 x 48,050.
        result.Item52_TotalTaxableCompensation.Should().Be(576_600m);
        result.Item23_GrossTaxable.Should().Be(576_600m);

        // So the year's tax due is the tax the engine was withholding towards; the 4 centavos
        // are twelve roundings of 4,818.333... (12 x 4,818.33 = 57,819.96).
        result.Item24_TaxDue.Should().Be(57_820m);
        result.Item25A_PresentTaxWithheld.Should().Be(57_819.96m);
    }

    [Fact]
    public async Task GetPreviewAsync_CertifiesHolidayNightDiffAndTaxableAllowances()
    {
        // PayrollComputationService withholds against
        // regularPay + overtimePay + holidayPay + nightDiffPay + taxableAllowances. Every one of
        // those five components has to reach the certificate's taxable total, or Item 21/23
        // understates what tax was actually withheld against - manufacturing a false
        // "tax due < tax withheld" result for any employee who worked a holiday, drew night
        // differential, or received a taxable allowance.
        PaidRunsAre(
            Run(payDate: new DateOnly(2026, 1, 15), status: PayrollRunStatus.Paid, entries:
            [
                Entry(_employeeId,
                    regularPay: 20_000m, overtimePay: 1_000m,
                    holidayPay: 800m, nightDiffPay: 300m, taxableAllowances: 1_500m)
            ]));

        var result = await _sut.GetPreviewAsync(_employeeId, 2026, CancellationToken.None);

        result.Should().NotBeNull();

        // All three components are reflected somewhere in Section B's taxable items.
        result!.Item44A_OtherAmount.Should().Be(800m);
        result.Item44B_OtherAmount.Should().Be(300m);
        result.Item51A_OtherAmount.Should().Be(1_500m);

        // The acceptance test: the sum of the taxable Section B items the service populates must
        // equal exactly what PayrollComputationService treats as the taxable base, so Item 24 and
        // Item 25A can only diverge because withholding was genuinely wrong - never because the
        // certificate omitted income.
        result.Item52_TotalTaxableCompensation.Should().Be(
            20_000m + 1_000m + 800m + 300m + 1_500m);
    }

    [Fact]
    public async Task GetPreviewAsync_PutsNonTaxableAllowancesInTheNonTaxableSectionOnly()
    {
        PaidRunsAre(
            Run(payDate: new DateOnly(2026, 1, 15), status: PayrollRunStatus.Paid, entries:
            [
                Entry(_employeeId, regularPay: 20_000m, nonTaxableAllowances: 2_000m)
            ]));

        var result = await _sut.GetPreviewAsync(_employeeId, 2026, CancellationToken.None);

        result.Should().NotBeNull();

        // Lands in Section A (non-taxable) ...
        result!.Item37_SalariesOtherForms.Should().Be(2_000m);
        result.Item38_TotalNonTaxable.Should().Be(2_000m);

        // ... and does NOT inflate taxable compensation.
        result.Item52_TotalTaxableCompensation.Should().Be(20_000m);
    }

    [Fact]
    public async Task GetPreviewAsync_ExcludesRunsThatAreNotPaid()
    {
        // Only Paid counts. ComputeAsync resets a run to Draft on every recompute, and Approved
        // is still one recompute away from moving - so admitting anything short of Paid would let
        // a tax certificate change after it was issued.
        PaidRunsAre(
            Run(payDate: new DateOnly(2026, 1, 15), status: PayrollRunStatus.Paid, entries:
                [Entry(_employeeId, regularPay: 20_000m, withholdingTax: 2_000m)]),
            Run(payDate: new DateOnly(2026, 1, 31), status: PayrollRunStatus.Draft, entries:
                [Entry(_employeeId, regularPay: 5_000m, withholdingTax: 500m)]),
            Run(payDate: new DateOnly(2026, 2, 15), status: PayrollRunStatus.ForApproval, entries:
                [Entry(_employeeId, regularPay: 6_000m, withholdingTax: 600m)]),
            Run(payDate: new DateOnly(2026, 2, 28), status: PayrollRunStatus.Approved, entries:
                [Entry(_employeeId, regularPay: 7_000m, withholdingTax: 700m)]));

        var result = await _sut.GetPreviewAsync(_employeeId, 2026, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Item39_BasicSalary.Should().Be(20_000m);
        result.Item25A_PresentTaxWithheld.Should().Be(2_000m);
    }

    [Fact]
    public async Task GetPreviewAsync_AttributesARunToTheYearItWasPaidIn()
    {
        // A run for period 2025-12-26..2026-01-10 with PayDate 2026-01-15 belongs to 2026,
        // NOT 2025. PayZen attributed by PeriodStart and would have put it in 2025.
        // BIR taxes compensation in the year it is PAID, and in a semi-monthly cycle a period
        // that straddles New Year is the ordinary case, not an edge case.
        PaidRunsAre(
            Run(payDate: new DateOnly(2026, 1, 15), status: PayrollRunStatus.Paid,
                periodStart: new DateOnly(2025, 12, 26), periodEnd: new DateOnly(2026, 1, 10),
                entries: [Entry(_employeeId, regularPay: 20_000m, withholdingTax: 2_000m)]));

        var paidYear = await _sut.GetPreviewAsync(_employeeId, 2026, CancellationToken.None);
        var periodStartYear = await _sut.GetPreviewAsync(_employeeId, 2025, CancellationToken.None);

        paidYear.Should().NotBeNull();
        paidYear!.Item39_BasicSalary.Should().Be(20_000m);
        paidYear.Item25A_PresentTaxWithheld.Should().Be(2_000m);

        periodStartYear.Should().BeNull(
            "the run was paid in 2026, so none of it is 2025 income - PayZen's PeriodStart.Year " +
            "would have certified it as 2025");
    }

    [Theory]
    [InlineData(80_000, 80_000, 0)]
    [InlineData(90_000, 90_000, 0)]
    [InlineData(100_000, 90_000, 10_000)]
    public async Task GetPreviewAsync_SplitsThirteenthMonthAtTheExemptionCap(
        decimal total, decimal expectedNonTaxable, decimal expectedTaxable)
    {
        PaidRunsAre(
            Run(payDate: new DateOnly(2026, 12, 15), status: PayrollRunStatus.Paid, entries:
                [Entry(_employeeId, thirteenthMonth: total)]));

        var result = await _sut.GetPreviewAsync(_employeeId, 2026, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Item34_ThirteenthMonthAndBenefits.Should().Be(expectedNonTaxable);
        result.Item48_TaxableThirteenthMonth.Should().Be(expectedTaxable);
        (result.Item34_ThirteenthMonthAndBenefits + result.Item48_TaxableThirteenthMonth)
            .Should().Be(total, "the split reallocates the 13th month, it never creates or loses any");
        StatutoryCaps.ThirteenthMonthExemption.Should().Be(90_000m);
    }

    [Fact]
    public async Task GetPreviewAsync_CertifiesFinalPayLeaveConversionAndSeparationPay()
    {
        // A regular run plus a Paid final-pay entry: leave conversion of 6,000 (4,000 de minimis,
        // 2,000 beyond it) and separation pay of 150,000, all non-taxable (separation for causes
        // beyond the employee's control is fully exempt). FinalPayNonTaxable therefore carries
        // both the leave conversion's de minimis slice and all of the separation pay: 4,000 +
        // 150,000 = 154,000. The 2,000 beyond de minimis is "other benefits" (RR 5-2011 as
        // amended by RR 11-2018): it shares the 90,000 exemption with the 13th month (none here),
        // so it's all within it and goes to Item 34. Nothing is taxable outright, so Item 51B is
        // blank.
        PaidRunsAre(
            Run(payDate: new DateOnly(2026, 1, 15), status: PayrollRunStatus.Paid, entries:
                [Entry(_employeeId, regularPay: 20_000m)]),
            Run(payDate: new DateOnly(2026, 6, 30), status: PayrollRunStatus.Paid, entries:
            [
                Entry(_employeeId,
                    leaveConversionPay: 6_000m, leaveConversionNonTaxable: 4_000m,
                    separationPay: 150_000m, finalPayNonTaxable: 154_000m)
            ]));

        var result = await _sut.GetPreviewAsync(_employeeId, 2026, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Item34_ThirteenthMonthAndBenefits.Should().Be(2_000m);
        result.Item35_DeMinimis.Should().Be(4_000m);
        result.Item37_SalariesOtherForms.Should().Be(150_000m);
        result.Item48_TaxableThirteenthMonth.Should().Be(0m);
        result.Item51B_OtherAmount.Should().Be(0m);
        result.Item51B_OtherLabel.Should().BeEmpty();
        // Item 19 = everything paid: 20,000 + 6,000 + 150,000 = 176,000.
        result.Item19_GrossCompensation.Should().Be(176_000m);
        result.Item52_TotalTaxableCompensation.Should().Be(20_000m);
    }

    [Fact]
    public async Task GetPreviewAsync_LeaveBeyondDeMinimis_SharesThe90000WithThe13thMonth()
    {
        // 85,000 of 13th month paid in a regular run, then a final pay with 1,000 more 13th month
        // and 12,000 of leave (3,000 de minimis, 9,000 beyond it). 13th month and other benefits
        // for the year = 85,000 + 1,000 + 9,000 = 95,000: 90,000 exempt (Item 34), 5,000 taxable
        // (Item 48). The de minimis 3,000 stays in Item 35.
        PaidRunsAre(
            Run(payDate: new DateOnly(2026, 5, 15), status: PayrollRunStatus.Paid, entries:
                [Entry(_employeeId, regularPay: 20_000m, thirteenthMonth: 85_000m)]),
            Run(payDate: new DateOnly(2026, 6, 30), status: PayrollRunStatus.Paid, entries:
            [
                Entry(_employeeId, thirteenthMonth: 1_000m,
                    leaveConversionPay: 12_000m, leaveConversionNonTaxable: 3_000m, finalPayNonTaxable: 3_000m)
            ]));

        var result = await _sut.GetPreviewAsync(_employeeId, 2026, CancellationToken.None);

        result!.Item34_ThirteenthMonthAndBenefits.Should().Be(90_000m);
        result.Item48_TaxableThirteenthMonth.Should().Be(5_000m);
        result.Item35_DeMinimis.Should().Be(3_000m);
        result.Item51B_OtherAmount.Should().Be(0m);
        // Item 52 = 20,000 basic + 5,000 = 25,000; Item 19 = 20,000 + 86,000 + 12,000 = 118,000.
        result.Item52_TotalTaxableCompensation.Should().Be(25_000m);
        result.Item19_GrossCompensation.Should().Be(118_000m);
    }

    [Fact]
    public async Task GetPreviewAsync_PutsTaxableSeparationPayInItem51B()
    {
        // A resignation with a 50,000 goodwill payment: taxable outright, so Item 51B.
        PaidRunsAre(
            Run(payDate: new DateOnly(2026, 6, 30), status: PayrollRunStatus.Paid, entries:
                [Entry(_employeeId, regularPay: 10_000m, separationPay: 50_000m)]));

        var result = await _sut.GetPreviewAsync(_employeeId, 2026, CancellationToken.None);

        result!.Item51B_OtherAmount.Should().Be(50_000m);
        result.Item51B_OtherLabel.Should().Be("Final pay - separation/retirement pay");
        result.Item34_ThirteenthMonthAndBenefits.Should().Be(0m);
    }

    [Fact]
    public async Task GetPreviewAsync_LeavesItem51BBlank_WhenThereIsNoFinalPay()
    {
        // No final-pay entry at all - the "Others (specify)" box for final pay must not appear
        // with a zero amount and a label on an ordinary certificate.
        PaidRunsAre(
            Run(payDate: new DateOnly(2026, 1, 15), status: PayrollRunStatus.Paid, entries:
                [Entry(_employeeId, regularPay: 20_000m)]));

        var result = await _sut.GetPreviewAsync(_employeeId, 2026, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Item51B_OtherAmount.Should().Be(0m);
        result.Item51B_OtherLabel.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildWithDraftEntryAsync_AddsTheDraftRunAndEntryToThePaidRunsItSums()
    {
        PaidRunsAre(
            Run(payDate: new DateOnly(2026, 1, 15), status: PayrollRunStatus.Paid, entries:
                [Entry(_employeeId, regularPay: 20_000m, withholdingTax: 1_000m)]));

        var draftRun = Run(payDate: new DateOnly(2026, 6, 30), status: PayrollRunStatus.Draft, entries: []);
        var draftEntry = Entry(_employeeId,
            leaveConversionPay: 6_000m, leaveConversionNonTaxable: 4_000m,
            separationPay: 150_000m, finalPayNonTaxable: 154_000m, withholdingTax: 300m);

        var result = await _sut.BuildWithDraftEntryAsync(_employeeId, 2026, draftRun, draftEntry, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Item39_BasicSalary.Should().Be(20_000m);
        result.Item35_DeMinimis.Should().Be(4_000m);
        result.Item37_SalariesOtherForms.Should().Be(150_000m);
        result.Item34_ThirteenthMonthAndBenefits.Should().Be(2_000m);   // the leave beyond de minimis
        result.Item51B_OtherAmount.Should().Be(0m);
        result.Item25A_PresentTaxWithheld.Should().Be(1_300m);
    }

    [Fact]
    public async Task BuildWithDraftEntryAsync_UsesTheSavedManualInputs_LikeAPreview()
    {
        PaidRunsAre(
            Run(payDate: new DateOnly(2026, 1, 15), status: PayrollRunStatus.Paid, entries:
                [Entry(_employeeId, regularPay: 20_000m)]));
        _inputsRepo.Setup(r => r.GetAsync(_employeeId, 2026, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new Bir2316Inputs { EmployeeId = _employeeId, Year = 2026, Item22_PrevTaxableCompensation = 50_000m });

        var draftRun = Run(payDate: new DateOnly(2026, 6, 30), status: PayrollRunStatus.Draft, entries: []);
        var draftEntry = Entry(_employeeId, separationPay: 10_000m, finalPayNonTaxable: 10_000m);

        var result = await _sut.BuildWithDraftEntryAsync(_employeeId, 2026, draftRun, draftEntry, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Item22_PrevTaxableCompensation.Should().Be(50_000m);
    }

    [Fact]
    public async Task BuildWithDraftEntryAsync_SettlingTheDraftEntrysWithholdingTax_MakesTaxDueEqualTaxWithheld()
    {
        // The scenario BuildWithDraftEntryAsync exists for: solve the draft (final-pay) entry's
        // WithholdingTax for Item24 (TaxDue) - Item25A(other runs) - Item25B - Item27, then rebuild
        // with that figure, and the certificate is left with Item24 == Item26 + Item27 - the
        // condition that qualifies the employee for substituted filing. Item24 depends only on
        // taxable compensation (Item23), never on withheld tax, so it does not move between the
        // two builds below - only Item25A/26 do.
        PaidRunsAre(
            Run(payDate: new DateOnly(2026, 1, 15), status: PayrollRunStatus.Paid, entries:
            [
                Entry(_employeeId, regularPay: 40_000m, withholdingTax: 3_000m)
            ]));

        var manual = new Bir2316ManualInputs
        {
            Item22_PrevTaxableCompensation = 10_000m, Item25B_PrevTaxWithheld = 1_000m, Item27_PeraTaxCredit = 500m
        };
        var draftRun = Run(payDate: new DateOnly(2026, 6, 30), status: PayrollRunStatus.Draft, entries: []);

        // First pass: find Item24 and the other-runs Item25A with the draft entry's withholding at
        // zero, exactly as Task 5 would before it has solved for the figure.
        var unsettledDraft = Entry(_employeeId,
            leaveConversionPay: 6_000m, leaveConversionNonTaxable: 4_000m,
            separationPay: 150_000m, finalPayNonTaxable: 154_000m, withholdingTax: 0m);
        var withZeroWithholding = await BuildWithDraft(draftRun, unsettledDraft, manual);

        decimal settledWithholding = withZeroWithholding!.Item24_TaxDue
            - withZeroWithholding.Item25A_PresentTaxWithheld
            - manual.Item25B_PrevTaxWithheld
            - manual.Item27_PeraTaxCredit;

        var settledDraft = Entry(_employeeId,
            leaveConversionPay: 6_000m, leaveConversionNonTaxable: 4_000m,
            separationPay: 150_000m, finalPayNonTaxable: 154_000m, withholdingTax: settledWithholding);

        var settled = await BuildWithDraft(draftRun, settledDraft, manual);

        settled.Should().NotBeNull();
        settled!.Item24_TaxDue.Should().Be(withZeroWithholding.Item24_TaxDue, "Item24 is derived from taxable compensation, not withheld tax");
        settled.Item24_TaxDue.Should().Be(settled.Item26_TotalTaxWithheld + settled.Item27_PeraTaxCredit);
    }

    private async Task<Bir2316Dto?> BuildWithDraft(PayrollRun draftRun, PayrollRunEmployee draftEntry, Bir2316ManualInputs manual)
    {
        _inputsRepo.Setup(r => r.GetAsync(_employeeId, 2026, It.IsAny<CancellationToken>())).ReturnsAsync(
            new Bir2316Inputs
            {
                EmployeeId = _employeeId,
                Year = 2026,
                Item22_PrevTaxableCompensation = manual.Item22_PrevTaxableCompensation,
                Item25B_PrevTaxWithheld = manual.Item25B_PrevTaxWithheld,
                Item27_PeraTaxCredit = manual.Item27_PeraTaxCredit
            });
        return await _sut.BuildWithDraftEntryAsync(_employeeId, 2026, draftRun, draftEntry, CancellationToken.None);
    }

    [Fact]
    public async Task BuildWithDraftEntryAsync_DoesNotDoubleCount_WhenTheDraftRunIsAlreadyAmongThePaidRuns()
    {
        // A caller who passes back a run that GetPaidRunsForEmployeeInYearAsync already returned
        // (same Id) must not have its entry summed twice - that would double every figure on the
        // certificate.
        var alreadyPaidRun = Run(payDate: new DateOnly(2026, 6, 30), status: PayrollRunStatus.Paid, entries:
            [Entry(_employeeId, regularPay: 20_000m, withholdingTax: 1_000m)]);
        PaidRunsAre(alreadyPaidRun);

        // Same Id as the run already in the Paid set, but a distinct object with its own entry -
        // the shape a caller could plausibly (if mistakenly) construct.
        var duplicateRun = new PayrollRun
        {
            Id = alreadyPaidRun.Id,
            RunNumber = alreadyPaidRun.RunNumber,
            PayDate = alreadyPaidRun.PayDate,
            PeriodStart = alreadyPaidRun.PeriodStart,
            PeriodEnd = alreadyPaidRun.PeriodEnd,
            Frequency = alreadyPaidRun.Frequency,
            Status = alreadyPaidRun.Status
        };
        var duplicateEntry = Entry(_employeeId, regularPay: 20_000m, withholdingTax: 1_000m);

        var result = await _sut.BuildWithDraftEntryAsync(_employeeId, 2026, duplicateRun, duplicateEntry, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Item39_BasicSalary.Should().Be(20_000m, "the duplicate run's entry must not be summed a second time");
        result.Item25A_PresentTaxWithheld.Should().Be(1_000m);
    }

    [Fact]
    public async Task BuildWithDraftEntryAsync_RefusesADraftEntryForADifferentEmployee()
    {
        PaidRunsAre(
            Run(payDate: new DateOnly(2026, 1, 15), status: PayrollRunStatus.Paid, entries:
                [Entry(_employeeId, regularPay: 20_000m)]));

        var draftRun = Run(payDate: new DateOnly(2026, 6, 30), status: PayrollRunStatus.Draft, entries: []);
        // Wrong employee id on the entry - putting someone else's pay on this certificate.
        var mismatchedEntry = Entry(_otherEmployeeId, separationPay: 10_000m, finalPayNonTaxable: 10_000m);

        var act = () => _sut.BuildWithDraftEntryAsync(_employeeId, 2026, draftRun, mismatchedEntry, CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task BuildWithDraftEntryAsync_BuildsFromTheDraftAlone_WhenTheEmployeeHasNoPaidRuns()
    {
        // A first-year employee whose only pay this year IS the final run - there is nothing Paid
        // yet for BuildAsync/GetPreviewAsync to find, but the draft entry alone is enough here.
        PaidRunsAre();

        var draftRun = Run(payDate: new DateOnly(2026, 6, 30), status: PayrollRunStatus.Draft, entries: []);
        var draftEntry = Entry(_employeeId, separationPay: 50_000m, finalPayNonTaxable: 50_000m, withholdingTax: 0m);

        var result = await _sut.BuildWithDraftEntryAsync(_employeeId, 2026, draftRun, draftEntry, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Item37_SalariesOtherForms.Should().Be(50_000m);
        result.Item25A_PresentTaxWithheld.Should().Be(0m);
    }

    [Fact]
    public async Task BuildWithDraftEntryAsync_WhenTheEmployeeIsUnknown_ReturnsNull()
    {
        _employeeRepo.Setup(r => r.GetByIdAsync(_employeeId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync((Employee?)null);

        var draftRun = Run(payDate: new DateOnly(2026, 6, 30), status: PayrollRunStatus.Draft, entries: []);
        var draftEntry = Entry(_employeeId, separationPay: 50_000m, finalPayNonTaxable: 50_000m);

        var result = await _sut.BuildWithDraftEntryAsync(_employeeId, 2026, draftRun, draftEntry, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPreviewAsync_WhenThereAreNoPaidRunsForTheYear_ReturnsNull()
    {
        PaidRunsAre();

        var result = await _sut.GetPreviewAsync(_employeeId, 2026, CancellationToken.None);

        result.Should().BeNull("there is nothing to certify");
    }

    // ------------------------------------------------------------------
    // The security departure: derived figures are never taken from the caller
    // ------------------------------------------------------------------

    [Fact]
    public async Task BuildAsync_IgnoresDerivedFiguresSuppliedByTheCaller()
    {
        // Payroll says 4,321 was withheld this year. The caller is going to try to state
        // something else on the certificate, and supply a large previous-employer figure
        // alongside it.
        PaidRunsAre(
            Run(payDate: new DateOnly(2026, 1, 15), status: PayrollRunStatus.Paid, entries:
            [
                Entry(_employeeId,
                    regularPay: 30_000m, overtimePay: 1_000m, withholdingTax: 4_321m,
                    sss: 900m, philHealth: 500m, pagIbig: 100m, thirteenthMonth: 100_000m)
            ]));

        var manual = new Bir2316ManualInputs
        {
            PrevEmployerTin = "111-222-333-000",
            PrevEmployerName = "Former Employer Inc.",
            PrevEmployerAddress = "1 Old Street, Makati City",
            PrevEmployerZipCode = "1200",
            Item22_PrevTaxableCompensation = 500_000m,
            Item25B_PrevTaxWithheld = 75_000m,
            Item35_DeMinimis = 7_000m,
            Item33_HazardPayMwe = 3_000m,
            Item27_PeraTaxCredit = 5_000m,
            StatutoryMinWagePerDay = 610m,
            StatutoryMinWagePerMonth = 15_910m
        };

        var result = await _sut.BuildAsync(_employeeId, 2026, manual, CancellationToken.None);

        result.Should().NotBeNull();

        // Every derived figure comes off the payroll records, whatever the request said.
        result!.Item25A_PresentTaxWithheld.Should().Be(4_321m);
        result.Item39_BasicSalary.Should().Be(28_500m);   // 30,000 less the 1,500 of contributions
        result.Item50_OvertimePay.Should().Be(1_000m);
        result.Item36_SssPhicPagibigContributions.Should().Be(1_500m);
        result.Item34_ThirteenthMonthAndBenefits.Should().Be(90_000m);
        result.Item48_TaxableThirteenthMonth.Should().Be(10_000m);

        // The manual half is overlaid exactly as supplied - that is what it is for.
        result.Item22_PrevTaxableCompensation.Should().Be(500_000m);
        result.Item25B_PrevTaxWithheld.Should().Be(75_000m);
        result.Item35_DeMinimis.Should().Be(7_000m);
        result.Item33_HazardPayMwe.Should().Be(3_000m);
        result.Item27_PeraTaxCredit.Should().Be(5_000m);
        result.PrevEmployerName.Should().Be("Former Employer Inc.");
        result.StatutoryMinWagePerDay.Should().Be(610m);

        // IsMinimumWageEarner has no manual input to overlay - see the doc comment on
        // Bir2316Dto.IsMinimumWageEarner - so it stays at its default even though nothing here
        // requests it explicitly. It is not derived and must not silently become true.
        result.IsMinimumWageEarner.Should().BeFalse();

        // Item 24 is the liability computed on Item 23, never the withheld total. Item 23 here is
        // (30,000 - 1,500 contributions) + 1,000 + 10,000 taxable 13th month + 500,000 previous
        // = 539,500, so tax due is 22,500 + 20% of the 139,500 over 400,000 = 50,400 - nothing
        // like the 79,321 withheld.
        //
        // Item 25B was 999,999 when this test was written, to dramatise a caller stating an
        // outlandish figure. That pair is impossible rather than merely large - no tax withheld
        // can exceed the compensation it came from, and Bir2316ManualInputsValidator now rejects
        // it - so the figure is a realistic 15% of Item 22. Nothing this test proves depended on
        // the old value: Item 25B is a legitimately caller-supplied field and is overlaid as
        // given, while the derived figure under test is Item 25A, which still comes off payroll.
        result.Item23_GrossTaxable.Should().Be(539_500m);
        result.Item24_TaxDue.Should().Be(50_400m);
        result.Item24_TaxDue.Should().Be(BirWithholdingTax.ComputeAnnualTaxDue(result.Item23_GrossTaxable));
        result.Item26_TotalTaxWithheld.Should().Be(79_321m);
        result.Item24_TaxDue.Should().NotBe(result.Item26_TotalTaxWithheld);

        // The type is the enforcement. Bir2316ManualInputs carries only what a human legitimately
        // supplies - there is no field on it capable of stating a derived figure, so there is no
        // request body a caller could craft to put one on a tax certificate. Pinned by name so
        // that adding such a field later fails here instead of shipping quietly.
        typeof(Bir2316ManualInputs).GetProperties().Select(p => p.Name)
            .Should().BeEquivalentTo(
            [
                nameof(Bir2316ManualInputs.PrevEmployerTin),
                nameof(Bir2316ManualInputs.PrevEmployerName),
                nameof(Bir2316ManualInputs.PrevEmployerAddress),
                nameof(Bir2316ManualInputs.PrevEmployerZipCode),
                nameof(Bir2316ManualInputs.Item22_PrevTaxableCompensation),
                nameof(Bir2316ManualInputs.Item25B_PrevTaxWithheld),
                nameof(Bir2316ManualInputs.Item27_PeraTaxCredit),
                nameof(Bir2316ManualInputs.Item33_HazardPayMwe),
                nameof(Bir2316ManualInputs.Item35_DeMinimis),
                nameof(Bir2316ManualInputs.StatutoryMinWagePerDay),
                nameof(Bir2316ManualInputs.StatutoryMinWagePerMonth)
            ]);
    }

    // ------------------------------------------------------------------
    // BuildAllAsync — the bulk path behind GenerateAll
    // ------------------------------------------------------------------

    [Fact]
    public async Task BuildAllAsync_BuildsOneCertificatePerEmployeeInTheOrderGiven()
    {
        // One run carries both employees' entries, the shape GetPaidRunsInYearAsync actually
        // returns - BuildAllAsync has to split it back out per employee itself, the same as
        // BuildAsync already does for a single employee's runs.
        var run = Run(payDate: new DateOnly(2026, 1, 15), status: PayrollRunStatus.Paid, entries:
        [
            Entry(_otherEmployeeId, regularPay: 15_000m, withholdingTax: 800m),
            Entry(_employeeId, regularPay: 30_000m, withholdingTax: 2_000m)
        ]);

        // GetEmployeeIdsWithPaidRunsInYearAsync is ordered by last name then first name; put the
        // "other" employee first here to prove BuildAllAsync preserves that order rather than,
        // say, the order entries happen to appear inside the run.
        PaidRunsInYearAre([_otherEmployeeId, _employeeId], run);
        EmployeesAre(
            AnEmployee(_otherEmployeeId, "Reyes", "Ana"),
            TheEmployee());

        var result = await _sut.BuildAllAsync(2026, CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].EmployeeLastName.Should().Be("Reyes");
        result[0].Item39_BasicSalary.Should().Be(15_000m);
        result[0].Item25A_PresentTaxWithheld.Should().Be(800m);
        result[1].EmployeeLastName.Should().Be("Dela Cruz");
        result[1].Item39_BasicSalary.Should().Be(30_000m);

        // Every certificate is built with an empty manual overlay, same as GetPreviewAsync -
        // nobody has supplied per-employee facts yet in a bulk run.
        result[0].PrevEmployerName.Should().BeEmpty();
        result[0].Item27_PeraTaxCredit.Should().Be(0m);
    }

    [Fact]
    public async Task BuildAllAsync_FormsCarryTheEmployeeId()
    {
        var run = Run(payDate: new DateOnly(2026, 1, 15), status: PayrollRunStatus.Paid, entries:
        [
            Entry(_employeeId, regularPay: 30_000m, withholdingTax: 2_000m)
        ]);
        PaidRunsInYearAre([_employeeId], run);
        EmployeesAre(TheEmployee());

        var result = await _sut.BuildAllAsync(2026, CancellationToken.None);

        result.Should().ContainSingle().Which.EmployeeId.Should().Be(_employeeId);
    }

    [Fact]
    public async Task BuildAllAsync_WhenNoEmployeeHasAPaidRunInTheYear_ReturnsEmptyWithoutFurtherQueries()
    {
        PaidRunsInYearAre([]);

        var result = await _sut.BuildAllAsync(2026, CancellationToken.None);

        result.Should().BeEmpty();

        // Nothing else needed querying once the employee-id set came back empty - the whole
        // point of fetching it first.
        _runRepo.Verify(r => r.GetPaidRunsInYearAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _employeeRepo.Verify(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ------------------------------------------------------------------
    // Identity
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetPreviewAsync_TakesTheEmployeeTinFromTheGovernmentIdRow()
    {
        // EmployeeConfiguration ignores the M2NET.Core base Employee.TIN column outright
        // ("PeopleCore uses GovernmentIds collection"), so the base property is never loaded from
        // the database and the EmployeeGovernmentId row is the only authoritative TIN. Set the
        // base property to something else here to prove which one reaches the certificate.
        var employee = TheEmployee();
        employee.TIN = "000-000-000-000";   // the M2NET.Core base property, unmapped by EF
        _employeeRepo.Setup(r => r.GetByIdAsync(_employeeId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(employee);

        PaidRunsAre(
            Run(payDate: new DateOnly(2026, 1, 15), status: PayrollRunStatus.Paid, entries:
                [Entry(_employeeId, regularPay: 20_000m)]));

        var result = await _sut.GetPreviewAsync(_employeeId, 2026, CancellationToken.None);

        result.Should().NotBeNull();
        result!.EmployeeTin.Should().Be("123-456-789-000");
        result.EmployeeLastName.Should().Be("Dela Cruz");
        result.EmployeeFirstName.Should().Be("Juan");
        result.RdoCode.Should().Be("050");
        result.EmployerTin.Should().Be("987-654-321-000");
        result.EmployerName.Should().Be("M2NET Solutions Inc.");
        result.EmployerZipCode.Should().Be("1605");
        result.IsMainEmployer.Should().BeTrue();
    }

    [Fact]
    public async Task GetPreviewAsync_WhenNoCompanyIsConfigured_Throws()
    {
        _companyRepo.Setup(r => r.GetDefaultAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync((Company?)null);
        PaidRunsAre(
            Run(payDate: new DateOnly(2026, 1, 15), status: PayrollRunStatus.Paid, entries:
                [Entry(_employeeId, regularPay: 20_000m)]));

        var act = () => _sut.GetPreviewAsync(_employeeId, 2026, CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
    }

    // ------------------------------------------------------------------
    // Saved manual inputs — GenerateAsync saves, BuildAsync never does, previews/bulk read them
    // ------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_SavesTheInputsItWasGenerated_With()
    {
        PaidRunsAre(
            Run(payDate: new DateOnly(2026, 1, 15), status: PayrollRunStatus.Paid, entries:
                [Entry(_employeeId, regularPay: 20_000m, withholdingTax: 2_000m)]));

        var inputs = new Bir2316ManualInputs { PrevEmployerName = "Old Co.", Item22_PrevTaxableCompensation = 50_000m };

        var dto = await _sut.GenerateAsync(_employeeId, 2026, inputs, CancellationToken.None);

        dto.Should().NotBeNull();
        _inputsRepo.Verify(r => r.SaveAsync(_employeeId, 2026, inputs, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateAsync_WhenTheEmployeeHasNoPaidRunsThatYear_SavesNothingAndReturnsNull()
    {
        PaidRunsAre();

        var dto = await _sut.GenerateAsync(_employeeId, 2026, new Bir2316ManualInputs(), CancellationToken.None);

        dto.Should().BeNull();
        _inputsRepo.Verify(r => r.SaveAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<Bir2316ManualInputs>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BuildAsync_SavesNothing()
    {
        PaidRunsAre(
            Run(payDate: new DateOnly(2026, 1, 15), status: PayrollRunStatus.Paid, entries:
                [Entry(_employeeId, regularPay: 20_000m, withholdingTax: 2_000m)]));

        await _sut.BuildAsync(_employeeId, 2026, new Bir2316ManualInputs { Item27_PeraTaxCredit = 100m }, CancellationToken.None);

        _inputsRepo.Verify(r => r.SaveAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<Bir2316ManualInputs>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GenerateAsync_RefusesInvalidInputs_BeforeSavingAnything()
    {
        PaidRunsAre(
            Run(payDate: new DateOnly(2026, 1, 15), status: PayrollRunStatus.Paid, entries:
                [Entry(_employeeId, regularPay: 20_000m, withholdingTax: 2_000m)]));

        // Not digits/separators only and neither 9 nor 12 digits - Bir2316ManualInputsValidator
        // rejects it on both grounds.
        var act = () => _sut.GenerateAsync(_employeeId, 2026, new Bir2316ManualInputs { PrevEmployerTin = "not-a-tin" }, CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        _inputsRepo.Verify(r => r.SaveAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<Bir2316ManualInputs>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetPreviewAsync_UsesTheSavedInputs()
    {
        PaidRunsAre(
            Run(payDate: new DateOnly(2026, 1, 15), status: PayrollRunStatus.Paid, entries:
                [Entry(_employeeId, regularPay: 20_000m, withholdingTax: 2_000m)]));
        _inputsRepo.Setup(r => r.GetAsync(_employeeId, 2026, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new Bir2316Inputs { EmployeeId = _employeeId, Year = 2026, Item22_PrevTaxableCompensation = 50_000m });

        var dto = await _sut.GetPreviewAsync(_employeeId, 2026, CancellationToken.None);

        dto!.Item22_PrevTaxableCompensation.Should().Be(50_000m);
    }

    [Fact]
    public async Task BuildAllAsync_UsesEachEmployeesSavedInputs()
    {
        PaidRunsInYearAre([_employeeId],
            Run(payDate: new DateOnly(2026, 1, 15), status: PayrollRunStatus.Paid, entries:
                [Entry(_employeeId, regularPay: 20_000m, withholdingTax: 2_000m)]));
        EmployeesAre(TheEmployee());
        _inputsRepo.Setup(r => r.GetForYearAsync(2026, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new Dictionary<Guid, Bir2316Inputs>
                   {
                       [_employeeId] = new() { EmployeeId = _employeeId, Year = 2026, Item25B_PrevTaxWithheld = 1_200m }
                   });

        var forms = await _sut.BuildAllAsync(2026, CancellationToken.None);

        forms.Single(f => f.EmployeeTin != null).Item25B_PrevTaxWithheld.Should().Be(1_200m);
    }

    [Fact]
    public async Task GetInputsAsync_WithNothingSaved_IsEmpty()
    {
        (await _sut.GetInputsAsync(_employeeId, 2026, CancellationToken.None)).Should().BeEquivalentTo(new Bir2316ManualInputs());
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Stubs the repository to hand back <paramref name="runs"/> for ANY year, deliberately: the
    /// year and status filters under test are the service's own, not the mock's.
    /// </summary>
    private void PaidRunsAre(params PayrollRun[] runs)
        => _runRepo
            .Setup(r => r.GetPaidRunsForEmployeeInYearAsync(
                _employeeId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(runs);

    /// <summary>
    /// Stubs the two repository calls BuildAllAsync makes for the bulk path: the ordered
    /// employee-id set, and the year's paid runs (for ANY year, for the same reason as
    /// <see cref="PaidRunsAre"/>).
    /// </summary>
    private void PaidRunsInYearAre(IReadOnlyList<Guid> employeeIds, params PayrollRun[] runs)
    {
        _runRepo.Setup(r => r.GetEmployeeIdsWithPaidRunsInYearAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(employeeIds);
        _runRepo.Setup(r => r.GetPaidRunsInYearAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(runs);
    }

    private void EmployeesAre(params Employee[] employees)
        => _employeeRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(employees);

    private static Employee AnEmployee(Guid id, string lastName, string firstName) => new()
    {
        Id = id,
        EmployeeNumber = $"EMP-{id:N}"[..10],
        FirstName = firstName,
        LastName = lastName,
        DateOfBirth = new DateOnly(1990, 1, 1)
    };

    private static PayrollRun Run(
        DateOnly payDate,
        PayrollRunStatus status,
        IEnumerable<PayrollRunEmployee> entries,
        DateOnly? periodStart = null,
        DateOnly? periodEnd = null)
    {
        var run = new PayrollRun
        {
            RunNumber = $"PR-{payDate:yyyyMMdd}",
            PayDate = payDate,
            PeriodStart = periodStart ?? payDate.AddDays(-15),
            PeriodEnd = periodEnd ?? payDate.AddDays(-1),
            Frequency = PayFrequency.SemiMonthly,
            Status = status
        };
        run.Employees.AddRange(entries);
        return run;
    }

    private static PayrollRunEmployee Entry(
        Guid employeeId,
        decimal regularPay = 0m,
        decimal overtimePay = 0m,
        decimal withholdingTax = 0m,
        decimal sss = 0m,
        decimal philHealth = 0m,
        decimal pagIbig = 0m,
        decimal thirteenthMonth = 0m,
        decimal holidayPay = 0m,
        decimal nightDiffPay = 0m,
        decimal taxableAllowances = 0m,
        decimal nonTaxableAllowances = 0m,
        decimal leaveConversionPay = 0m,
        decimal leaveConversionNonTaxable = 0m,
        decimal separationPay = 0m,
        decimal retirementPay = 0m,
        decimal finalPayNonTaxable = 0m)
        => new()
        {
            EmployeeId = employeeId,
            RegularPay = regularPay,
            OvertimePay = overtimePay,
            WithholdingTax = withholdingTax,
            SSSEmployee = sss,
            PhilHealthEmployee = philHealth,
            PagIbigEmployee = pagIbig,
            ThirteenthMonth = thirteenthMonth,
            HolidayPay = holidayPay,
            NightDiffPay = nightDiffPay,
            TaxableAllowances = taxableAllowances,
            NonTaxableAllowances = nonTaxableAllowances,
            LeaveConversionPay = leaveConversionPay,
            LeaveConversionNonTaxable = leaveConversionNonTaxable,
            SeparationPay = separationPay,
            RetirementPay = retirementPay,
            FinalPayNonTaxable = finalPayNonTaxable
        };

    private Employee TheEmployee()
    {
        var employee = new Employee
        {
            Id = _employeeId,
            EmployeeNumber = "EMP-0001",
            FirstName = "Juan",
            MiddleName = "Protacio",
            LastName = "Dela Cruz",
            DateOfBirth = new DateOnly(1990, 4, 12),
            MobileNumber = "0917-555-0101",
            Address = "12 Mabini Street, Quezon City",
            ZipCode = "1101",
            RdoCode = "050"
        };
        employee.GovernmentIds.Add(new EmployeeGovernmentId
        {
            EmployeeId = _employeeId,
            IdType = GovernmentIdType.TIN,
            IdNumber = "123-456-789-000"
        });
        return employee;
    }

    private static Company TheCompany() => new()
    {
        Name = "M2NET Solutions Inc.",
        TIN = "987-654-321-000",
        Address = "8 Ortigas Avenue, Pasig City",
        City = "Pasig City",
        RdoCode = "043",
        ZipCode = "1605"
    };
}
