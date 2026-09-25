using PeopleCore.Domain.Enums;

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

    /// <summary>How this type's entitlement is tracked: Accrued, YearlyAllowance or PerEvent.</summary>
    public LeaveEntitlementKind EntitlementKind { get; set; } = LeaveEntitlementKind.Accrued;

    /// <summary>Whether every date from start to end counts, instead of only scheduled working days.</summary>
    public bool CountsCalendarDays { get; set; } = false;

    /// <summary>Days granted per request/event, for PerEvent types. Also holds a maternity type's live-birth base.</summary>
    public decimal? DaysPerEvent { get; set; }

    /// <summary>Months of service required before this type may be filed.</summary>
    public int? MinServiceMonths { get; set; }

    /// <summary>Whether the employee must be married to file this type.</summary>
    public bool RequiresMarried { get; set; } = false;

    /// <summary>Whether the employee must have a valid solo parent ID on the start date.</summary>
    public bool RequiresSoloParentId { get; set; } = false;

    /// <summary>Most approved requests of this type an employee may have, for PerEvent types with a cap.</summary>
    public int? MaxEvents { get; set; }

    /// <summary>Whether this type is hidden from managers (e.g. VAWC leave).</summary>
    public bool IsConfidential { get; set; } = false;

    /// <summary>Whether this is the Expanded Maternity Leave type.</summary>
    public bool IsMaternity { get; set; } = false;
}
