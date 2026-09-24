using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Leave.DTOs;

namespace PeopleCore.Application.Leave.Interfaces;

public interface ILeaveRequestService
{
    /// <summary>
    /// A page of requests. <paramref name="excludeConfidential"/> leaves confidential types (VAWC)
    /// out of the query itself, so the count and pages match - set for an approval queue whose
    /// caller may not see them.
    /// </summary>
    Task<PagedResult<LeaveRequestDto>> GetAllAsync(
        Guid? employeeId, Guid? reportingManagerId, string? status, int page, int pageSize,
        bool excludeConfidential, CancellationToken ct = default);
    Task<LeaveRequestDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<LeaveRequestDto> CreateAsync(CreateLeaveRequestDto dto, CancellationToken ct = default);
    Task<LeaveRequestDto> ApproveAsync(Guid id, Guid approverId, CancellationToken ct = default);
    Task<LeaveRequestDto> RejectAsync(Guid id, Guid rejecterId, RejectLeaveDto dto, CancellationToken ct = default);
    Task CancelAsync(Guid id, Guid requestingEmployeeId, CancellationToken ct = default);
}
