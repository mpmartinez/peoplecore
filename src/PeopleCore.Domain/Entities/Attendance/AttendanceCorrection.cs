using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Enums;

namespace PeopleCore.Domain.Entities.Attendance;

/// <summary>
/// A change to one employee's time-in or time-out on one day, and the record of it. HR edits and
/// replacing imports are applied at once; an employee's request waits for an approver. Once applied,
/// the row is the day's history: what the times were, what they became, who changed them and why.
/// Times are wall-clock at the site labelled UTC, like <see cref="AttendanceRecord"/>'s.
/// </summary>
public class AttendanceCorrection : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public DateOnly AttendanceDate { get; set; }

    /// <summary>The day's times when the correction was applied (or, while pending, when it was asked for).</summary>
    public DateTime? PreviousTimeIn { get; set; }
    public DateTime? PreviousTimeOut { get; set; }

    public DateTime? NewTimeIn { get; set; }
    public DateTime? NewTimeOut { get; set; }

    public string Reason { get; set; } = string.Empty;
    public AttendanceCorrectionSource Source { get; set; }
    public AttendanceCorrectionStatus Status { get; set; }

    /// <summary>Who made or asked for the change: the account's email.</summary>
    public string RequestedBy { get; set; } = string.Empty;

    /// <summary>Who approved or rejected an employee's request; null for changes applied directly.</summary>
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? RejectionReason { get; set; }
}
