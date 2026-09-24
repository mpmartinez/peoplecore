using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities.Leave;

namespace PeopleCore.Application.Leave.Interfaces;

public interface ILeaveTypeRepository : IRepository<LeaveType>
{
    Task<LeaveType?> GetByCodeAsync(string code, CancellationToken ct = default);

    /// <summary>
    /// Whether the type has any leave request (of any status), balance row or accrual transaction -
    /// once it has, it can't be deleted.
    /// </summary>
    Task<bool> IsUsedAsync(Guid id, CancellationToken ct = default);

    /// <summary>Inserts a new type and its accrual policies in one save: both land, or neither does.</summary>
    Task<LeaveType> AddWithPoliciesAsync(LeaveType type, IReadOnlyList<LeaveAccrualPolicy> policies, CancellationToken ct = default);

    /// <summary>Deletes a type and every accrual policy of it in one save: both go, or neither does.</summary>
    Task DeleteWithPoliciesAsync(LeaveType type, CancellationToken ct = default);
}
