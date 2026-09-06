using FluentAssertions;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Domain.Entities.Payroll;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

/// <summary>
/// The payslip lists these lines but prints GrossPay and TotalDeductions as the column totals,
/// so the lines have to add up to those figures. A pay component added to the entity but not to
/// the breakdown makes the document silently fail to foot.
/// </summary>
public class PayrollLineBuilderTests
{
    [Fact]
    public void Earning_lines_add_up_to_gross_pay()
    {
        var e = FullyLoaded();

        PayrollLineBuilder.Earnings(e).Sum(l => l.Amount).Should().Be(e.GrossPay);
    }

    [Fact]
    public void Employee_deduction_lines_add_up_to_total_deductions()
    {
        var e = FullyLoaded();

        PayrollLineBuilder.Deductions(e).Where(l => !l.IsEmployer).Sum(l => l.Amount)
            .Should().Be(e.TotalDeductions);
    }

    [Fact]
    public void Absences_and_tardiness_are_shown_against_the_basic_pay_they_reduced()
    {
        // RegularPay arrives already net, so the basic figure is restored and the reductions
        // shown beneath it. Listing them as deductions would double-count - they are not part
        // of TotalDeductions.
        var e = FullyLoaded();

        var lines = PayrollLineBuilder.Earnings(e);

        lines[0].Description.Should().Be("Basic Pay");
        lines[0].Amount.Should().Be(10_000m, "8,684.94 net plus the 1,315.06 absence deduction");
        lines.Should().Contain(l => l.Description == "Less: Absences" && l.Amount == -1_315.06m);
        lines.Should().Contain(l => l.Description == "Less: Tardiness / Undertime" && l.Amount == -82.19m);
    }

    [Fact]
    public void Night_differential_appears_as_its_own_earning()
    {
        var e = FullyLoaded();

        PayrollLineBuilder.Earnings(e)
            .Should().Contain(l => l.Description == "Night Shift Differential" && l.Amount == 65.75m);
    }

    [Fact]
    public void Other_deductions_appear_once_a_custom_field_populates_them()
    {
        var e = FullyLoaded();

        PayrollLineBuilder.Deductions(e)
            .Should().Contain(l => l.Description == "Other Deductions" && l.Amount == 400m);
    }

    [Fact]
    public void A_clean_period_shows_no_adjustment_lines()
    {
        var e = new PayrollRunEmployee { RegularPay = 10_000m };

        var lines = PayrollLineBuilder.Earnings(e);

        lines.Should().ContainSingle();
        lines[0].Amount.Should().Be(10_000m);
    }

    /// <summary>An entry exercising every pay component the builders know about.</summary>
    private static PayrollRunEmployee FullyLoaded() => new()
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
        SSSEmployee = 500m,
        SSSEmployer = 1_015m,
        PhilHealthEmployee = 250m,
        PhilHealthEmployer = 250m,
        PagIbigEmployee = 100m,
        PagIbigEmployer = 100m,
        WithholdingTax = 1_234.56m,
        LoanDeductions = 500m,
        OtherDeductions = 400m
    };
}
