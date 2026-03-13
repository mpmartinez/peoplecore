using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Performance.Interfaces;
using PeopleCore.Domain.Entities.Performance;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class PerformanceReviewRepository : Repository<PerformanceReview>, IPerformanceReviewRepository
{
    public PerformanceReviewRepository(AppDbContext context) : base(context) { }

    public override async Task<PerformanceReview?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await Context.PerformanceReviews
            .Include(r => r.Employee)
            .Include(r => r.ReviewCycle)
            .Include(r => r.Reviewer)
            .Include(r => r.KpiItems)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<(IReadOnlyList<PerformanceReview> Items, int TotalCount)> GetPagedAsync(
        Guid? employeeId, Guid? cycleId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = Context.PerformanceReviews
            .Include(r => r.Employee)
            .Include(r => r.ReviewCycle)
            .Include(r => r.Reviewer)
            .Include(r => r.KpiItems)
            .AsQueryable();

        if (employeeId.HasValue)
            query = query.Where(r => r.EmployeeId == employeeId);

        if (cycleId.HasValue)
            query = query.Where(r => r.ReviewCycleId == cycleId);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }
}
