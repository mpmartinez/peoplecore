using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Scheduling.Services;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Interfaces;

namespace PeopleCore.Application.Leave.Services;

/// <summary>
/// Counts a leave request's days, split by calendar year.
/// <para>
/// A calendar-day type (<see cref="LeaveType.CountsCalendarDays"/>) counts every date from start
/// to end - holidays included. Every other type counts only the dates the employee's shift
/// schedules: <see cref="ShiftScheduleResolver.Resolve"/> not null and not a rest day, with
/// Monday-to-Friday counted where there is no assignment to answer for the date at all. Either
/// way, a working date is still skipped when <see cref="IHolidayService.IsHolidayAsync"/> finds a
/// regular or special non-working holiday there; a special working day counts as usual.
/// </para>
/// </summary>
public sealed class LeaveDayCounter : ILeaveDayCounter
{
    private readonly IShiftAssignmentRepository _assignments;
    private readonly IHolidayService _holidays;

    public LeaveDayCounter(IShiftAssignmentRepository assignments, IHolidayService holidays)
    {
        _assignments = assignments;
        _holidays = holidays;
    }

    public async Task<IReadOnlyDictionary<int, decimal>> CountByYearAsync(
        Guid employeeId, LeaveType type, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(type);

        // The start year is always present, even at 0 - callers key balances/messages off it.
        var counts = new Dictionary<int, decimal> { [start.Year] = 0m };

        if (type.CountsCalendarDays)
        {
            for (var date = start; date <= end; date = date.AddDays(1))
                counts[date.Year] = counts.GetValueOrDefault(date.Year) + 1m;

            return counts;
        }

        // Loaded once for the whole range, the same way PayrollAttendanceBridge does, then picked
        // per date with the shared helper rather than re-querying per date.
        var assignments = await _assignments.GetActiveForPeriodAsync([employeeId], start, end, ct);

        for (var date = start; date <= end; date = date.AddDays(1))
        {
            var assignment = ShiftScheduleResolver.PickAssignment(assignments, date);
            var schedule = ShiftScheduleResolver.Resolve(assignment, date);

            // No schedule means no basis to say the date is a rest day - fall back to the plain
            // working week. Either way, a holiday on a working day is not a leave day.
            var isWorkingDay = schedule is null
                ? date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
                : !schedule.IsRestDay;

            if (isWorkingDay && !IsDayOff(await _holidays.IsHolidayAsync(date, ct)))
                counts[date.Year] = counts.GetValueOrDefault(date.Year) + 1m;
        }

        return counts;
    }

    /// <summary>
    /// A regular or special non-working holiday is a day off. A special working day is proclaimed
    /// but is an ordinary working day - the same reading <c>PayrollAttendanceBridge</c> gives it.
    /// </summary>
    private static bool IsDayOff(HolidayType? holiday) =>
        holiday is HolidayType.RegularHoliday or HolidayType.SpecialNonWorking;
}
