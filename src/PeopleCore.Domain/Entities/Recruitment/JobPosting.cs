using PeopleCore.Domain.Entities.Organization;

namespace PeopleCore.Domain.Entities.Recruitment;

public class JobPosting : AuditableEntity
{
    public string Title { get; set; } = string.Empty;
    public Guid? DepartmentId { get; set; }
    public Department? Department { get; set; }
    public Guid? PositionId { get; set; }
    public Position? Position { get; set; }
    public string? Description { get; set; }
    public string? Requirements { get; set; }
    public int Vacancies { get; set; } = 1;
    public string Status { get; set; } = "Draft";
    public DateTime? PostedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public ICollection<Applicant> Applicants { get; set; } = [];
}
