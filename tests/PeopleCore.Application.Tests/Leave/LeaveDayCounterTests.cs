using FluentAssertions;
using Moq;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Leave.Services;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Entities.Scheduling;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Interfaces;

namespace PeopleCore.Application.Tests.Leave;

public class LeaveDayCounterTests
{
    private readonly Mock<IShiftAssignmentRepository> _assignments = new();
    private readonly Mock<IHolidayService> _holidays = new();
    private readonly LeaveDayCounter _sut;

    public LeaveDayCounterTests()
    {
        _assignments.Setup(r => r.GetActiveForPeriodAsync(
                        It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync([]);
        _holidays.Setup(h => h.IsHolidayAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync((HolidayType?)null);

        _sut = new LeaveDayCounter(_assignments.Object, _holidays.Object);
    }

    private static ShiftTemplate DayShift(WorkDays workDays) => new()
    {
        Name = "Day", StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(17, 0), WorkDays = workDays
    };

    private static EmployeeShiftAssignment FixedAssignment(Guid employeeId, DateOnly effectiveFrom, WorkDays workDays)
    {
        var template = DayShift(workDays);
        return new EmployeeShiftAssignment
        {
            EmployeeId = employeeId,
            ShiftTemplateId = template.Id,
            ShiftTemplate = template,
            EffectiveFrom = effectiveFrom
        };
    }

    /// <summary>A 2-on-1-off pattern anchored at <paramref name="anchor"/>; offset 2 is a rest day.</summary>
    private static EmployeeShiftAssignment RotatingAssignment(Guid employeeId, DateOnly anchor)
    {
        var template = DayShift(WorkDays.AllDays);
        var pattern = new RotatingPattern { Name = "2-on-1-off", CycleLengthDays = 3 };
        pattern.Slots.Add(new RotatingPatternSlot { DayOffset = 0, ShiftTemplateId = template.Id, ShiftTemplate = template });
        pattern.Slots.Add(new RotatingPatternSlot { DayOffset = 1, ShiftTemplateId = template.Id, ShiftTemplate = template });
        pattern.Slots.Add(new RotatingPatternSlot { DayOffset = 2 }); // rest day

        return new EmployeeShiftAssignment
        {
            EmployeeId = employeeId,
            RotatingPatternId = pattern.Id,
            RotatingPattern = pattern,
            PatternStartDate = anchor,
            EffectiveFrom = anchor
        };
    }

    private static LeaveType CalendarType() => new() { Name = "Maternity", CountsCalendarDays = true };
    private static LeaveType WorkingDayType() => new() { Name = "Vacation", CountsCalendarDays = false };

    [Fact]
    public async Task CountByYearAsync_CalendarDayType_CountsEveryDateAndSplitsByYear()
    {
        var employeeId = Guid.NewGuid();
        var start = new DateOnly(2026, 12, 30);
        var end = new DateOnly(2027, 1, 2);

        var result = await _sut.CountByYearAsync(employeeId, CalendarType(), start, end);

        result.Should().BeEquivalentTo(new Dictionary<int, decimal> { [2026] = 2m, [2027] = 2m });
    }

    [Fact]
    public async Task CountByYearAsync_NoAssignment_CountsMondayToFridayOnly()
    {
        var employeeId = Guid.NewGuid();
        // Monday 2026-03-02 through Sunday 2026-03-08.
        var start = new DateOnly(2026, 3, 2);
        var end = new DateOnly(2026, 3, 8);

        var result = await _sut.CountByYearAsync(employeeId, WorkingDayType(), start, end);

        result.Should().BeEquivalentTo(new Dictionary<int, decimal> { [2026] = 5m });
    }

    [Fact]
    public async Task CountByYearAsync_FixedMondayToSaturdayTemplate_CountsSixDaysForTheSameWeek()
    {
        var employeeId = Guid.NewGuid();
        var start = new DateOnly(2026, 3, 2);
        var end = new DateOnly(2026, 3, 8);

        _assignments.Setup(r => r.GetActiveForPeriodAsync(
                        It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync([FixedAssignment(employeeId, start, WorkDays.MondayToSaturday)]);

        var result = await _sut.CountByYearAsync(employeeId, WorkingDayType(), start, end);

        result.Should().BeEquivalentTo(new Dictionary<int, decimal> { [2026] = 6m });
    }

    [Fact]
    public async Task CountByYearAsync_RotatingPattern_SkipsRestDaySlots()
    {
        var employeeId = Guid.NewGuid();
        var anchor = new DateOnly(2026, 3, 1); // offset 0
        var end = new DateOnly(2026, 3, 6);    // 6 days: offsets 0,1,2,0,1,2 -> 4 working, 2 rest

        _assignments.Setup(r => r.GetActiveForPeriodAsync(
                        It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync([RotatingAssignment(employeeId, anchor)]);

        var result = await _sut.CountByYearAsync(employeeId, WorkingDayType(), anchor, end);

        result.Should().BeEquivalentTo(new Dictionary<int, decimal> { [2026] = 4m });
    }

    [Fact]
    public async Task CountByYearAsync_HolidayOnAScheduledDay_IsSkipped()
    {
        var employeeId = Guid.NewGuid();
        var start = new DateOnly(2026, 3, 2);
        var end = new DateOnly(2026, 3, 4);
        var holiday = new DateOnly(2026, 3, 3);

        _assignments.Setup(r => r.GetActiveForPeriodAsync(
                        It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync([FixedAssignment(employeeId, start, WorkDays.MondayToFriday)]);
        _holidays.Setup(h => h.IsHolidayAsync(holiday, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(HolidayType.RegularHoliday);

        var result = await _sut.CountByYearAsync(employeeId, WorkingDayType(), start, end);

        result.Should().BeEquivalentTo(new Dictionary<int, decimal> { [2026] = 2m });
    }

    [Fact]
    public async Task CountByYearAsync_NoAssignment_SkipsHolidaysInTheMondayToFridayFallback()
    {
        var employeeId = Guid.NewGuid();
        // Monday 2025-12-22 through Friday 2025-12-26, no assignment. Dec 24 and 25 are holidays,
        // so 22, 23 and 26 count.
        var start = new DateOnly(2025, 12, 22);
        var end = new DateOnly(2025, 12, 26);

        _holidays.Setup(h => h.IsHolidayAsync(new DateOnly(2025, 12, 24), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(HolidayType.SpecialNonWorking);
        _holidays.Setup(h => h.IsHolidayAsync(new DateOnly(2025, 12, 25), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(HolidayType.RegularHoliday);

        var result = await _sut.CountByYearAsync(employeeId, WorkingDayType(), start, end);

        result.Should().BeEquivalentTo(new Dictionary<int, decimal> { [2025] = 3m });
    }

    [Fact]
    public async Task CountByYearAsync_OnlyRestDays_GivesZeroForTheStartYear()
    {
        var employeeId = Guid.NewGuid();
        var anchor = new DateOnly(2026, 3, 1); // offset 0
        var restDay = new DateOnly(2026, 3, 3); // offset 2

        _assignments.Setup(r => r.GetActiveForPeriodAsync(
                        It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync([RotatingAssignment(employeeId, anchor)]);

        var result = await _sut.CountByYearAsync(employeeId, WorkingDayType(), restDay, restDay);

        result.Should().BeEquivalentTo(new Dictionary<int, decimal> { [2026] = 0m });
    }

    [Fact]
    public async Task CountByYearAsync_WorkingDayRequestOverNewYear_SplitsByYear()
    {
        var employeeId = Guid.NewGuid();
        // Thursday 2026-12-31 through Monday 2027-01-04, no assignment: Mon-Fri counts.
        var start = new DateOnly(2026, 12, 31);
        var end = new DateOnly(2027, 1, 4);

        var result = await _sut.CountByYearAsync(employeeId, WorkingDayType(), start, end);

        // 2026-12-31 Thu counts; 2027-01-01 Fri, 01-02 Sat(no), 01-03 Sun(no), 01-04 Mon counts.
        result.Should().BeEquivalentTo(new Dictionary<int, decimal> { [2026] = 1m, [2027] = 2m });
    }

    [Fact]
    public async Task CountByYearAsync_AlwaysContainsTheStartYearEvenWithZeroDays()
    {
        var employeeId = Guid.NewGuid();
        var start = new DateOnly(2026, 3, 7); // Saturday, no assignment
        var end = new DateOnly(2026, 3, 7);

        var result = await _sut.CountByYearAsync(employeeId, WorkingDayType(), start, end);

        result.Should().ContainKey(2026).WhoseValue.Should().Be(0m);
    }
}
