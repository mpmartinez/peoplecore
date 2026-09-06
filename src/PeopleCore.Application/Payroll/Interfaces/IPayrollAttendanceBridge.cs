using PeopleCore.Application.Payroll.DTOs;

namespace PeopleCore.Application.Payroll.Interfaces;

/// <summary>
/// Derives each employee's <see cref="PayrollAttendanceInput"/> for a pay period from PeopleCore's
/// own punches, approved leave, approved overtime, holiday calendar and shift schedule. Reads
/// only; the caller snapshots the result onto the payroll run.
/// </summary>
public interface IPayrollAttendanceBridge
{
    /// <summary>
    /// Builds the totals for every requested employee. Missing data is never an error - an
    /// employee with no records simply accumulates zeros.
    /// </summary>
    /// <exception cref="ArgumentException">When <paramref name="to"/> precedes <paramref name="from"/>.</exception>
    Task<AttendanceBridgeResult> BuildAsync(
        IReadOnlyList<Guid> employeeIds, DateOnly from, DateOnly to, CancellationToken ct = default);
}
