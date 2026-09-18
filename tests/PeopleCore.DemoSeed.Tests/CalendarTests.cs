using FluentAssertions;
using PeopleCore.DemoSeed.Plan;

namespace PeopleCore.DemoSeed.Tests;

/// <summary>Which days are worked and which half-months get paid.</summary>
public class CalendarTests
{
    [Fact]
    public void TheHolidays_AreTheProclaimed2026Dates_JanuaryToSeptember()
    {
        Calendar.Holidays.Select(h => (h.Date, h.IsRegular)).Should().Equal(
            (new DateOnly(2026, 1, 1), true),
            (new DateOnly(2026, 2, 17), false),
            (new DateOnly(2026, 4, 2), true),
            (new DateOnly(2026, 4, 3), true),
            (new DateOnly(2026, 4, 4), false),
            (new DateOnly(2026, 4, 9), true),
            (new DateOnly(2026, 5, 1), true),
            (new DateOnly(2026, 6, 12), true),
            (new DateOnly(2026, 8, 21), false),
            (new DateOnly(2026, 8, 31), true));
    }

    [Fact]
    public void Weekends_AndHolidays_AreNotWorkDays()
    {
        Calendar.IsWorkDay(new DateOnly(2026, 1, 1)).Should().BeFalse();   // New Year's Day, a Thursday
        Calendar.IsWorkDay(new DateOnly(2026, 1, 3)).Should().BeFalse();   // Saturday
        Calendar.IsWorkDay(new DateOnly(2026, 1, 4)).Should().BeFalse();   // Sunday
        Calendar.IsWorkDay(new DateOnly(2026, 1, 2)).Should().BeTrue();    // Friday
    }

    [Fact]
    public void WorkDays_InJanuary2026_AreTwentyOne()
    {
        Calendar.WorkDays(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)).Should().HaveCount(21);
    }

    [Fact]
    public void PayPeriods_TileTheYear_UpToTheLastCompletedHalfMonth()
    {
        var periods = Calendar.CompletedPayPeriods(new DateOnly(2026, 9, 18));

        periods.Should().HaveCount(17);
        periods[0].Should().Be(new PayPeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 15), new DateOnly(2026, 1, 15)));
        periods[1].Should().Be(new PayPeriod(new DateOnly(2026, 1, 16), new DateOnly(2026, 1, 31), new DateOnly(2026, 1, 31)));
        periods[3].End.Should().Be(new DateOnly(2026, 2, 28));
        periods[^1].Should().Be(new PayPeriod(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 15)));

        for (var i = 1; i < periods.Count; i++)
            periods[i].Start.Should().Be(periods[i - 1].End.AddDays(1), "periods leave no gap and do not overlap");
    }

    [Fact]
    public void AHalfMonthEndingToday_IsNotYetComplete()
    {
        Calendar.CompletedPayPeriods(new DateOnly(2026, 9, 15)).Should().HaveCount(16);
        Calendar.CompletedPayPeriods(new DateOnly(2026, 9, 16)).Should().HaveCount(17);
    }
}
