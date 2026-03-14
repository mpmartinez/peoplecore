namespace PeopleCore.Domain.Entities.Scheduling;

public class RotatingPatternSlot : AuditableEntity
{
    public Guid RotatingPatternId { get; set; }
    public RotatingPattern RotatingPattern { get; set; } = null!;
    public int DayOffset { get; set; }
    public Guid? ShiftTemplateId { get; set; }
    public ShiftTemplate? ShiftTemplate { get; set; }
}
