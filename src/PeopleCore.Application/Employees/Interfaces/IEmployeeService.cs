using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Employees.DTOs;

namespace PeopleCore.Application.Employees.Interfaces;

public interface IEmployeeService
{
    Task<PagedResult<EmployeeDto>> GetAllAsync(EmployeeFilterDto filter, CancellationToken ct = default);
    Task<EmployeeDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<EmployeeDto> CreateAsync(CreateEmployeeDto dto, CancellationToken ct = default);
    Task<EmployeeDto> UpdateAsync(Guid id, UpdateEmployeeDto dto, CancellationToken ct = default);
    Task DeactivateAsync(Guid id, DateOnly separationDate, CancellationToken ct = default);
    Task<IReadOnlyList<GovernmentIdDto>> GetGovernmentIdsAsync(Guid employeeId, CancellationToken ct = default);
    Task UpsertGovernmentIdAsync(Guid employeeId, UpsertGovernmentIdDto dto, CancellationToken ct = default);
    Task<IReadOnlyList<EmergencyContactDto>> GetEmergencyContactsAsync(Guid employeeId, CancellationToken ct = default);
    Task<EmergencyContactDto> AddEmergencyContactAsync(Guid employeeId, CreateEmergencyContactDto dto, CancellationToken ct = default);
    Task DeleteEmergencyContactAsync(Guid employeeId, Guid contactId, CancellationToken ct = default);
}
