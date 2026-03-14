namespace PeopleCore.Domain.Entities.Scheduling;

public class ShiftTemplate : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int BreakMinutes { get; set; }
    public bool IsNightShift { get; set; }
    public bool IsActive { get; set; } = true;
}
