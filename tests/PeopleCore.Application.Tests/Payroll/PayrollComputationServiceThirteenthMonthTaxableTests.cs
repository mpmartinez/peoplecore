using FluentAssertions;
using PeopleCore.Application.Payroll.FinalPay;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

/// <summary>
/// PayrollComputationService.Compute stores the part of the 13th month above what is left of the
/// 90,000 exemption for 13th month and other benefits (<see cref="PayrollRunEmployee.ThirteenthMonthTaxable"/>):
/// <c>max(0, ThirteenthMonth - max(0, 90,000 - exemption used earlier in the year))</c>, the 13th month
/// filling the exemption before the leave beyond de minimis. It is a record of the split only: the
/// tax and every other figure are what they were before it was stored.
/// <para>
/// The regular cases use the 88,000 monthly salary of PayrollComputationServiceLeaveConversionTests:
/// a full December's regular pay is 88,000, and with 968,000 of basic earned earlier in the year the
/// 13th month is (968,000 + 88,000) / 12 = 88,000. Withholding base 88,000 - 4,150 contributions =
/// 83,850 a month, 1,006,200 a year: 154,050 a year, 12,837.50 a month, in the 25% bracket
/// (800,000-2,000,000). Every taxable excess below stays inside that bracket, so it is taxed at 25%.
/// </para>
/// </summary>
public class PayrollComputationServiceThirteenthMonthTaxableTests
{
    private const decimal RegularWithholding = 12_837.50m;
    private const decimal BasicEarnedJanToNov = 968_000m;

    private readonly PayrollComputationService _sut = new();

    [Fact]
    public void Compute_without_a_13th_month_stores_nothing_taxable()
    {
        // Stored on every entry the engine computes: 0 when there is no 13th month.
        var result = _sut.Compute(NewEmployee(), DecemberRun(), otherBenefitsExemptUsedEarlierInYear: 90_000m);

        result.ThirteenthMonth.Should().Be(0m);
        result.ThirteenthMonthTaxable.Should().Be(0m);
        result.ThirteenthMonthExempt.Should().Be(0m);
        result.WithholdingTax.Should().Be(RegularWithholding);
    }

    [Fact]
    public void Compute_a_13th_month_wholly_within_the_exemption_is_not_taxable()
    {
        // 13th month 88,000; nothing used earlier, so 90,000 is left: all 88,000 exempt, 0 taxable.
        // No excess, so the tax is the regular month's 12,837.50.
        var result = _sut.Compute(NewEmployee(), DecemberRun(),
            includeThirteenthMonth: true, basicEarnedEarlierInYear: BasicEarnedJanToNov);

        result.ThirteenthMonth.Should().Be(88_000m);
        result.ThirteenthMonthTaxable.Should().Be(0m);
        result.ThirteenthMonthExempt.Should().Be(88_000m);
        result.WithholdingTax.Should().Be(RegularWithholding);
        result.GrossPay.Should().Be(88_000m + 88_000m);
    }

    [Fact]
    public void Compute_a_13th_month_partly_over_the_exemption_stores_the_part_over_it()
    {
        // 30,000 of the exemption used earlier (other benefits): 90,000 - 30,000 = 60,000 left.
        // 13th month 88,000: 60,000 exempt, 88,000 - 60,000 = 28,000 taxable.
        // Tax: 28,000 x 25% = 7,000 + 12,837.50 = 19,837.50 - as before the figure was stored.
        var result = _sut.Compute(NewEmployee(), DecemberRun(),
            includeThirteenthMonth: true, basicEarnedEarlierInYear: BasicEarnedJanToNov,
            otherBenefitsExemptUsedEarlierInYear: 30_000m);

        result.ThirteenthMonth.Should().Be(88_000m);
        result.ThirteenthMonthTaxable.Should().Be(28_000m);
        result.ThirteenthMonthExempt.Should().Be(60_000m);
        result.WithholdingTax.Should().Be(19_837.50m);
    }

    [Fact]
    public void Compute_a_13th_month_wholly_over_the_exemption_is_all_taxable()
    {
        // 100,000 used earlier is more than the 90,000: max(0, 90,000 - 100,000) = 0 left, so all
        // 88,000 is taxable and none exempt. Tax: 88,000 x 25% = 22,000 + 12,837.50 = 34,837.50
        // (annual 1,006,200 + 88,000 = 1,094,200, still in the 25% bracket).
        var result = _sut.Compute(NewEmployee(), DecemberRun(),
            includeThirteenthMonth: true, basicEarnedEarlierInYear: BasicEarnedJanToNov,
            otherBenefitsExemptUsedEarlierInYear: 100_000m);

        result.ThirteenthMonthTaxable.Should().Be(88_000m);
        result.ThirteenthMonthExempt.Should().Be(0m);
        result.WithholdingTax.Should().Be(34_837.50m);
    }

