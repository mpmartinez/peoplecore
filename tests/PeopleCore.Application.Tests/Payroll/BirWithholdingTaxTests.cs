using FluentAssertions;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Domain.Payroll;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class BirWithholdingTaxTests
{
    // Expectations derived from the BIR TRAIN Law annual bracket table (2023 onwards):
    //   over 250,000 -> 15% of the excess
    //   over 400,000 -> 22,500 + 20% of the excess
    //   over 800,000 -> 102,500 + 25% of the excess
    //   over 2,000,000 -> 402,500 + 30% of the excess
    //   over 8,000,000 -> 2,202,500 + 35% of the excess
    [Theory]
    [InlineData(0, 0)]
    [InlineData(250_000, 0)]              // at the threshold, not over it
    [InlineData(250_001, 0.15)]           // 15% of 1
    [InlineData(400_000, 22_500)]         // 15% of 150,000
    [InlineData(400_001, 22_500.20)]      // 22,500 + 20% of 1
    [InlineData(800_000, 102_500)]        // 22,500 + 20% of 400,000
    [InlineData(2_000_000, 402_500)]      // 102,500 + 25% of 1,200,000
    [InlineData(8_000_000, 2_202_500)]    // 402,500 + 30% of 6,000,000
    [InlineData(10_000_000, 2_902_500)]   // 2,202,500 + 35% of 2,000,000
    public void ComputeAnnualTaxDue_applies_the_bracket_base_and_rate(decimal annualTaxable, decimal expected)
    {
        BirWithholdingTax.ComputeAnnualTaxDue(annualTaxable).Should().Be(expected);
    }

    [Fact]
    public void Item24_is_the_liability_on_item_23_not_the_amount_withheld()
    {
        // Gross taxable of 500,000 for the year: 22,500 + 20% of 100,000 = 42,500 due.
        var dto = new Bir2316Dto
        {
            Item39_BasicSalary = 500_000m,
            Item25A_PresentTaxWithheld = 1_000m   // deliberately under-withheld
        };

        dto.Item23_GrossTaxable.Should().Be(500_000m);
        dto.Item24_TaxDue.Should().Be(42_500m);
        dto.Item26_TotalTaxWithheld.Should().Be(1_000m);

        dto.Item24_TaxDue.Should().NotBe(dto.Item26_TotalTaxWithheld,
            "under-withholding must stay visible on the certificate - the employee attests " +
            "under substituted filing that tax due equals tax withheld");
    }

    [Fact]
    public void Item24_matches_item_26_when_withholding_was_correct()
    {
        var dto = new Bir2316Dto
        {
            Item39_BasicSalary = 500_000m,
            Item25A_PresentTaxWithheld = 42_500m
        };

        dto.Item24_TaxDue.Should().Be(dto.Item26_TotalTaxWithheld,
            "this equality is what qualifies the employee for substituted filing");
    }

    [Fact]
    public void Item24_includes_taxable_compensation_from_a_previous_employer()
    {
        // Item 23 sums the present (Item 21) and previous (Item 22) employer's taxable income,
        // so tax due must be computed on the combined figure, not the present employer alone.
        var dto = new Bir2316Dto
        {
            Item39_BasicSalary = 300_000m,
            Item22_PrevTaxableCompensation = 200_000m
        };

        dto.Item23_GrossTaxable.Should().Be(500_000m);
        dto.Item24_TaxDue.Should().Be(42_500m);
    }

    [Fact]
    public void Item24_is_zero_when_annual_taxable_income_is_within_the_exempt_threshold()
    {
        var dto = new Bir2316Dto { Item39_BasicSalary = 250_000m };

        dto.Item24_TaxDue.Should().Be(0m);
    }

    [Fact]
    public void Non_taxable_income_does_not_raise_the_tax_due()
    {
        // Item 36 (mandatory contributions) is non-taxable and feeds Item 38, never Item 23.
        var dto = new Bir2316Dto
        {
            Item39_BasicSalary = 250_000m,
            Item36_SssPhicPagibigContributions = 50_000m,
            Item34_ThirteenthMonthAndBenefits = 90_000m
        };

        dto.Item23_GrossTaxable.Should().Be(250_000m);
        dto.Item24_TaxDue.Should().Be(0m);
    }
}
