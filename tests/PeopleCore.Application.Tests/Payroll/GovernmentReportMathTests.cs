using FluentAssertions;
using PeopleCore.Application.Payroll.GovernmentReports;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class GovernmentReportMathTests
{
    // Circular 2024-006: the employee pays 5% of the MSC; EC is 10.00 below an MSC of 15,000 and
    // 30.00 from 15,000 up.
    [Theory]
    [InlineData(250.00, 5_000, 10)]      // the floor
    [InlineData(725.00, 14_500, 10)]     // last 10.00 bracket
    [InlineData(750.00, 15_000, 30)]     // EC steps up
    [InlineData(1_750.00, 35_000, 30)]   // the ceiling
    public void SssCredit_WorksBackTheMscAndEcFromTheEmployeeShare(decimal share, decimal msc, decimal ec)
    {
        GovernmentReportMath.SssCredit(share).Should().Be((msc, ec));
    }

    [Fact]
    public void SssCredit_AddsTwoCutoffsBeforeWorkingBack()
    {
        // Two semi-monthly halves of 512.50 are one 1,025.00 month: MSC 20,500.
        GovernmentReportMath.SssCredit(512.50m + 512.50m).Should().Be((20_500m, 30m));
    }

    [Fact]
    public void SssCredit_IsBlankWithNothingDeducted()
    {
        GovernmentReportMath.SssCredit(0m).Should().Be(((decimal?)null, (decimal?)null));
    }

    [Theory]
    [InlineData(30_000, 0, 30_000)]        // nothing used yet
    [InlineData(40_000, 60_000, 30_000)]   // 30,000 of the exemption left
    [InlineData(40_000, 90_000, 0)]        // all used earlier in the year
    [InlineData(40_000, 120_000, 0)]       // more than the exemption paid earlier
    public void NonTaxableThirteenthMonth_IsWhatIsLeftOfThe90kExemption(decimal thisMonth, decimal earlier, decimal expected)
    {
        GovernmentReportMath.NonTaxableThirteenthMonth(thisMonth, earlier).Should().Be(expected);
    }

    [Fact]
    public void Money_IsInvariantWithTwoDecimalsAndNoSeparator()
    {
        GovernmentReportMath.Money(12_345.5m).Should().Be("12345.50");
    }
}
