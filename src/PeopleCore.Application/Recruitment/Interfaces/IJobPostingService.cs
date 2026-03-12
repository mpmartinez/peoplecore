using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Recruitment.DTOs;

namespace PeopleCore.Application.Recruitment.Interfaces;

public interface IJobPostingService
{
    Task<PagedResult<JobPostingDto>> GetAllAsync(string? status, int page, int pageSize, CancellationToken ct = default);
    Task<JobPostingDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<JobPostingDto> CreateAsync(CreateJobPostingDto dto, CancellationToken ct = default);
    Task<JobPostingDto> UpdateAsync(Guid id, UpdateJobPostingDto dto, CancellationToken ct = default);
    Task<JobPostingDto> PublishAsync(Guid id, CancellationToken ct = default);
    Task<JobPostingDto> CloseAsync(Guid id, CancellationToken ct = default);
}
