using FluentAssertions;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Services;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

/// <summary>
/// The DTO-shaped builder the rendered payslip PDF uses. The payslip prints GrossPay and TotalDeductions as the column totals rather
/// than summing these lines, so the lines have to add up to those figures or the document
/// silently fails to foot.
/// </summary>
public class PayslipLineBuilderTests
{
    [Fact]
    public void Earning_lines_add_up_to_gross_pay()
    {
        var e = FullyLoaded();

        PayslipLineBuilder.Earnings(e).Sum(l => l.Amount).Should().Be(e.GrossPay);
    }

    [Fact]
    public void Employee_deduction_lines_add_up_to_total_deductions()
    {
        var e = FullyLoaded();

        PayslipLineBuilder.Deductions(e).Where(l => !l.IsEmployer).Sum(l => l.Amount)
            .Should().Be(e.TotalDeductions);
    }

    [Fact]
    public void Absences_and_tardiness_are_shown_against_the_basic_pay_they_reduced()
    {
        // RegularPay arrives already net, so the basic figure is restored and the reductions
        // shown beneath it. Listing them as deductions would double-count - they are not part
        // of TotalDeductions.
        var e = FullyLoaded();

        var lines = PayslipLineBuilder.Earnings(e);

        lines[0].Description.Should().Be("Basic Pay");
        lines[0].Amount.Should().Be(10_000m, "8,602.75 net plus the 1,315.06 absence deduction and 82.19 tardiness deduction");
        lines.Should().Contain(l => l.Description == "Less: Absences" && l.Amount == -1_315.06m);
        lines.Should().Contain(l => l.Description == "Less: Tardiness / Undertime" && l.Amount == -82.19m);
    }

    [Fact]
    public void Night_differential_appears_as_its_own_earning()
    {
        var e = FullyLoaded();

        PayslipLineBuilder.Earnings(e)
            .Should().Contain(l => l.Description == "Night Shift Differential" && l.Amount == 65.75m);
    }

    [Fact]
    public void Other_deductions_appear_once_a_custom_field_populates_them()
    {
        var e = FullyLoaded();

        PayslipLineBuilder.Deductions(e)
            .Should().Contain(l => l.Description == "Other Deductions" && l.Amount == 400m);
    }

    [Fact]
    public void A_clean_period_shows_no_adjustment_lines()
    {
        var e = Clean() with { RegularPay = 10_000m, GrossPay = 10_000m };

        var lines = PayslipLineBuilder.Earnings(e);

        lines.Should().ContainSingle();
        lines[0].Amount.Should().Be(10_000m);
    }

    // ---------- Final pay ----------

    [Fact]
    public void A_final_pay_foots_gross_less_the_deductions_shown_plus_the_refund_to_net()
    {
        var e = FinalPay();

        var earnings = PayslipLineBuilder.Earnings(e);
        var deductions = PayslipLineBuilder.Deductions(e).Where(l => !l.IsEmployer).ToList();

        earnings.Sum(l => l.Amount).Should().Be(e.GrossPay).And.Be(62_500m);
        deductions.Sum(l => l.Amount).Should().Be(PayslipLineBuilder.DeductionsTotal(e)).And.Be(7_350m,
            "500 + 250 + 100 contributions, 3,000 loans and 3,500 HR deductions - the refund is not among them");
        PayslipLineBuilder.TaxRefund(e).Should().Be(1_800m);
        (e.GrossPay - PayslipLineBuilder.DeductionsTotal(e) + PayslipLineBuilder.TaxRefund(e))
            .Should().Be(e.NetPay).And.Be(56_950m);
    }

    [Fact]
    public void Leave_conversion_beyond_de_minimis_is_flagged_like_the_13th_month()
    {
        // 7,500 of leave, 6,000 of it de minimis: the other 1,500 is "other benefits", which,
        // like the 13th month, is taxed only past the year's 90,000 exemption - which one
        // payslip can't see. So the line carries the 13th month's flag.
        var lines = PayslipLineBuilder.Earnings(FinalPay());

        var leave = lines.Single(l => l.Description == "Leave Conversion");
        var thirteenth = lines.Single(l => l.Description == "13th Month Pay");
        leave.IsTaxable.Should().Be(thirteenth.IsTaxable).And.BeFalse();
    }

    [Fact]
    public void A_final_pay_shows_leave_conversion_and_separation_pay_as_earnings_and_omits_a_zero_retirement_pay()
    {
        var lines = PayslipLineBuilder.Earnings(FinalPay());

        lines.Should().Contain(l => l.Description == "Leave Conversion" && l.Amount == 7_500m);
        lines.Should().Contain(l => l.Description == "Separation Pay" && l.Amount == 40_000m && !l.IsTaxable,
            "separation pay for an authorized cause is wholly non-taxable here");
        lines.Should().NotContain(l => l.Description == "Retirement Pay");
    }

