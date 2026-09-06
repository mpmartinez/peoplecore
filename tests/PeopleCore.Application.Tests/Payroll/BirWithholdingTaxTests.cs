using FluentAssertions;
using PeopleCore.Domain.Payroll;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

// The five BIR Form 2316 Item 24 tests from the source suite are deferred to Phase 4, where
// BIR2316Dto and BIR2316Document are ported. They assert Form 2316 semantics, not the bracket
// table this file covers.
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
}
