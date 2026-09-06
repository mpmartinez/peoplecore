using FluentAssertions;
using PeopleCore.Application.Scheduling.Services;
using PeopleCore.Domain.Entities.Scheduling;

namespace PeopleCore.Application.Tests.Scheduling;

public class ShiftScheduleResolverTests
{
    private static ShiftTemplate Day() => new()
    {
        Name = "Day", StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(17, 0), IsNightShift = false
    };

    [Fact]
    public void Resolve_WhenAssignmentIsNull_ReturnsNull()
    {
        // Null means "no basis to say anything about this day" - NOT a rest day.
        ShiftScheduleResolver.Resolve(null, new DateOnly(2026, 3, 2)).Should().BeNull();
    }

    [Fact]
    public void Resolve_WithFixedShift_ReturnsThatShiftAndIsNotARestDay()
    {
        var template = Day();
        var assignment = new EmployeeShiftAssignment
        {
            ShiftTemplateId = template.Id,
            ShiftTemplate = template,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };

        var result = ShiftScheduleResolver.Resolve(assignment, new DateOnly(2026, 3, 2));

        result.Should().NotBeNull();
        result!.IsRestDay.Should().BeFalse();
        result.ShiftName.Should().Be("Day");
        result.StartTime.Should().Be(new TimeOnly(8, 0));
    }

    [Fact]
    public void Resolve_WithRotatingPattern_MarksAnEmptySlotAsARestDay()
    {
        var template = Day();
        var pattern = new RotatingPattern { Name = "2-on-1-off", CycleLengthDays = 3 };
        pattern.Slots.Add(new RotatingPatternSlot { DayOffset = 0, ShiftTemplateId = template.Id, ShiftTemplate = template });
        pattern.Slots.Add(new RotatingPatternSlot { DayOffset = 1, ShiftTemplateId = template.Id, ShiftTemplate = template });
        pattern.Slots.Add(new RotatingPatternSlot { DayOffset = 2 });   // rest day

        var assignment = new EmployeeShiftAssignment
        {
            RotatingPatternId = pattern.Id,
            RotatingPattern = pattern,
            PatternStartDate = new DateOnly(2026, 3, 1),
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };

        ShiftScheduleResolver.Resolve(assignment, new DateOnly(2026, 3, 1))!.IsRestDay.Should().BeFalse();
        ShiftScheduleResolver.Resolve(assignment, new DateOnly(2026, 3, 3))!.IsRestDay.Should().BeTrue();
    }

    [Fact]
    public void Resolve_WithRotatingPattern_HandlesADateBeforeTheAnchor()
    {
        // The day offset must stay non-negative; C# % yields a negative for dates before the anchor.
        var template = Day();
        var pattern = new RotatingPattern { Name = "2-on-1-off", CycleLengthDays = 3 };
        pattern.Slots.Add(new RotatingPatternSlot { DayOffset = 0, ShiftTemplateId = template.Id, ShiftTemplate = template });
        pattern.Slots.Add(new RotatingPatternSlot { DayOffset = 1, ShiftTemplateId = template.Id, ShiftTemplate = template });
        pattern.Slots.Add(new RotatingPatternSlot { DayOffset = 2 });

        var assignment = new EmployeeShiftAssignment
        {
            RotatingPatternId = pattern.Id,
            RotatingPattern = pattern,
            PatternStartDate = new DateOnly(2026, 3, 1),
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };

        // 2026-02-28 is one day before the anchor: (date - anchor) % cycle is -1 in C#, which must
        // be normalized to a non-negative offset (2, here) rather than left negative or truncated.
        ShiftScheduleResolver.Resolve(assignment, new DateOnly(2026, 2, 28))!.IsRestDay.Should().BeTrue();
    }
}
