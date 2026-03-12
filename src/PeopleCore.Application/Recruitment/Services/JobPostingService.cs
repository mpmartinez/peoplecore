using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Recruitment.DTOs;
using PeopleCore.Application.Recruitment.Interfaces;
using PeopleCore.Domain.Entities.Recruitment;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.Application.Recruitment.Services;

public class JobPostingService : IJobPostingService
{
    private readonly IJobPostingRepository _repo;

    public JobPostingService(IJobPostingRepository repo) => _repo = repo;

    public async Task<PagedResult<JobPostingDto>> GetAllAsync(
        string? status, int page, int pageSize, CancellationToken ct = default)
    {
        var (items, total) = await _repo.GetPagedAsync(status, page, pageSize, ct);
        return PagedResult<JobPostingDto>.Create(items.Select(ToDto).ToList(), total, page, pageSize);
    }

    public async Task<JobPostingDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var posting = await _repo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Job posting {id} not found.");
        return ToDto(posting);
    }

    public async Task<JobPostingDto> CreateAsync(CreateJobPostingDto dto, CancellationToken ct = default)
    {
        var posting = new JobPosting
        {
            Title = dto.Title,
            DepartmentId = dto.DepartmentId,
            PositionId = dto.PositionId,
            Description = dto.Description,
            Requirements = dto.Requirements,
            Vacancies = dto.Vacancies,
            Status = JobPostingStatus.Draft
        };

        var created = await _repo.AddAsync(posting, ct);
        return ToDto(created);
    }

    public async Task<JobPostingDto> UpdateAsync(Guid id, UpdateJobPostingDto dto, CancellationToken ct = default)
    {
        var posting = await _repo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Job posting {id} not found.");

        posting.Title = dto.Title;
        posting.DepartmentId = dto.DepartmentId;
        posting.PositionId = dto.PositionId;
        posting.Description = dto.Description;
        posting.Requirements = dto.Requirements;
        posting.Vacancies = dto.Vacancies;
        posting.UpdatedAt = DateTime.UtcNow;

        await _repo.UpdateAsync(posting, ct);
        return ToDto(posting);
    }

    public async Task<JobPostingDto> PublishAsync(Guid id, CancellationToken ct = default)
    {
        var posting = await _repo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Job posting {id} not found.");

        if (posting.Status != JobPostingStatus.Draft)
            throw new DomainException("Only Draft job postings can be published.");

        posting.Status = JobPostingStatus.Open;
        posting.PostedAt = DateTime.UtcNow;
        posting.UpdatedAt = DateTime.UtcNow;

        await _repo.UpdateAsync(posting, ct);
        return ToDto(posting);
    }

    public async Task<JobPostingDto> CloseAsync(Guid id, CancellationToken ct = default)
    {
        var posting = await _repo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Job posting {id} not found.");

        if (posting.Status != JobPostingStatus.Open)
            throw new DomainException("Only Open job postings can be closed.");

        posting.Status = JobPostingStatus.Closed;
        posting.ClosedAt = DateTime.UtcNow;
        posting.UpdatedAt = DateTime.UtcNow;

        await _repo.UpdateAsync(posting, ct);
        return ToDto(posting);
    }

    private static JobPostingDto ToDto(JobPosting p) => new(
        p.Id, p.Title,
        p.DepartmentId, p.Department?.Name,
        p.PositionId, p.Position?.Title,
        p.Description, p.Requirements, p.Vacancies,
        p.Status, p.PostedAt, p.ClosedAt);
}