    [Fact]
    public void Compute_counts_the_exemption_a_13th_month_advance_and_other_benefits_used_earlier()
    {
        // Earlier in the year: a 44,000 13th month advance and 26,000 of other benefits - 70,000 of
        // the exemption used, 20,000 left. 13th month due: 88,000 - 44,000 paid = 44,000:
        // 20,000 exempt, 44,000 - 20,000 = 24,000 taxable.
        // Tax: 24,000 x 25% = 6,000 + 12,837.50 = 18,837.50.
        var result = _sut.Compute(NewEmployee(), DecemberRun(),
            includeThirteenthMonth: true, basicEarnedEarlierInYear: BasicEarnedJanToNov,
            thirteenthMonthPaidEarlierInYear: 44_000m, otherBenefitsExemptUsedEarlierInYear: 70_000m);

        result.ThirteenthMonth.Should().Be(44_000m);
        result.ThirteenthMonthTaxable.Should().Be(24_000m);
        result.ThirteenthMonthExempt.Should().Be(20_000m);
        result.WithholdingTax.Should().Be(18_837.50m);
    }

    [Fact]
    public void Compute_counts_the_exemption_used_as_at_least_the_13th_month_paid_earlier()
    {
        // As the tax does (Compute_counts_the_exemption_used_as_at_least_the_13th_month_paid_earlier
        // in PayrollComputationServiceLeaveConversionTests): 80,000 of 13th month paid earlier but
        // only 30,000 said to be used - the larger stands. 90,000 - 80,000 = 10,000 left.
        // 13th month due: 88,000 - 80,000 = 8,000, all within the 10,000: 0 taxable.
        // (Taking the 30,000 would also give 0; the leave below tells them apart.)
        // Leave beyond de minimis 6,000 takes the 2,000 left: 4,000 taxable x 25% = 1,000.
        // Tax: 12,837.50 + 1,000 = 13,837.50.
        var result = _sut.Compute(NewEmployee(), DecemberRun(),
            includeThirteenthMonth: true, basicEarnedEarlierInYear: BasicEarnedJanToNov,
            thirteenthMonthPaidEarlierInYear: 80_000m, otherBenefitsExemptUsedEarlierInYear: 30_000m,
            leaveConversion: new LeaveConversionInput(DeMinimis: 12_000m, OtherBenefits: 6_000m));

        result.ThirteenthMonth.Should().Be(8_000m);
        result.ThirteenthMonthTaxable.Should().Be(0m);
        result.WithholdingTax.Should().Be(13_837.50m);
    }

    [Fact]
    public void Compute_with_a_leave_conversion_lets_the_13th_month_fill_the_exemption_first()
    {
        // Nothing used earlier: 90,000 left. The 13th month's 88,000 fills it first - all exempt,
        // 0 taxable - and the 6,000 leave beyond de minimis takes the 2,000 that remains, so its
        // other 4,000 is the taxable part. Tax unchanged: 4,000 x 25% = 1,000 + 12,837.50 = 13,837.50
        // (Compute_with_a_leave_conversion_taxes_the_other_benefits_past_the_exemption_left_by_the_13th_month).
        var result = _sut.Compute(NewEmployee(), DecemberRun(),
            includeThirteenthMonth: true, basicEarnedEarlierInYear: BasicEarnedJanToNov,
            leaveConversion: new LeaveConversionInput(DeMinimis: 12_000m, OtherBenefits: 6_000m));

        result.ThirteenthMonth.Should().Be(88_000m);
        result.ThirteenthMonthTaxable.Should().Be(0m);
        result.ThirteenthMonthExempt.Should().Be(88_000m);
        result.WithholdingTax.Should().Be(13_837.50m);
        result.GrossPay.Should().Be(88_000m + 88_000m + 18_000m);
    }

    [Fact]
    public void Compute_with_a_leave_conversion_and_the_exemption_partly_used_taxes_the_13th_month_past_what_is_left()
    {
        // 30,000 used earlier: 60,000 left. The 13th month fills it: 60,000 exempt, 28,000 taxable.
        // The leave's 6,000 finds nothing left, all taxable. Total excess 28,000 + 6,000 = 34,000,
        // x 25% = 8,500 + 12,837.50 = 21,337.50.
        var result = _sut.Compute(NewEmployee(), DecemberRun(),
            includeThirteenthMonth: true, basicEarnedEarlierInYear: BasicEarnedJanToNov,
            otherBenefitsExemptUsedEarlierInYear: 30_000m,
            leaveConversion: new LeaveConversionInput(DeMinimis: 12_000m, OtherBenefits: 6_000m));

        result.ThirteenthMonthTaxable.Should().Be(28_000m);
        result.ThirteenthMonthExempt.Should().Be(60_000m);
        result.WithholdingTax.Should().Be(21_337.50m);
    }

