using PeopleCore.Application.Scheduling.DTOs;
using PeopleCore.Domain.Entities.Scheduling;
using PeopleCore.Domain.Enums;

namespace PeopleCore.Application.Scheduling.Services;

/// <summary>
/// Resolves what an employee was scheduled to work on one date.
/// <para>
/// Returns null when the assignment cannot answer for the date - which is NOT the same as a rest
/// day. Payroll needs the distinction: a rest day means "not a working day"; null means "no basis
/// to derive an absence", and deriving one anyway would deduct wages the schedule cannot justify.
/// </para>
/// </summary>
public static class ShiftScheduleResolver
{
    public static DailyScheduleDto? Resolve(EmployeeShiftAssignment? assignment, DateOnly date)
    {
        if (assignment is null) return null;

        if (assignment.ShiftTemplateId.HasValue && assignment.ShiftTemplate is not null)
        {
            var s = assignment.ShiftTemplate;
            var isRestDay = !s.WorkDays.IncludesDay(date.DayOfWeek);
            return new DailyScheduleDto(date, s.Name, s.StartTime, s.EndTime, isRestDay, s.IsNightShift);
        }

        // A rotating pattern's slots - including empty, rest-day slots - are already authoritative
        // about which days it schedules. A pattern deliberately scheduling a Saturday (or any other
        // day) must keep doing so, so the WorkDays check above is never applied here.
        if (assignment.RotatingPatternId.HasValue && assignment.RotatingPattern is not null)
        {
            var pattern = assignment.RotatingPattern;
            var anchorDate = assignment.PatternStartDate ?? assignment.EffectiveFrom;
            var rawOffset = (date.DayNumber - anchorDate.DayNumber) % pattern.CycleLengthDays;
            var dayOffset = rawOffset < 0 ? rawOffset + pattern.CycleLengthDays : rawOffset;
            var slot = pattern.Slots.FirstOrDefault(s => s.DayOffset == dayOffset);

            if (slot is null || slot.ShiftTemplateId is null || slot.ShiftTemplate is null)
                return new DailyScheduleDto(date, null, null, null, true, false); // rest day

            var st = slot.ShiftTemplate;
            return new DailyScheduleDto(date, st.Name, st.StartTime, st.EndTime, false, st.IsNightShift);
        }

        return null;
    }
}
