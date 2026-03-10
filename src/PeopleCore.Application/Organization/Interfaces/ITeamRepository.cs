using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities.Organization;

namespace PeopleCore.Application.Organization.Interfaces;

public interface ITeamRepository : IRepository<Team>
{
    Task<(IReadOnlyList<Team> Items, int TotalCount)> GetPagedAsync(int page, int pageSize, Guid? departmentId, CancellationToken ct = default);
}
