namespace PeopleCore.Domain.Entities.Scheduling;

public class RotatingPattern : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public int CycleLengthDays { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<RotatingPatternSlot> Slots { get; set; } = [];
}
