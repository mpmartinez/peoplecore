using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Organization.DTOs;
using PeopleCore.Application.Organization.Interfaces;
using PeopleCore.Domain.Entities.Organization;

namespace PeopleCore.Application.Organization.Services;

public class TeamService : ITeamService
{
    private readonly ITeamRepository _repo;

    public TeamService(ITeamRepository repo) => _repo = repo;

    public async Task<PagedResult<TeamDto>> GetAllAsync(int page, int pageSize, Guid? departmentId = null, CancellationToken ct = default)
    {
        var (items, total) = await _repo.GetPagedAsync(page, pageSize, departmentId, ct);
        return PagedResult<TeamDto>.Create(items.Select(ToDto).ToList(), total, page, pageSize);
    }

    public async Task<TeamDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var team = await _repo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Team {id} not found.");
        return ToDto(team);
    }

    public async Task<TeamDto> CreateAsync(CreateTeamDto dto, CancellationToken ct = default)
    {
        var team = new Team
        {
            DepartmentId = dto.DepartmentId,
            Name = dto.Name
        };
        var created = await _repo.AddAsync(team, ct);
        return ToDto(created);
    }

    public async Task<TeamDto> UpdateAsync(Guid id, UpdateTeamDto dto, CancellationToken ct = default)
    {
        var team = await _repo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Team {id} not found.");
        team.Name = dto.Name;
        team.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(team, ct);
        return ToDto(team);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var team = await _repo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Team {id} not found.");
        await _repo.DeleteAsync(team, ct);
    }

    private static TeamDto ToDto(Team t) => new(
        t.Id, t.DepartmentId, t.Department?.Name ?? string.Empty, t.Name);
}
