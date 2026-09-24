using PeopleCore.Domain.Enums;

namespace PeopleCore.Application.Leave.DTOs;

/// <param name="IsConvertibleToCash">Whether a remaining balance is paid out on final pay.</param>
/// <param name="CountsAsVacationForDeMinimis">Whether converted days count toward the 10-day de minimis ceiling on vacation leave.</param>
public record LeaveTypeDto(
    Guid Id, string Name, string Code,
    decimal MaxDaysPerYear, bool IsPaid, bool IsCarryOver,
    decimal? CarryOverMaxDays, string? GenderRestriction,
    bool RequiresDocument, bool IsActive,
    bool IsConvertibleToCash, bool CountsAsVacationForDeMinimis);

/// <summary>
/// A leave type to create, or the full replacement of one on update. The final-pay settings
/// default to the entity's own defaults, so a body that leaves them out is still valid - but an
/// update that leaves them out resets them, as it would any other field.
/// </summary>
public record CreateLeaveTypeDto(
    string Name, string Code, decimal MaxDaysPerYear,
    bool IsPaid, bool IsCarryOver, decimal? CarryOverMaxDays,
    string? GenderRestriction, bool RequiresDocument,
    bool IsConvertibleToCash = false, bool CountsAsVacationForDeMinimis = true);

/// <param name="IsConfidential">The type is confidential (VAWC): the row is shown only to the employee and to <c>approvals.all</c>.</param>
public record LeaveBalanceDto(
    Guid Id, Guid EmployeeId, string EmployeeName,
    Guid LeaveTypeId, string LeaveTypeName,
    int Year, decimal TotalDays, decimal UsedDays,
    decimal CarriedOverDays, decimal RemainingDays,
    bool IsConfidential);

/// <param name="IsConfidential">
/// The type is confidential (VAWC). Anyone but the employee and <c>approvals.all</c> gets the
/// request masked - type "Leave", no reason, no document - with this cleared to false.
/// </param>
public record LeaveRequestDto(
    Guid Id, Guid EmployeeId, string EmployeeName,
    Guid LeaveTypeId, string LeaveTypeName,
    DateOnly StartDate, DateOnly EndDate,
    decimal TotalDays, string? Reason,
    LeaveStatus Status, Guid? ApprovedBy, DateTime? ApprovedAt,
    string? RejectionReason, DateTime CreatedAt,
    MaternityCase? MaternityCase, int DaysAllocatedToFather,
    bool HasDocument, string? DocumentFileName,
    bool IsConfidential);

/// <param name="MaternityCase">Required on a maternity type; cleared on any other type.</param>
/// <param name="DaysAllocatedToFather">Maternity days given to the father (live birth only); cleared on any other type.</param>
public record CreateLeaveRequestDto(
    Guid EmployeeId, Guid LeaveTypeId,
    DateOnly StartDate, DateOnly EndDate, string? Reason,
    MaternityCase? MaternityCase = null, int DaysAllocatedToFather = 0);

public record RejectLeaveDto(string RejectionReason);
