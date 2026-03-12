using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities.Recruitment;

namespace PeopleCore.Application.Recruitment.Interfaces;

public interface IInterviewStageRepository : IRepository<InterviewStage>
{
    Task<IReadOnlyList<InterviewStage>> GetByApplicantAsync(Guid applicantId, CancellationToken ct = default);
}
