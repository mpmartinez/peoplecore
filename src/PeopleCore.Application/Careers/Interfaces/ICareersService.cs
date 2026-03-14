using PeopleCore.Application.Careers.DTOs;

namespace PeopleCore.Application.Careers.Interfaces;

public interface ICareersService
{
    Task<IReadOnlyList<PublicJobPostingDto>> GetOpenJobsAsync(CancellationToken ct = default);
    Task<PublicJobPostingDto?> GetJobAsync(Guid id, CancellationToken ct = default);
    Task<JobApplicationResponse> ApplyAsync(Guid jobPostingId, JobApplicationRequest request, CancellationToken ct = default);
}