    [Fact]
    public void Compute_with_a_leave_conversion_and_no_13th_month_stores_nothing_taxable()
    {
        // The leave's 6,000 is all taxable (the exemption is used up), but none of it is 13th month.
        // Tax: 6,000 x 25% = 1,500 + 12,837.50 = 14,337.50.
        var result = _sut.Compute(NewEmployee(), DecemberRun(),
            otherBenefitsExemptUsedEarlierInYear: 90_000m,
            leaveConversion: new LeaveConversionInput(DeMinimis: 12_000m, OtherBenefits: 6_000m));

        result.ThirteenthMonthTaxable.Should().Be(0m);
        result.WithholdingTax.Should().Be(14_337.50m);
    }

    [Fact]
    public void Compute_a_13th_month_with_centavos_splits_to_the_centavo()
    {
        // 13th month 88,000; 80,000.25 used earlier: 90,000 - 80,000.25 = 9,999.75 left, so
        // 88,000 - 9,999.75 = 78,000.25 taxable and 9,999.75 exempt - the two add up to the 13th month.
        var result = _sut.Compute(NewEmployee(), DecemberRun(),
            includeThirteenthMonth: true, basicEarnedEarlierInYear: BasicEarnedJanToNov,
            otherBenefitsExemptUsedEarlierInYear: 80_000.25m);

        result.ThirteenthMonthTaxable.Should().Be(78_000.25m);
        result.ThirteenthMonthExempt.Should().Be(9_999.75m);
        (result.ThirteenthMonthTaxable + result.ThirteenthMonthExempt).Should().Be(result.ThirteenthMonth);
    }

    [Fact]
    public void Compute_a_final_pay_stores_the_split_even_though_its_tax_is_settled()
    {
        // A final pay at 36,500 a month (daily rate 1,200): 10 working days = 12,000 regular pay.
        // 1,080,000 of basic earned earlier and 85,000 of 13th month paid earlier:
        // 13th month (1,080,000 + 12,000) / 12 = 91,000 - 85,000 = 6,000. The exemption used is the
        // 85,000, so 5,000 is left: 5,000 exempt, 1,000 taxable. The settled tax (the override)
        // replaces the computed withholding, and is untouched by the split.
        var result = _sut.Compute(NewEmployee(basicSalary: 36_500m), FinalPayRun(),
            includeThirteenthMonth: true, basicEarnedEarlierInYear: 1_080_000m,
            thirteenthMonthPaidEarlierInYear: 85_000m,
            finalPay: new FinalPayExtras(
                WorkingDays: 10m,
                LeaveConversionNonTaxable: 0m,
                LeaveConversionOtherBenefits: 0m,
                SeparationPay: 0m,
                RetirementPay: 0m,
                SeparationAndRetirementNonTaxable: 0m,
                Deductions: [],
                WithholdingTaxOverride: 1_234.56m));

        result.RegularPay.Should().Be(12_000m);
        result.ThirteenthMonth.Should().Be(6_000m);
        result.ThirteenthMonthTaxable.Should().Be(1_000m);
        result.ThirteenthMonthExempt.Should().Be(5_000m);
        result.WithholdingTax.Should().Be(1_234.56m);
    }

    [Fact]
    public void ThirteenthMonthExempt_on_an_entry_computed_before_the_split_was_stored_is_the_whole_13th_month()
    {
        // Null on entries computed before this change: their 13th month reads as all exempt.
        var entry = new PayrollRunEmployee { ThirteenthMonth = 50_000m, ThirteenthMonthTaxable = null };

        entry.ThirteenthMonthExempt.Should().Be(50_000m);
    }

    private static EmployeeCompensation NewEmployee(decimal basicSalary = 88_000m) => new()
    {
        BasicSalary = basicSalary,
        PayFrequency = PayFrequency.Monthly
    };

    private static PayrollRun DecemberRun() => new()
    {
        RunNumber = "PAY-2026-024",
        PeriodStart = new DateOnly(2026, 12, 1),
        PeriodEnd = new DateOnly(2026, 12, 31),
        PayDate = new DateOnly(2026, 12, 20),
        Frequency = PayFrequency.Monthly
    };

    private static PayrollRun FinalPayRun() => new()
    {
        RunNumber = "FP-2026-001",
        RunType = PayrollRunType.FinalPay,
        PeriodStart = new DateOnly(2026, 12, 1),
        PeriodEnd = new DateOnly(2026, 12, 14),
        PayDate = new DateOnly(2026, 12, 20),
        Frequency = PayFrequency.Monthly
    };
}
