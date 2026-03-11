using PeopleCore.Domain.Enums;

namespace PeopleCore.Application.Leave.DTOs;

public record LeaveTypeDto(
    Guid Id, string Name, string Code,
    decimal MaxDaysPerYear, bool IsPaid, bool IsCarryOver,
    decimal? CarryOverMaxDays, string? GenderRestriction,
    bool RequiresDocument, bool IsActive);

public record CreateLeaveTypeDto(
    string Name, string Code, decimal MaxDaysPerYear,
    bool IsPaid, bool IsCarryOver, decimal? CarryOverMaxDays,
    string? GenderRestriction, bool RequiresDocument);

public record LeaveBalanceDto(
    Guid Id, Guid EmployeeId, string EmployeeName,
    Guid LeaveTypeId, string LeaveTypeName,
    int Year, decimal TotalDays, decimal UsedDays,
    decimal CarriedOverDays, decimal RemainingDays);

public record LeaveRequestDto(
    Guid Id, Guid EmployeeId, string EmployeeName,
    Guid LeaveTypeId, string LeaveTypeName,
    DateOnly StartDate, DateOnly EndDate,
    decimal TotalDays, string? Reason,
    LeaveStatus Status, Guid? ApprovedBy, DateTime? ApprovedAt,
    string? RejectionReason, DateTime CreatedAt);

public record CreateLeaveRequestDto(
    Guid EmployeeId, Guid LeaveTypeId,
    DateOnly StartDate, DateOnly EndDate, string? Reason);

public record ApproveLeaveDto(Guid ApproverId);
public record RejectLeaveDto(string RejectionReason);
