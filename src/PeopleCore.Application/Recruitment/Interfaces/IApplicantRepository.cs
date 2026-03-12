using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities.Recruitment;

namespace PeopleCore.Application.Recruitment.Interfaces;

public interface IApplicantRepository : IRepository<Applicant>
{
    Task<(IReadOnlyList<Applicant> Items, int TotalCount)> GetPagedAsync(
        Guid? jobPostingId, string? status, int page, int pageSize, CancellationToken ct = default);
}
