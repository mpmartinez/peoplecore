using System;
using FluentAssertions;
using PeopleCore.Domain.Payroll;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class SssContributionScheduleTests
{
    [Fact]
    public void ForPeriod_returns_the_2025_schedule_on_or_after_its_effective_date()
    {
        var schedule = SssContributionSchedule.ForPeriod(new DateOnly(2025, 1, 1));

        schedule.EffectiveFrom.Should().Be(new DateOnly(2025, 1, 1));
        schedule.Citation.Should().Be("SSS Circular No. 2024-006");
    }

    [Fact]
    public void ForPeriod_throws_for_a_date_before_the_earliest_registered_schedule()
    {
        var act = () => SssContributionSchedule.ForPeriod(new DateOnly(2024, 12, 31));

        act.Should().Throw<NotSupportedException>();
    }
}
