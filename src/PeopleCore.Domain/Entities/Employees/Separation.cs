using PeopleCore.Domain.Enums;

namespace PeopleCore.Domain.Entities.Employees;

/// <summary>
/// One employee's departure: how and when they left, and the clearance that has to be complete
/// before their final pay is released.
/// </summary>
public class Separation : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;

    public SeparationType Type { get; set; }
    public AuthorizedCause? AuthorizedCause { get; set; }
    public DateOnly NoticeDate { get; set; }
    public DateOnly LastWorkingDay { get; set; }
    public string? Reason { get; set; }

    public SeparationStatus Status { get; set; }
    public string RecordedBy { get; set; } = "";
    public string? SeparatedBy { get; set; }
    public DateTime? SeparatedAt { get; set; }

    public List<SeparationClearanceItem> ClearanceItems { get; set; } = [];

    /// <summary>The final-pay run created for this separation, once one exists. At most one per separation.</summary>
    public Guid? FinalPayRunId { get; set; }
    public Payroll.PayrollRun? FinalPayRun { get; set; }

    /// <summary>DOLE Labor Advisory 06-2020: final pay within 30 days of separation.</summary>
    public DateOnly FinalPayDueBy => LastWorkingDay.AddDays(30);

    public bool ClearanceComplete => ClearanceItems.Count > 0 && ClearanceItems.All(i => i.ClearedAt is not null);
}

public class SeparationClearanceItem : AuditableEntity
{
    public Guid SeparationId { get; set; }
    public Separation Separation { get; set; } = null!;
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public string? ClearedBy { get; set; }
    public DateTime? ClearedAt { get; set; }
    public string? Note { get; set; }

    /// <summary>Who last undid a clearance on this item, and the note that was cleared when they did.</summary>
    public string? LastUndoneBy { get; set; }
    public DateTime? LastUndoneAt { get; set; }
    public string? LastUndoneNote { get; set; }
}
