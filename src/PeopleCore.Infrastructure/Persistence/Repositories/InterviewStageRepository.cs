using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Recruitment.Interfaces;
using PeopleCore.Domain.Entities.Recruitment;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class InterviewStageRepository : Repository<InterviewStage>, IInterviewStageRepository
{
    public InterviewStageRepository(AppDbContext context) : base(context) { }

    public async Task<IReadOnlyList<InterviewStage>> GetByApplicantAsync(Guid applicantId, CancellationToken ct = default)
        => await Context.InterviewStages
            .Include(s => s.Applicant)
            .Include(s => s.Interviewer)
            .Where(s => s.ApplicantId == applicantId)
            .OrderBy(s => s.ScheduledAt)
            .ToListAsync(ct);
}
