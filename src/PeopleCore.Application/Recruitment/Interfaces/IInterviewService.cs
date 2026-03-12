using PeopleCore.Application.Recruitment.DTOs;

namespace PeopleCore.Application.Recruitment.Interfaces;

public interface IInterviewService
{
    Task<IReadOnlyList<InterviewStageDto>> GetByApplicantAsync(Guid applicantId, CancellationToken ct = default);
    Task<InterviewStageDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<InterviewStageDto> CreateAsync(CreateInterviewStageDto dto, CancellationToken ct = default);
    Task<InterviewStageDto> UpdateAsync(Guid id, UpdateInterviewStageDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
