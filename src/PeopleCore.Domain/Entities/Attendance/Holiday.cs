using PeopleCore.Domain.Enums;

namespace PeopleCore.Domain.Entities.Attendance;

public class Holiday : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public DateOnly HolidayDate { get; set; }
    public HolidayType HolidayType { get; set; }
    public bool IsRecurring { get; set; } = false;
}
