using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Organization.DTOs;

namespace PeopleCore.Application.Organization.Interfaces;

public interface ITeamService
{
    Task<PagedResult<TeamDto>> GetAllAsync(int page, int pageSize, Guid? departmentId = null, CancellationToken ct = default);
    Task<TeamDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<TeamDto> CreateAsync(CreateTeamDto dto, CancellationToken ct = default);
    Task<TeamDto> UpdateAsync(Guid id, UpdateTeamDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
