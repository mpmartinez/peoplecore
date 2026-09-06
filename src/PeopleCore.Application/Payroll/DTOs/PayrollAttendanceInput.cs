namespace PeopleCore.Application.Payroll.DTOs;

/// <summary>
/// Attendance totals for one employee over one payroll period. Phase 1 leaves this null and
/// treats the employee as fully present; Phase 2 populates it from PeopleCore's punches,
/// approved leave and approved overtime.
/// </summary>
public record PayrollAttendanceInput
{
    public decimal LateMinutes { get; init; }
    public decimal AbsenceDays { get; init; }
    public decimal OvertimeHours { get; init; }
    public decimal UndertimeMinutes { get; init; }
    public decimal HolidayRegularDays { get; init; }
    public decimal HolidaySpecialDays { get; init; }
    public decimal NightDiffHours { get; init; }
    public decimal RestDayOTHours { get; init; }
}
