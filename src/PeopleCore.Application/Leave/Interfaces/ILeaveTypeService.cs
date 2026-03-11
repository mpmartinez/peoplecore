using PeopleCore.Application.Leave.DTOs;

namespace PeopleCore.Application.Leave.Interfaces;

public interface ILeaveTypeService
{
    Task<IReadOnlyList<LeaveTypeDto>> GetAllAsync(CancellationToken ct = default);
    Task<LeaveTypeDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<LeaveTypeDto> CreateAsync(CreateLeaveTypeDto dto, CancellationToken ct = default);
    Task<LeaveTypeDto> UpdateAsync(Guid id, CreateLeaveTypeDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
