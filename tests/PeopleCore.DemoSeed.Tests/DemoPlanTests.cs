using FluentAssertions;
using PeopleCore.DemoSeed.Plan;

namespace PeopleCore.DemoSeed.Tests;

public class DemoPlanTests
{
    [Fact]
    public void TheSameSeedAndDay_BuildTheSamePlan()
    {
        var a = DemoPlan.Build(20260918, new DateOnly(2026, 9, 18));
        var b = DemoPlan.Build(20260918, new DateOnly(2026, 9, 18));

        b.People.Should().BeEquivalentTo(a.People);
        b.Leave.Should().Equal(a.Leave);
        b.Overtime.Should().Equal(a.Overtime);
        b.Applicants.Should().Equal(a.Applicants);
    }

    [Fact]
    public void Months_RunFromJanuaryToTodaysMonth()
    {
        DemoPlan.Build(1, new DateOnly(2026, 9, 18)).Months.Should().Equal(1, 2, 3, 4, 5, 6, 7, 8, 9);
    }

    [Theory]
    [InlineData(2026, 1, 20)]
    [InlineData(2027, 3, 1)]
    public void ADayTheCalendarCannotCover_IsRefused(int year, int month, int day)
    {
        var act = () => DemoPlan.Build(1, new DateOnly(year, month, day));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
