using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Organization.Interfaces;
using PeopleCore.Domain.Entities.Organization;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class PositionRepository : Repository<Position>, IPositionRepository
{
    public PositionRepository(AppDbContext context) : base(context) { }

    public async Task<(IReadOnlyList<Position> Items, int TotalCount)> GetPagedAsync(
        int page, int pageSize, Guid? departmentId, CancellationToken ct = default)
    {
        var query = Context.Positions
            .Include(p => p.Department)
            .Where(p => departmentId == null || p.DepartmentId == departmentId)
            .OrderBy(p => p.Title);

        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }
}