    [Fact]
    public void Retirement_pay_is_shown_when_there_is_some()
    {
        var e = FinalPay() with { SeparationPay = 0m, RetirementPay = 40_000m };

        var lines = PayslipLineBuilder.Earnings(e);

        lines.Should().Contain(l => l.Description == "Retirement Pay" && l.Amount == 40_000m);
        lines.Should().NotContain(l => l.Description == "Separation Pay");
    }

    [Fact]
    public void A_negative_withholding_tax_withholds_nothing_and_is_added_back_as_a_refund()
    {
        // The year's tax settled on final pay can hand some back. Nothing is withheld, the
        // deductions are only the real ones, and the refund is added after them, above net pay.
        var e = FinalPay();

        var lines = PayslipLineBuilder.Deductions(e);

        lines.Should().Contain(l => l.Description == "Withholding Tax" && l.Amount == 0m);
        lines.Should().NotContain(l => l.Amount < 0m);
        PayslipLineBuilder.DeductionsTotal(e).Should().Be(e.TotalDeductions - e.WithholdingTax);
    }

    [Fact]
    public void A_refund_larger_than_the_other_deductions_still_leaves_them_positive()
    {
        var e = FinalPay() with { LoanDeductions = 0m, OtherDeductions = 0m, TotalDeductions = -950m, NetPay = 63_450m };

        PayslipLineBuilder.DeductionsTotal(e).Should().Be(850m);
        PayslipLineBuilder.TaxRefund(e).Should().Be(1_800m);
        (e.GrossPay - PayslipLineBuilder.DeductionsTotal(e) + PayslipLineBuilder.TaxRefund(e)).Should().Be(e.NetPay);
    }

    [Fact]
    public void A_regular_run_has_no_final_pay_lines()
    {
        var e = FullyLoaded();

        PayslipLineBuilder.Earnings(e).Should().NotContain(l =>
            l.Description == "Leave Conversion" || l.Description == "Separation Pay" || l.Description == "Retirement Pay");
        PayslipLineBuilder.Deductions(e).Should().Contain(l => l.Description == "Withholding Tax" && l.Amount == 1_234.56m);
        PayslipLineBuilder.TaxRefund(e).Should().Be(0m);
        PayslipLineBuilder.DeductionsTotal(e).Should().Be(e.TotalDeductions);
    }

    // ---------- Maternity (RA 11210) ----------

    [Fact]
    public void A_maternity_entry_shows_the_covered_days_against_basic_pay_and_the_advance_as_a_non_taxable_earning()
    {
        var e = Maternity();

        var lines = PayslipLineBuilder.Earnings(e);

        // 24,000 regular pay is net of the 6,000 SSS covers, so the basic is restored to 30,000
        // and the offset shown beneath it, right after the absences line would be.
        lines[0].Should().Be(new PayrollEarningLineDto("Basic Pay", 30_000m));
        lines[1].Should().Be(new PayrollEarningLineDto("Less: covered by SSS maternity benefit", -6_000m));
        lines.Should().ContainSingle(l => l.Description == "SSS maternity benefit (advance)")
            .Which.Should().Be(new PayrollEarningLineDto("SSS maternity benefit (advance)", 70_000.35m, IsTaxable: false));
    }

    [Fact]
    public void A_maternity_entry_foots_earnings_less_deductions_to_net_pay()
    {
        var e = Maternity();

        var earnings = PayslipLineBuilder.Earnings(e);
        var deductions = PayslipLineBuilder.Deductions(e).Where(l => !l.IsEmployer).ToList();

        // 30,000 - 6,000 + 70,000.35 = 94,000.35.
        earnings.Sum(l => l.Amount).Should().Be(e.GrossPay).And.Be(94_000.35m);
        // 1,500 + 750 + 200 + 107.50 = 2,557.50.
        deductions.Sum(l => l.Amount).Should().Be(PayslipLineBuilder.DeductionsTotal(e)).And.Be(2_557.50m);
        // 94,000.35 - 2,557.50 = 91,442.85.
        (earnings.Sum(l => l.Amount) - deductions.Sum(l => l.Amount)).Should().Be(e.NetPay).And.Be(91_442.85m);
    }

