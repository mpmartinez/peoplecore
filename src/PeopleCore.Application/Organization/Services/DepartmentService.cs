using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Organization.DTOs;
using PeopleCore.Application.Organization.Interfaces;
using PeopleCore.Domain.Entities.Organization;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.Application.Organization.Services;

public class DepartmentService : IDepartmentService
{
    private readonly IDepartmentRepository _repo;

    public DepartmentService(IDepartmentRepository repo) => _repo = repo;

    public async Task<PagedResult<DepartmentDto>> GetAllAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var (items, total) = await _repo.GetPagedAsync(page, pageSize, ct);
        return PagedResult<DepartmentDto>.Create(items.Select(ToDto).ToList(), total, page, pageSize);
    }

    public async Task<DepartmentDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var dept = await _repo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Department {id} not found.");
        return ToDto(dept);
    }

    public async Task<DepartmentDto> CreateAsync(CreateDepartmentDto dto, CancellationToken ct = default)
    {
        var dept = new Department
        {
            CompanyId = dto.CompanyId,
            ParentDepartmentId = dto.ParentDepartmentId,
            Name = dto.Name,
            Code = dto.Code
        };
        var created = await _repo.AddAsync(dept, ct);
        return ToDto(created);
    }

    public async Task<DepartmentDto> UpdateAsync(Guid id, UpdateDepartmentDto dto, CancellationToken ct = default)
    {
        var dept = await _repo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Department {id} not found.");
        dept.ParentDepartmentId = dto.ParentDepartmentId;
        dept.Name = dto.Name;
        dept.Code = dto.Code;
        dept.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(dept, ct);
        return ToDto(dept);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var dept = await _repo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Department {id} not found.");
        if (dept.SubDepartments.Count > 0)
            throw new DomainException("Cannot delete a department that has sub-departments.");
        await _repo.DeleteAsync(dept, ct);
    }

    private static DepartmentDto ToDto(Department d) => new(
        d.Id, d.CompanyId, d.ParentDepartmentId,
        d.ParentDepartment?.Name, d.Name, d.Code,
        d.SubDepartments.Count);
}
