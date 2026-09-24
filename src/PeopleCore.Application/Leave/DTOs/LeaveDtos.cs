using PeopleCore.Domain.Enums;

namespace PeopleCore.Application.Leave.DTOs;

/// <param name="IsConvertibleToCash">Whether a remaining balance is paid out on final pay.</param>
/// <param name="CountsAsVacationForDeMinimis">Whether converted days count toward the 10-day de minimis ceiling on vacation leave.</param>
/// <param name="EntitlementKind">Accrued (balances built by accrual policies), YearlyAllowance (MaxDaysPerYear a year) or PerEvent (DaysPerEvent a request).</param>
/// <param name="CountsCalendarDays">Every date from start to end counts, not only scheduled working days.</param>
/// <param name="DaysPerEvent">Days per request, for a PerEvent type; a maternity type's live-birth base.</param>
/// <param name="MinServiceMonths">Months of service before the type may be filed.</param>
/// <param name="MaxEvents">Most approved requests an employee may have, for a PerEvent type; null on any other kind.</param>
/// <param name="IsConfidential">Hidden from managers (VAWC).</param>
/// <param name="IsMaternity">The Expanded Maternity Leave type; always PerEvent.</param>
public record LeaveTypeDto(
    Guid Id, string Name, string Code,
    decimal MaxDaysPerYear, bool IsPaid, bool IsCarryOver,
    decimal? CarryOverMaxDays, string? GenderRestriction,
    bool RequiresDocument, bool IsActive,
    bool IsConvertibleToCash, bool CountsAsVacationForDeMinimis,
    LeaveEntitlementKind EntitlementKind, bool CountsCalendarDays,
    decimal? DaysPerEvent, int? MinServiceMonths,
    bool RequiresMarried, bool RequiresSoloParentId,
    int? MaxEvents, bool IsConfidential, bool IsMaternity);

/// <summary>
/// A leave type to create, or the full replacement of one on update. The final-pay and statutory
/// settings default to the entity's own defaults (an active, accrued type with every rule off), so
/// a body that leaves them out is still valid - but an update that leaves them out resets them, as
/// it would any other field.
/// </summary>
/// <remarks>
/// Refused with a DomainException: a PerEvent type without DaysPerEvent &gt; 0 ("Set the days per
/// event."), a YearlyAllowance type without MaxDaysPerYear &gt; 0 ("Set the days per year."), and
/// IsMaternity on any kind but PerEvent ("A maternity type must be per event."). MaxEvents is
/// cleared on any kind but PerEvent, and a blank GenderRestriction is stored as null (any gender).
/// </remarks>
public record CreateLeaveTypeDto(
    string Name, string Code, decimal MaxDaysPerYear,
    bool IsPaid, bool IsCarryOver, decimal? CarryOverMaxDays,
    string? GenderRestriction, bool RequiresDocument,
    bool IsConvertibleToCash = false, bool CountsAsVacationForDeMinimis = true,
    bool IsActive = true, LeaveEntitlementKind EntitlementKind = LeaveEntitlementKind.Accrued,
    bool CountsCalendarDays = false, decimal? DaysPerEvent = null, int? MinServiceMonths = null,
    bool RequiresMarried = false, bool RequiresSoloParentId = false, int? MaxEvents = null,
    bool IsConfidential = false, bool IsMaternity = false);

/// <summary>What "add the statutory set" did: the codes it created and the codes the site already had, in the set's order.</summary>
public record StatutoryLeaveResultDto(IReadOnlyList<string> Added, IReadOnlyList<string> Skipped);

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