    [Fact]
    public void A_maternity_entry_can_show_the_offset_without_an_advance_and_the_advance_without_an_offset()
    {
        var offsetOnly = Maternity() with { MaternityBenefitAdvance = 0m, GrossPay = 24_000m, NetPay = 21_442.50m };
        var advanceOnly = Maternity() with
        {
            RegularPay = 30_000m, MaternityBenefitOffset = 0m, GrossPay = 100_000.35m,
            WithholdingTax = 1_007.50m, TotalDeductions = 3_457.50m, NetPay = 96_542.85m
        };

        var offsetLines = PayslipLineBuilder.Earnings(offsetOnly);
        offsetLines.Should().NotContain(l => l.Description == "SSS maternity benefit (advance)");
        offsetLines.Sum(l => l.Amount).Should().Be(offsetOnly.GrossPay);

        var advanceLines = PayslipLineBuilder.Earnings(advanceOnly);
        advanceLines.Should().NotContain(l => l.Description == "Less: covered by SSS maternity benefit");
        advanceLines[0].Amount.Should().Be(30_000m);
        advanceLines.Sum(l => l.Amount).Should().Be(advanceOnly.GrossPay);
    }

    [Fact]
    public void The_salary_differential_is_its_own_non_taxable_earning_and_basic_pay_is_shown_net_of_it()
    {
        // 30,000 salary; SSS covers 6,000 of the leave days and the 4,000 left for them is the
        // differential. Regular pay 24,000 holds the differential, so basic is shown as
        // 30,000 - 4,000 = 26,000, then -6,000 covered, then +4,000 differential (non-taxable).
        var e = Maternity() with
        {
            MaternityDifferential = 4_000m, MaternityBenefitAdvance = 0m, GrossPay = 24_000m,
            WithholdingTax = 0m, TotalDeductions = 2_450m, NetPay = 21_550m
        };

        var lines = PayslipLineBuilder.Earnings(e);
        var deductions = PayslipLineBuilder.Deductions(e).Where(l => !l.IsEmployer).ToList();

        lines[0].Should().Be(new PayrollEarningLineDto("Basic Pay", 26_000m));
        lines[1].Should().Be(new PayrollEarningLineDto("Less: covered by SSS maternity benefit", -6_000m));
        lines[2].Should().Be(new PayrollEarningLineDto("Maternity salary differential (non-taxable)", 4_000m, IsTaxable: false));
        // 26,000 - 6,000 + 4,000 = 24,000 = gross; 24,000 - 2,450 = 21,550 = net.
        lines.Sum(l => l.Amount).Should().Be(e.GrossPay);
        (lines.Sum(l => l.Amount) - deductions.Sum(l => l.Amount)).Should().Be(e.NetPay);
    }

    [Fact]
    public void Deferred_shares_show_as_a_negative_deduction_and_the_payslip_still_foots()
    {
        // Fully covered cutoff: gross 0, shares 1,500 + 750 + 200 = 2,450 all deferred. The
        // deductions column: 2,450 of shares, 0 tax, -2,450 deferred = 0; 0 - 0 = 0 net.
        var e = Maternity() with
        {
            RegularPay = 0m, MaternityBenefitOffset = 30_000m, MaternityBenefitAdvance = 0m, GrossPay = 0m,
            WithholdingTax = 0m, TotalDeductions = 2_450m, ContributionsDeferred = 2_450m, NetPay = 0m
        };

        var deductions = PayslipLineBuilder.Deductions(e).Where(l => !l.IsEmployer).ToList();

        deductions.Should().ContainSingle(l => l.Description == "Contributions deferred (collected later)")
            .Which.Amount.Should().Be(-2_450m);
        deductions.Sum(l => l.Amount).Should().Be(PayslipLineBuilder.DeductionsTotal(e)).And.Be(0m);
        (PayslipLineBuilder.Earnings(e).Sum(l => l.Amount) - PayslipLineBuilder.DeductionsTotal(e)).Should().Be(e.NetPay);
    }

    [Fact]
    public void Collected_shares_show_as_a_deduction_and_the_payslip_still_foots()
    {
        // An ordinary 30,000 month that collects 2,450 deferred earlier: deductions 2,450 + 1,007.50
        // + 2,450 = 5,907.50; net 30,000 - 5,907.50 = 24,092.50.
        var e = Clean() with
        {
            RegularPay = 30_000m, GrossPay = 30_000m, SSSEmployee = 1_500m, PhilHealthEmployee = 750m,
            PagIbigEmployee = 200m, WithholdingTax = 1_007.50m, TotalDeductions = 3_457.50m,
            DeferredContributionsCollected = 2_450m, NetPay = 24_092.50m
        };

        var deductions = PayslipLineBuilder.Deductions(e).Where(l => !l.IsEmployer).ToList();

        deductions.Should().ContainSingle(l => l.Description == "Deferred contributions collected")
            .Which.Amount.Should().Be(2_450m);
        PayslipLineBuilder.DeductionsTotal(e).Should().Be(5_907.50m);
        deductions.Sum(l => l.Amount).Should().Be(5_907.50m);
        (e.GrossPay - PayslipLineBuilder.DeductionsTotal(e)).Should().Be(e.NetPay);
    }

