namespace PeopleCore.Domain.Entities.Performance;

public class KpiItem : AuditableEntity
{
    public Guid PerformanceReviewId { get; set; }
    public PerformanceReview PerformanceReview { get; set; } = null!;
    public string Description { get; set; } = string.Empty;
    public string? Target { get; set; }
    public string? Actual { get; set; }
    public decimal Weight { get; set; } = 0;
    public decimal? Score { get; set; }
}
