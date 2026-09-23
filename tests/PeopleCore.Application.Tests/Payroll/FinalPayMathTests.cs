using FluentAssertions;
using PeopleCore.Application.Payroll.FinalPay;
using PeopleCore.Domain.Enums;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class FinalPayMathTests
{
    [Theory]
    [InlineData("2021-03-01", "2026-02-28", 5)]   // 4 years 11 months 27 days: the fraction is 6+ months, so 5
    [InlineData("2021-03-01", "2025-08-31", 4)]   // 4 years 5 months 30 days: under 6 months, so 4
    [InlineData("2021-03-01", "2025-09-01", 5)]   // 4 years 6 months exactly: counts
    [InlineData("2026-01-05", "2026-03-31", 0)]   // under 6 months of service
    [InlineData("2025-10-01", "2026-04-01", 1)]   // 6 months exactly
    public void ServiceYears_RoundsASixMonthFractionUpToAWholeYear(string hired, string lastDay, int expected)
    {
        FinalPayMath.ServiceYears(DateOnly.Parse(hired), DateOnly.Parse(lastDay)).Should().Be(expected);
    }

    [Theory]
    [InlineData(AuthorizedCause.Redundancy, 5, 150_000)]              // one month per year
    [InlineData(AuthorizedCause.LaborSavingDevices, 5, 150_000)]
    [InlineData(AuthorizedCause.Retrenchment, 5, 75_000)]             // half a month per year
    [InlineData(AuthorizedCause.ClosureNotDueToLosses, 5, 75_000)]
    [InlineData(AuthorizedCause.Disease, 5, 75_000)]
    [InlineData(AuthorizedCause.Retrenchment, 1, 30_000)]             // half of one month is below the one-month minimum
    [InlineData(AuthorizedCause.Redundancy, 0, 30_000)]               // under six months still gets the minimum
    [InlineData(AuthorizedCause.ClosureDueToSeriousLosses, 5, 0)]     // no separation pay
    public void SeparationPay_FollowsArticles298And299(AuthorizedCause cause, int years, decimal expected)
    {
        FinalPayMath.SeparationPay(cause, monthlyBasic: 30_000m, years).Should().Be(expected);
    }

    [Theory]
    [InlineData("1966-03-01", "2026-03-01", 5, true)]    // turns 60 on the last day
    [InlineData("1966-03-02", "2026-03-01", 5, false)]   // 59 on the last day
    [InlineData("1961-03-01", "2026-03-01", 5, true)]    // exactly 65 on the last day
    [InlineData("1960-03-01", "2026-03-01", 5, false)]   // 66 on the last day, past RA 7641's 65
    [InlineData("1964-01-01", "2026-03-01", 4, false)]   // under 5 years of service
    public void RetirementEligibility_Needs60To65AndFiveYears(string born, string lastDay, int years, bool expected)
    {
        FinalPayMath.IsRetirementEligible(DateOnly.Parse(born), DateOnly.Parse(lastDay), years).Should().Be(expected);
    }

    [Fact]
    public void RetirementPay_Is22AndAHalfDaysPerYear()
    {
        FinalPayMath.RetirementPay(dailyRate: 1_200m, serviceYears: 10).Should().Be(270_000m);   // 1,200 × 22.5 × 10
    }

    [Fact]
    public void LeaveConversion_TreatsTheFirstTenVacationDaysAsDeMinimis()
    {
        var (deMinimis, otherBenefits) = FinalPayMath.LeaveConversion(
            [(8m, true), (5m, true), (3m, false)], dailyRate: 1_000m);

        deMinimis.Should().Be(10_000m, "10 of the 13 vacation-type days");
        otherBenefits.Should().Be(6_000m, "the other 3 vacation days, plus 3 days of a type that doesn't count as vacation");
    }

    [Theory]
    // Nothing used earlier: 90,000 - 4,341.67 13th month = 85,658.33 left; all 36,000 fits.
    [InlineData(36_000, 4_341.67, 0, 36_000)]
    // 90,000 - 0 - 30,342.47 = 59,657.53 left of 98,630.20.
    [InlineData(98_630.20, 30_342.47, 0, 59_657.53)]
    // 80,000 used earlier: 90,000 - 80,000 - 5,000 = 5,000 left of 10,000.
    [InlineData(10_000, 5_000, 80_000, 5_000)]
    // The 13th month alone fills what's left: 90,000 - 88,000 - 5,000 < 0 -> nothing.
    [InlineData(10_000, 5_000, 88_000, 0)]
    // Past the cap already: nothing.
    [InlineData(10_000, 0, 95_000, 0)]
    public void OtherBenefitsExempt_FillsWhatTheThirteenthMonthLeavesOfTheYearsExemption(
        decimal otherBenefits, decimal thirteenthMonth, decimal usedEarlierInYear, decimal expected)
    {
        FinalPayMath.OtherBenefitsExempt(otherBenefits, thirteenthMonth, usedEarlierInYear).Should().Be(expected);
    }

    [Fact]
    public void LeaveConversion_IgnoresNegativeBalances()
    {
        FinalPayMath.LeaveConversion([(-2m, true), (4m, true)], 1_000m).Should().Be((4_000m, 0m));
    }
}