    [Fact]
    public void A_regular_run_has_no_maternity_lines()
    {
        PayslipLineBuilder.Earnings(FullyLoaded()).Should().NotContain(l => l.Description.Contains("maternity"));
    }

    /// <summary>
    /// A maternity month on a 30,000 salary: 6,000 of it covered by SSS (regular pay 24,000) and
    /// the 70,000.35 benefit advanced; contributions on the 30,000 basic (1,500 / 750 / 200) and
    /// 107.50 withheld on 24,000 - 2,450 = 21,550.
    /// </summary>
    private static PayrollRunEmployeeDto Maternity() => Clean() with
    {
        RegularPay = 24_000m,
        MaternityBenefitOffset = 6_000m,
        MaternityBenefitAdvance = 70_000.35m,
        GrossPay = 94_000.35m,
        SSSEmployee = 1_500m,
        SSSEmployer = 3_030m,
        PhilHealthEmployee = 750m,
        PhilHealthEmployer = 750m,
        PagIbigEmployee = 200m,
        PagIbigEmployer = 200m,
        WithholdingTax = 107.50m,
        TotalDeductions = 2_557.50m,
        NetPay = 91_442.85m
    };

    /// <summary>
    /// A worked final pay: 5,000 salary, 10,000 13th month, 7,500 leave (6,000 of it de minimis)
    /// and 40,000 authorized-cause separation pay; 3,000 of loans, 3,500 of HR deductions, and the
    /// year's tax settling to a 1,800 refund.
    /// </summary>
    private static PayrollRunEmployeeDto FinalPay() => Clean() with
    {
        RegularPay = 5_000m,
        ThirteenthMonth = 10_000m,
        LeaveConversionPay = 7_500m,
        LeaveConversionNonTaxable = 6_000m,
        SeparationPay = 40_000m,
        FinalPayNonTaxable = 46_000m,
        GrossPay = 62_500m,
        SSSEmployee = 500m,
        PhilHealthEmployee = 250m,
        PagIbigEmployee = 100m,
        WithholdingTax = -1_800m,
        LoanDeductions = 3_000m,
        OtherDeductions = 3_500m,
        TotalDeductions = 5_550m,
        NetPay = 56_950m
    };

    /// <summary>An entry exercising every pay component the builders know about.</summary>
    private static PayrollRunEmployeeDto FullyLoaded() => Clean() with
    {
        RegularPay = 8_602.75m,          // 10,000 less 1,315.06 absence and 82.19 tardiness
        AbsenceDeduction = 1_315.06m,
        TardinessDeduction = 82.19m,
        OvertimePay = 1_933.11m,
        HolidayPay = 657.53m,
        NightDiffPay = 65.75m,
        TaxableAllowances = 500m,
        NonTaxableAllowances = 1_000m,
        ThirteenthMonth = 20_000m,
        GrossPay = 32_759.14m,           // sum of every earning component above
        SSSEmployee = 500m,
        SSSEmployer = 1_015m,
        PhilHealthEmployee = 250m,
        PhilHealthEmployer = 250m,
        PagIbigEmployee = 100m,
        PagIbigEmployer = 100m,
        WithholdingTax = 1_234.56m,
        LoanDeductions = 500m,
        OtherDeductions = 400m,
        TotalDeductions = 2_984.56m,     // sum of every non-employer deduction above
        NetPay = 29_774.58m
    };

    private static PayrollRunEmployeeDto Clean() => new(
        Id: Guid.NewGuid(),
        EmployeeId: Guid.NewGuid(),
        EmployeeName: "Dela Cruz, Juan P.",
        EmployeeNumber: "EMP-0042",
        DaysWorked: 22m,
        GrossPay: 0m,
        TotalDeductions: 0m,
        NetPay: 0m,
        RegularPay: 0m,
        OvertimePay: 0m,
        HolidayPay: 0m,
        NightDiffPay: 0m,
        TaxableAllowances: 0m,
        NonTaxableAllowances: 0m,
        ThirteenthMonth: 0m,
        AbsenceDeduction: 0m,
        TardinessDeduction: 0m,
        SSSEmployee: 0m,
        SSSEmployer: 0m,
        PhilHealthEmployee: 0m,
        PhilHealthEmployer: 0m,
        PagIbigEmployee: 0m,
        PagIbigEmployer: 0m,
        WithholdingTax: 0m,
        LoanDeductions: 0m,
        OtherDeductions: 0m);
}
