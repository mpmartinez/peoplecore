using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Entities.Employees;

namespace PeopleCore.Domain.Entities.Recruitment;

public class Applicant : AuditableEntity
{
    public Guid JobPostingId { get; set; }
    public JobPosting JobPosting { get; set; } = null!;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? ResumeStorageKey { get; set; }
    public ApplicantStatus Status { get; set; } = ApplicantStatus.Applied;
    public Guid? ConvertedEmployeeId { get; set; }
    public Employee? ConvertedEmployee { get; set; }
    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;
    public ICollection<InterviewStage> InterviewStages { get; set; } = [];
}
