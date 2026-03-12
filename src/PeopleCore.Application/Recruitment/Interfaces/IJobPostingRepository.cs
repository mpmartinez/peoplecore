using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities.Recruitment;

namespace PeopleCore.Application.Recruitment.Interfaces;

public interface IJobPostingRepository : IRepository<JobPosting>
{
    Task<(IReadOnlyList<JobPosting> Items, int TotalCount)> GetPagedAsync(
        string? status, int page, int pageSize, CancellationToken ct = default);
}
