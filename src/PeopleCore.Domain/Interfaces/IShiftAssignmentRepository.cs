using PeopleCore.Domain.Entities.Scheduling;

namespace PeopleCore.Domain.Interfaces;

public interface IShiftAssignmentRepository
{
    Task<EmployeeShiftAssignment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<EmployeeShiftAssignment?> GetActiveAssignmentAsync(Guid employeeId, DateOnly date, CancellationToken ct = default);
    Task<IReadOnlyList<EmployeeShiftAssignment>> GetByEmployeeAsync(Guid employeeId, CancellationToken ct = default);
    /// <summary>
    /// Every assignment overlapping the period for the given employees, with the navigation
    /// properties the resolver needs. One query, so callers can resolve day-by-day in memory
    /// instead of issuing a query per employee per day.
    /// </summary>
    Task<IReadOnlyList<EmployeeShiftAssignment>> GetActiveForPeriodAsync(
        IReadOnlyList<Guid> employeeIds, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<EmployeeShiftAssignment> AddAsync(EmployeeShiftAssignment entity, CancellationToken ct = default);
    Task DeleteAsync(EmployeeShiftAssignment entity, CancellationToken ct = default);
}
