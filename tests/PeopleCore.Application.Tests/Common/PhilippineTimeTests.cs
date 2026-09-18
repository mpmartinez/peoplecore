using FluentAssertions;
using PeopleCore.Application.Common.Time;
using Xunit;

namespace PeopleCore.Application.Tests.Common;

public class PhilippineTimeTests
{
    [Theory]
    [InlineData("2026-03-12T00:07:00Z", "2026-03-12T08:07:00")]
    [InlineData("2026-03-11T23:55:00Z", "2026-03-12T07:55:00")]   // crosses midnight into the next Manila day
    [InlineData("2026-12-31T16:00:00Z", "2027-01-01T00:00:00")]   // and the year
    [InlineData("2026-07-01T09:00:00+02:00", "2026-07-01T15:00:00")] // the clock's own offset is irrelevant
    public void Now_IsTheManilaWallClock_LabelledUtc(string instant, string wallClock)
    {
        var now = PhilippineTime.Now(new FixedClock(instant));

        now.Should().Be(DateTime.SpecifyKind(DateTime.Parse(wallClock, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc));
        now.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Now_DropsFractionsOfASecond()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 3, 12, 0, 7, 0, TimeSpan.Zero).AddTicks(5_999_999));

        PhilippineTime.Now(clock).Should().Be(new DateTime(2026, 3, 12, 8, 7, 0, DateTimeKind.Utc));
    }
}
