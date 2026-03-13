using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Performance.Interfaces;
using PeopleCore.Domain.Entities.Performance;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class ReviewCycleRepository : Repository<ReviewCycle>, IReviewCycleRepository
{
    public ReviewCycleRepository(AppDbContext context) : base(context) { }

    public async Task<(IReadOnlyList<ReviewCycle> Items, int TotalCount)> GetPagedAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        var query = Context.ReviewCycles.AsQueryable();

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(c => c.Year)
            .ThenByDescending(c => c.Quarter)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }
}
