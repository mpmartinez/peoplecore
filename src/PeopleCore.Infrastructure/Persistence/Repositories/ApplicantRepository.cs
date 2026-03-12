using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Recruitment.Interfaces;
using PeopleCore.Domain.Entities.Recruitment;
using PeopleCore.Domain.Enums;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class ApplicantRepository : Repository<Applicant>, IApplicantRepository
{
    public ApplicantRepository(AppDbContext context) : base(context) { }

    public async Task<(IReadOnlyList<Applicant> Items, int TotalCount)> GetPagedAsync(
        Guid? jobPostingId, string? status, int page, int pageSize, CancellationToken ct = default)
    {
        var query = Context.Applicants
            .Include(a => a.JobPosting)
            .AsQueryable();

        if (jobPostingId.HasValue)
            query = query.Where(a => a.JobPostingId == jobPostingId.Value);

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<ApplicantStatus>(status, true, out var parsed))
            query = query.Where(a => a.Status == parsed);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(a => a.AppliedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }
}
