using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Entities.Employees;

namespace PeopleCore.Domain.Entities.Attendance;

public class OvertimeRequest : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public DateOnly OvertimeDate { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int TotalMinutes { get; set; }
    public string Reason { get; set; } = string.Empty;
    public OvertimeStatus Status { get; set; } = OvertimeStatus.Pending;
    public Guid? ApprovedBy { get; set; }
    public Employee? Approver { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? RejectionReason { get; set; }
}
