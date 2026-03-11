using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Organization.DTOs;
using PeopleCore.Application.Organization.Interfaces;
using PeopleCore.Domain.Entities.Organization;

namespace PeopleCore.Application.Organization.Services;

public class PositionService : IPositionService
{
    private readonly IPositionRepository _repo;

    public PositionService(IPositionRepository repo) => _repo = repo;

    public async Task<PagedResult<PositionDto>> GetAllAsync(int page, int pageSize, Guid? departmentId = null, CancellationToken ct = default)
    {
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page), "Page must be >= 1.");
        if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize), "PageSize must be >= 1.");
        var (items, total) = await _repo.GetPagedAsync(page, pageSize, departmentId, ct);
        return PagedResult<PositionDto>.Create(items.Select(ToDto).ToList(), total, page, pageSize);
    }

    public async Task<PositionDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var pos = await _repo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Position {id} not found.");
        return ToDto(pos);
    }

    public async Task<PositionDto> CreateAsync(CreatePositionDto dto, CancellationToken ct = default)
    {
        var pos = new Position
        {
            DepartmentId = dto.DepartmentId,
            Title = dto.Title,
            Level = dto.Level
        };
        var created = await _repo.AddAsync(pos, ct);
        return ToDto(created);
    }

    public async Task<PositionDto> UpdateAsync(Guid id, UpdatePositionDto dto, CancellationToken ct = default)
    {
        var pos = await _repo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Position {id} not found.");
        pos.Title = dto.Title;
        pos.Level = dto.Level;
        pos.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(pos, ct);
        return ToDto(pos);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var pos = await _repo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Position {id} not found.");
        // TODO Phase 3: Add guard to prevent deleting positions assigned to active employees
        await _repo.DeleteAsync(pos, ct);
    }

    private static PositionDto ToDto(Position p) => new(
        p.Id, p.DepartmentId, p.Department?.Name ?? string.Empty, p.Title, p.Level);
}
