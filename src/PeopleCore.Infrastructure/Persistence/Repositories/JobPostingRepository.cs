using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Recruitment.Interfaces;
using PeopleCore.Domain.Entities.Recruitment;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class JobPostingRepository : Repository<JobPosting>, IJobPostingRepository
{
    public JobPostingRepository(AppDbContext context) : base(context) { }

    public override async Task<JobPosting?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await Context.JobPostings
            .Include(j => j.Department)
            .Include(j => j.Position)
            .FirstOrDefaultAsync(j => j.Id == id, ct);

    public async Task<(IReadOnlyList<JobPosting> Items, int TotalCount)> GetPagedAsync(
        string? status, int page, int pageSize, CancellationToken ct = default)
    {
        var query = Context.JobPostings
            .Include(j => j.Department)
            .Include(j => j.Position)
            .AsQueryable();

        if (!string.IsNullOrEmpty(status))
            query = query.Where(j => j.Status == status);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(j => j.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }
}
