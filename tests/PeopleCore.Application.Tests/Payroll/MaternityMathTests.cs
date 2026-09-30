using FluentAssertions;
using PeopleCore.Application.Payroll.Maternity;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class MaternityMathTests
{
    private static DateOnly D(string s) => DateOnly.Parse(s);

    [Fact]
    public void Window_is_the_twelve_months_before_the_semester_of_contingency()
    {
        // Mar 10, 2026 -> semester Oct 2025 to Mar 2026 -> window Oct 2024 to Sep 2025.
        var (from, to) = MaternityMath.ContributionWindow(D("2026-03-10"));
        from.Should().Be(D("2024-10-01"));
        to.Should().Be(D("2025-09-30"));
    }

    [Fact]
    public void Window_for_a_contingency_in_the_second_quarter_is_the_prior_calendar_year()
    {
        // Semester Jan to Jun 2026 -> window Jan to Dec 2025.
        var (from, to) = MaternityMath.ContributionWindow(D("2026-06-30"));
        from.Should().Be(D("2025-01-01"));
        to.Should().Be(D("2025-12-31"));
    }

    [Fact]
    public void Window_for_a_contingency_in_the_third_quarter_starts_in_april_of_the_prior_year()
    {
        // Jul 1, 2026 -> semester Apr to Sep 2026 -> window Apr 2025 to Mar 2026.
        var (from, to) = MaternityMath.ContributionWindow(D("2026-07-01"));
        from.Should().Be(D("2025-04-01"));
        to.Should().Be(D("2026-03-31"));
    }

    [Fact]
    public void Window_for_a_contingency_in_the_fourth_quarter_starts_in_july()
    {
        // Semester Jul to Dec 2026 -> window Jul 2025 to Jun 2026.
        var (from, to) = MaternityMath.ContributionWindow(D("2026-12-31"));
        from.Should().Be(D("2025-07-01"));
        to.Should().Be(D("2026-06-30"));
    }

    [Fact]
    public void Suggested_allowance_is_the_six_highest_credits_over_180()
    {
        // The six highest sum to 120,000; the two lowest are left out.
        var credits = new[] { 5000m, 20000m, 20000m, 20000m, 20000m, 20000m, 20000m, 5000m };
        MaternityMath.SuggestedDailyAllowance(credits).Should().Be(666.67m);
    }

    [Fact]
    public void Suggested_allowance_uses_all_credits_when_fewer_than_six()
    {
        MaternityMath.SuggestedDailyAllowance(new[] { 20000m, 20000m, 10000m, 5000m })
            .Should().Be(Math.Round(55000m / 180m, 2, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void Suggested_allowance_is_null_without_credits()
        => MaternityMath.SuggestedDailyAllowance(Array.Empty<decimal>()).Should().BeNull();

    [Fact]
    public void Suggested_allowance_ignores_non_positive_credits()
    {
        MaternityMath.SuggestedDailyAllowance(new[] { 0m, -5m }).Should().BeNull();
        MaternityMath.SuggestedDailyAllowance(new[] { 18000m, 0m }).Should().Be(100m);
    }

    [Fact]
    public void Benefit_is_the_allowance_times_the_days_rounded_to_centavos()
        => MaternityMath.Benefit(666.67m, 105m).Should().Be(70000.35m);

    [Fact]
    public void Benefit_rounds_half_away_from_zero()
        => MaternityMath.Benefit(0.05m, 0.5m).Should().Be(0.03m);

    [Fact]
    public void Days_in_period_counts_the_request_inside_the_period()
        => MaternityMath.DaysInPeriod(D("2026-03-01"), D("2026-03-20"), D("2026-03-01"), D("2026-03-31")).Should().Be(20);

    [Fact]
    public void Days_in_period_clips_a_request_that_straddles_both_ends()
        => MaternityMath.DaysInPeriod(D("2026-02-10"), D("2026-05-20"), D("2026-03-01"), D("2026-03-31")).Should().Be(31);

    [Fact]
    public void Days_in_period_clips_a_request_that_starts_before_the_period()
        => MaternityMath.DaysInPeriod(D("2026-02-10"), D("2026-03-05"), D("2026-03-01"), D("2026-03-31")).Should().Be(5);

    [Fact]
    public void Days_in_period_clips_a_request_that_ends_after_the_period()
        => MaternityMath.DaysInPeriod(D("2026-03-25"), D("2026-05-20"), D("2026-03-01"), D("2026-03-31")).Should().Be(7);

    [Fact]
    public void Days_in_period_is_zero_when_they_do_not_overlap()
        => MaternityMath.DaysInPeriod(D("2026-04-01"), D("2026-05-20"), D("2026-03-01"), D("2026-03-31")).Should().Be(0);

    [Fact]
    public void Offset_is_the_allowance_for_the_maternity_days()
        => MaternityMath.Offset(20000m, 666.67m, 15).Should().Be(10000.05m);

    [Fact]
    public void The_maximum_daily_allowance_is_the_regular_ss_ceiling_times_6_over_180()
        // 20,000 x 6 / 180 = 666.666... -> 666.67.
        => MaternityMath.MaximumDailyAllowance.Should().Be(666.67m);

    [Fact]
    public void Offset_is_capped_at_the_maternity_days_pay()
        => MaternityMath.Offset(5000m, 666.67m, 15).Should().Be(5000m);

    [Fact]
    public void Maternity_days_pay_is_the_regular_pay_share_of_the_maternity_days()
        // 15,000 x 6 / 15 = 6,000.00.
        => MaternityMath.MaternityDaysPay(15000m, 6, 15).Should().Be(6000m);

    [Fact]
    public void Maternity_days_pay_rounds_to_centavos()
        // 10,000 x 15 / 31 = 4,838.709... -> 4,838.71.
        => MaternityMath.MaternityDaysPay(10000m, 15, 31).Should().Be(4838.71m);

    [Fact]
    public void Maternity_days_pay_is_zero_without_days_or_a_period()
    {
        MaternityMath.MaternityDaysPay(10000m, 0, 31).Should().Be(0m);
        MaternityMath.MaternityDaysPay(10000m, 15, 0).Should().Be(0m);
    }

    [Fact]
    public void The_differential_is_the_maternity_days_pay_the_offset_leaves()
    {
        // 6,000.00 for the days less 4,000.02 SSS covers = 1,999.98.
        MaternityMath.Differential(6000m, 4000.02m).Should().Be(1999.98m);
        // SSS covers all of it: nothing left.
        MaternityMath.Differential(1200m, 1200m).Should().Be(0m);
    }

    [Fact]
    public void Offset_is_zero_without_maternity_days()
        => MaternityMath.Offset(20000m, 666.67m, 0).Should().Be(0m);

    [Fact]
    public void Exempt_offset_is_the_regular_pay_share_of_the_maternity_days()
        => MaternityMath.ExemptOffset(31000m, 15, 31).Should().Be(15000m);

    [Fact]
    public void Exempt_offset_rounds_to_centavos()
        => MaternityMath.ExemptOffset(10000m, 15, 31).Should().Be(4838.71m);

    [Fact]
    public void Exempt_offset_floors_at_regular_pay()
        => MaternityMath.ExemptOffset(10000m, 40, 31).Should().Be(10000m);

    [Fact]
    public void Exempt_offset_is_zero_without_a_period()
        => MaternityMath.ExemptOffset(10000m, 15, 0).Should().Be(0m);
}
