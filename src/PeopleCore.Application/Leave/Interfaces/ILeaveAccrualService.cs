using PeopleCore.Application.Leave.DTOs;

namespace PeopleCore.Application.Leave.Interfaces;

public interface ILeaveAccrualService
{
    Task<IReadOnlyList<LeaveAccrualPolicyDto>> GetPoliciesAsync(Guid leaveTypeId, CancellationToken ct = default);
    Task<LeaveAccrualPolicyDto> CreatePolicyAsync(CreateLeaveAccrualPolicyRequest request, CancellationToken ct = default);
    Task UpdatePolicyAsync(Guid id, CreateLeaveAccrualPolicyRequest request, CancellationToken ct = default);
    Task DeletePolicyAsync(Guid id, CancellationToken ct = default);
    Task RunAccrualsAsync(int year, int month, CancellationToken ct = default);
    Task<IReadOnlyList<LeaveAccrualTransactionDto>> GetEmployeeAccrualHistoryAsync(Guid employeeId, CancellationToken ct = default);
}
