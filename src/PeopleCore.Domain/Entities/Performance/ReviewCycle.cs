using PeopleCore.Domain.Enums;

namespace PeopleCore.Domain.Entities.Performance;

public class ReviewCycle : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public int Year { get; set; }
    public int? Quarter { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public ReviewStatus Status { get; set; } = ReviewStatus.Draft;
    public ICollection<PerformanceReview> Reviews { get; set; } = [];
}
