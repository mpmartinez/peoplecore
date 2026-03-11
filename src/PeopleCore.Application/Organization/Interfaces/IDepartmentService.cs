using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Organization.DTOs;

namespace PeopleCore.Application.Organization.Interfaces;

public interface IDepartmentService
{
    Task<PagedResult<DepartmentDto>> GetAllAsync(int page, int pageSize, CancellationToken ct = default);
    Task<DepartmentDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<DepartmentDto> CreateAsync(CreateDepartmentDto dto, CancellationToken ct = default);
    Task<DepartmentDto> UpdateAsync(Guid id, UpdateDepartmentDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
