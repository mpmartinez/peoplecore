using FluentAssertions;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Services;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

/// <summary>
/// Mirrors <see cref="PayrollLineBuilderTests"/> for the DTO-shaped builder the rendered PDF
/// actually uses. The payslip prints GrossPay and TotalDeductions as the column totals rather
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
