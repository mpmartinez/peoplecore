using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Recruitment.DTOs;
using PeopleCore.Application.Recruitment.Interfaces;
using PeopleCore.Domain.Entities.Recruitment;

namespace PeopleCore.Application.Recruitment.Services;

public class InterviewService : IInterviewService
{
    private readonly IRepository<InterviewStage> _repo;
    private readonly IApplicantRepository _applicantRepo;

    public InterviewService(IRepository<InterviewStage> repo, IApplicantRepository applicantRepo)
    {
        _repo = repo;
        _applicantRepo = applicantRepo;
    }

    public async Task<IReadOnlyList<InterviewStageDto>> GetByApplicantAsync(Guid applicantId, CancellationToken ct = default)
    {
        var all = await _repo.GetAllAsync(ct);
        return all
            .Where(s => s.ApplicantId == applicantId)
            .Select(ToDto)
            .ToList();
    }

    public async Task<InterviewStageDto> CreateAsync(CreateInterviewStageDto dto, CancellationToken ct = default)
    {
        var applicant = await _applicantRepo.GetByIdAsync(dto.ApplicantId, ct)
            ?? throw new KeyNotFoundException($"Applicant {dto.ApplicantId} not found.");

        var stage = new InterviewStage
        {
            ApplicantId = dto.ApplicantId,
            Applicant = applicant,
            StageName = dto.StageName,
            ScheduledAt = dto.ScheduledAt,
            InterviewerId = dto.InterviewerId
        };

        var created = await _repo.AddAsync(stage, ct);
        return ToDto(created);
    }

    public async Task<InterviewStageDto> UpdateAsync(Guid id, UpdateInterviewStageDto dto, CancellationToken ct = default)
    {
        var stage = await _repo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Interview stage {id} not found.");

        stage.ScheduledAt = dto.ScheduledAt;
        stage.InterviewerId = dto.InterviewerId;
        stage.Outcome = dto.Outcome;
        stage.Notes = dto.Notes;
        stage.UpdatedAt = DateTime.UtcNow;

        await _repo.UpdateAsync(stage, ct);
        return ToDto(stage);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var stage = await _repo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Interview stage {id} not found.");
        await _repo.DeleteAsync(stage, ct);
    }

    private static InterviewStageDto ToDto(InterviewStage s) => new(
        s.Id,
        s.ApplicantId,
        s.Applicant != null ? $"{s.Applicant.FirstName} {s.Applicant.LastName}" : string.Empty,
        s.StageName,
        s.ScheduledAt,
        s.InterviewerId,
        s.Interviewer?.FullName,
        s.Outcome,
        s.Notes);
}
