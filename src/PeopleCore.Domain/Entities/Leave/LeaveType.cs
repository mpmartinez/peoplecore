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

    /// <summary>Whether a remaining balance of this type is paid out in cash on final pay.</summary>
    public bool IsConvertibleToCash { get; set; } = false;

    /// <summary>
    /// Whether converted days of this type count toward the combined 10-day de minimis cap on
    /// leave conversion. Vacation-type leaves typically do; others typically don't.
    /// </summary>
    public bool CountsAsVacationForDeMinimis { get; set; } = true;
}
