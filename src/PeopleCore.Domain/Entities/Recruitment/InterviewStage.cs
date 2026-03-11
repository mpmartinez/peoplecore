using PeopleCore.Domain.Entities.Employees;

namespace PeopleCore.Domain.Entities.Recruitment;

public class InterviewStage : AuditableEntity
{
    public Guid ApplicantId { get; set; }
    public Applicant Applicant { get; set; } = null!;
    public string StageName { get; set; } = string.Empty;
    public DateTime? ScheduledAt { get; set; }
    public Guid? InterviewerId { get; set; }
    public Employee? Interviewer { get; set; }
    public string? Outcome { get; set; }
    public string? Notes { get; set; }
}
