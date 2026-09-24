using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Entities.Employees;

namespace PeopleCore.Domain.Entities.Leave;

public class LeaveRequest : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public Guid LeaveTypeId { get; set; }
    public LeaveType LeaveType { get; set; } = null!;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal TotalDays { get; set; }
    public string? Reason { get; set; }
    public LeaveStatus Status { get; set; } = LeaveStatus.Pending;
    public Guid? ApprovedBy { get; set; }
    public Employee? Approver { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? RejectionReason { get; set; }

    /// <summary>Which Expanded Maternity Leave scenario this request is for, on a maternity type.</summary>
    public MaternityCase? MaternityCase { get; set; }

    /// <summary>Of the mother's maternity leave, how many days are allocated to the father.</summary>
    public int DaysAllocatedToFather { get; set; } = 0;

    /// <summary>The part of <see cref="TotalDays"/> charged to <see cref="StartDate"/>'s year; the rest goes to <see cref="EndDate"/>'s year.</summary>
    public decimal DaysInStartYear { get; set; } = 0m;

    public string? DocumentFileName { get; set; }
    public string? DocumentStorageKey { get; set; }
    public string? DocumentContentType { get; set; }
    public long? DocumentSizeBytes { get; set; }
    public Guid? DocumentUploadedBy { get; set; }
    public DateTime? DocumentUploadedAt { get; set; }
}
