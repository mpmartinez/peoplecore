namespace PeopleCore.Domain.Entities.Leave;

public class LeaveType : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public decimal MaxDaysPerYear { get; set; }
    public bool IsPaid { get; set; } = true;
    public bool IsCarryOver { get; set; } = false;
    public decimal? CarryOverMaxDays { get; set; }
    public string? GenderRestriction { get; set; }
    public bool RequiresDocument { get; set; } = false;
    public bool IsActive { get; set; } = true;
}
