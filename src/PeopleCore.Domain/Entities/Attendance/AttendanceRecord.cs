using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Entities.Employees;

namespace PeopleCore.Domain.Entities.Attendance;

public class AttendanceRecord : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public DateOnly AttendanceDate { get; set; }
    public DateTime? TimeIn { get; set; }
    public DateTime? TimeOut { get; set; }
    public int LateMinutes { get; set; } = 0;
    public int UndertimeMinutes { get; set; } = 0;
    public int OvertimeMinutes { get; set; } = 0;
    public bool IsPresent { get; set; } = false;
    public bool IsHoliday { get; set; } = false;
    public HolidayType? HolidayType { get; set; }
    public string? Remarks { get; set; }
}
