using PeopleCore.Application.Leave.DTOs;

namespace PeopleCore.Application.Leave.Interfaces;

public interface ILeaveTypeService
{
    Task<IReadOnlyList<LeaveTypeDto>> GetAllAsync(CancellationToken ct = default);
    Task<LeaveTypeDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<LeaveTypeDto> CreateAsync(CreateLeaveTypeDto dto, CancellationToken ct = default);
    Task<LeaveTypeDto> UpdateAsync(Guid id, CreateLeaveTypeDto dto, CancellationToken ct = default);

    /// <summary>Deletes a type that has never been used, with its accrual policies; a used one is refused ("{Type} has been used; deactivate it instead.").</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Creates the Philippine statutory leave types the site lacks (matched by code, trimmed and
    /// ignoring case), and SIL's accrual policy when SIL is created. Existing types are left as they are.
    /// </summary>
    Task<StatutoryLeaveResultDto> AddStatutoryAsync(CancellationToken ct = default);
}
