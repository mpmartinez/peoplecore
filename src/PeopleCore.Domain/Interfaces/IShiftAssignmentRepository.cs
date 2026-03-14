using PeopleCore.Domain.Entities.Scheduling;

namespace PeopleCore.Domain.Interfaces;

public interface IShiftAssignmentRepository
{
    Task<EmployeeShiftAssignment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<EmployeeShiftAssignment?> GetActiveAssignmentAsync(Guid employeeId, DateOnly date, CancellationToken ct = default);
    Task<IReadOnlyList<EmployeeShiftAssignment>> GetByEmployeeAsync(Guid employeeId, CancellationToken ct = default);
    Task<EmployeeShiftAssignment> AddAsync(EmployeeShiftAssignment entity, CancellationToken ct = default);
    Task DeleteAsync(EmployeeShiftAssignment entity, CancellationToken ct = default);
}
