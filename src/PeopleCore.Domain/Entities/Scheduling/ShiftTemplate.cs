using PeopleCore.Domain.Enums;

namespace PeopleCore.Domain.Entities.Scheduling;

public class ShiftTemplate : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int BreakMinutes { get; set; }
    public bool IsNightShift { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// The days of the week this template schedules as working days. Defaults to
    /// <see cref="WorkDays.MondayToFriday"/> so a template created without specifying one behaves
    /// exactly as the previous hardcoded Mon-Fri fallback.
    /// </summary>
    public WorkDays WorkDays { get; set; } = WorkDays.MondayToFriday;
}
