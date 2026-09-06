using FluentAssertions;
using PeopleCore.Application.Payroll.Services;

namespace PeopleCore.Application.Tests.Payroll;

public class NightDifferentialTests
{
    [Fact]
    public void Hours_ForADayShift_IsZero()
    {
        NightDifferential.Hours(new DateTime(2026, 3, 2, 8, 0, 0), new DateTime(2026, 3, 2, 17, 0, 0))
            .Should().Be(0m);
    }

    [Fact]
    public void Hours_ForANightShiftCrossingMidnight_CountsOnlyTheNightWindow()
    {
        // 22:00 to 06:00 is entirely inside the window: eight hours.
        NightDifferential.Hours(new DateTime(2026, 3, 2, 22, 0, 0), new DateTime(2026, 3, 3, 6, 0, 0))
            .Should().Be(8m);
    }

    [Fact]
    public void Hours_CountsOnlyTheOverlappingPortion()
    {
        // 20:00 to 02:00 overlaps the window from 22:00: four hours, not six.
        NightDifferential.Hours(new DateTime(2026, 3, 2, 20, 0, 0), new DateTime(2026, 3, 3, 2, 0, 0))
            .Should().Be(4m);
    }

    [Fact]
    public void Hours_ForAShiftStartingAfterMidnight_UsesThePreviousDaysWindow()
    {
        // 02:00 to 07:00 sits inside the window that opened at 22:00 the previous day: four hours.
        NightDifferential.Hours(new DateTime(2026, 3, 3, 2, 0, 0), new DateTime(2026, 3, 3, 7, 0, 0))
            .Should().Be(4m);
    }

    [Fact]
    public void Hours_SpanningTwoNights_SumsBothWindows()
    {
        // 2 Mar 20:00 to 4 Mar 08:00: 22:00-06:00 twice = sixteen hours.
        NightDifferential.Hours(new DateTime(2026, 3, 2, 20, 0, 0), new DateTime(2026, 3, 4, 8, 0, 0))
            .Should().Be(16m);
    }

    [Fact]
    public void Hours_WhenTimeOutIsNotAfterTimeIn_IsZero()
    {
        NightDifferential.Hours(new DateTime(2026, 3, 2, 22, 0, 0), new DateTime(2026, 3, 2, 22, 0, 0))
            .Should().Be(0m);
    }
}
