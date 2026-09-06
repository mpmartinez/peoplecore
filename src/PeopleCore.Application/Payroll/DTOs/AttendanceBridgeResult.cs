namespace PeopleCore.Application.Payroll.DTOs;

/// <summary>
/// What the attendance bridge derived for one payroll period.
/// <para>
/// <see cref="EmployeesWithoutSchedule"/> is a diagnostic, not an error. An employee the bridge
/// could not schedule for any date in the period yields zero <c>AbsenceDays</c> rather than a
/// guessed work week - over-deducting wages is a DOLE compliance problem, under-deducting is a
/// recoverable business one - so the condition has to be visible in operations before anyone is
/// paid.
/// </para>
/// </summary>
public record AttendanceBridgeResult(
    IReadOnlyDictionary<Guid, PayrollAttendanceInput> Inputs,
    IReadOnlyList<Guid> EmployeesWithoutSchedule);
