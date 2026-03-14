using PeopleCore.Domain.Entities.Leave;

namespace PeopleCore.Domain.Interfaces;

public interface ILeaveAccrualRepository
{
    // Policies
    Task<IReadOnlyList<LeaveAccrualPolicy>> GetPoliciesByLeaveTypeAsync(Guid leaveTypeId, CancellationToken ct = default);
    Task<IReadOnlyList<LeaveAccrualPolicy>> GetAllActivePoliciesAsync(CancellationToken ct = default);
    Task<LeaveAccrualPolicy?> GetPolicyByIdAsync(Guid id, CancellationToken ct = default);
    Task<LeaveAccrualPolicy> AddPolicyAsync(LeaveAccrualPolicy policy, CancellationToken ct = default);
    Task UpdatePolicyAsync(LeaveAccrualPolicy policy, CancellationToken ct = default);
    Task DeletePolicyAsync(LeaveAccrualPolicy policy, CancellationToken ct = default);

    // Transactions
    Task<bool> TransactionExistsAsync(Guid employeeId, Guid leaveTypeId, int year, int month, CancellationToken ct = default);
    Task AddTransactionAsync(LeaveAccrualTransaction transaction, CancellationToken ct = default);
    Task<IReadOnlyList<LeaveAccrualTransaction>> GetTransactionsByEmployeeAsync(Guid employeeId, CancellationToken ct = default);
}
