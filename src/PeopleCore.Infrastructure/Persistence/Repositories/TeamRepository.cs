using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Organization.Interfaces;
using PeopleCore.Domain.Entities.Organization;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class TeamRepository : Repository<Team>, ITeamRepository
{
    public TeamRepository(AppDbContext context) : base(context) { }

    public async Task<(IReadOnlyList<Team> Items, int TotalCount)> GetPagedAsync(
        int page, int pageSize, Guid? departmentId, CancellationToken ct = default)
    {
        var query = Context.Teams
            .Include(t => t.Department)
            .Where(t => departmentId == null || t.DepartmentId == departmentId)
            .OrderBy(t => t.Name);

        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }
}
