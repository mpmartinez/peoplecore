using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Entities.Employees;

namespace PeopleCore.Domain.Entities.Performance;

public class PerformanceReview : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public Guid ReviewCycleId { get; set; }
    public ReviewCycle ReviewCycle { get; set; } = null!;
    public Guid ReviewerId { get; set; }
    public Employee Reviewer { get; set; } = null!;
    public decimal? SelfEvaluationScore { get; set; }
    public decimal? ManagerScore { get; set; }
    public decimal? FinalScore { get; set; }
    public string? SelfEvaluationComments { get; set; }
    public string? ManagerComments { get; set; }
    public ReviewStatus Status { get; set; } = ReviewStatus.Draft;
    public DateTime? SubmittedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public ICollection<KpiItem> KpiItems { get; set; } = [];
}
