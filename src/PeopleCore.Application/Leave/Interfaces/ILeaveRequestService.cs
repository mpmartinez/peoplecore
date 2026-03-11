using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Leave.DTOs;

namespace PeopleCore.Application.Leave.Interfaces;

public interface ILeaveRequestService
{
    Task<PagedResult<LeaveRequestDto>> GetAllAsync(Guid? employeeId, string? status, int page, int pageSize, CancellationToken ct = default);
    Task<LeaveRequestDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<LeaveRequestDto> CreateAsync(CreateLeaveRequestDto dto, CancellationToken ct = default);
    Task<LeaveRequestDto> ApproveAsync(Guid id, ApproveLeaveDto dto, CancellationToken ct = default);
    Task<LeaveRequestDto> RejectAsync(Guid id, RejectLeaveDto dto, CancellationToken ct = default);
    Task CancelAsync(Guid id, Guid requestingEmployeeId, CancellationToken ct = default);
}
