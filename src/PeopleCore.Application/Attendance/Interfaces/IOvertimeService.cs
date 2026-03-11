using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Common.DTOs;

namespace PeopleCore.Application.Attendance.Interfaces;

public interface IOvertimeService
{
    Task<PagedResult<OvertimeRequestDto>> GetAllAsync(Guid? employeeId, string? status, int page, int pageSize, CancellationToken ct = default);
    Task<OvertimeRequestDto> CreateAsync(CreateOvertimeRequestDto dto, CancellationToken ct = default);
    Task<OvertimeRequestDto> ApproveAsync(Guid id, ApproveOvertimeDto dto, CancellationToken ct = default);
    Task<OvertimeRequestDto> RejectAsync(Guid id, RejectOvertimeDto dto, CancellationToken ct = default);
}
