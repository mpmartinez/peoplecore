using PeopleCore.Domain.Entities.Employees;

namespace PeopleCore.Domain.Entities.Scheduling;

public class EmployeeShiftAssignment : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public Guid? ShiftTemplateId { get; set; }
    public ShiftTemplate? ShiftTemplate { get; set; }
    public Guid? RotatingPatternId { get; set; }
    public RotatingPattern? RotatingPattern { get; set; }
    public DateOnly? PatternStartDate { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }

    /// <summary>Exactly one of ShiftTemplateId or RotatingPatternId must be set.</summary>
    public bool IsValid => ShiftTemplateId.HasValue != RotatingPatternId.HasValue;
}
