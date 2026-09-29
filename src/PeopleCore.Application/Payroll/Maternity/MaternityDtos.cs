using PeopleCore.Domain.Enums;

namespace PeopleCore.Application.Payroll.Maternity;

/// <summary>A maternity claim as HR sees it: the leave it covers, the benefit, and where it stands with SSS.</summary>
public record MaternityClaimDto(Guid Id, Guid LeaveRequestId, Guid EmployeeId, string EmployeeName,
    DateOnly LeaveStart, DateOnly LeaveEnd, decimal Days, decimal? DailyAllowance, decimal Benefit,
    MaternityClaimStatus Status, Guid? AdvanceRunId, string? AdvanceRunNumber, DateOnly? AdvancedAt,
    DateOnly? ReimbursedOn, decimal? ReimbursedAmount, string? Note);

/// <summary>
/// The SSS daily maternity allowance worked out from paid payroll: the 6 highest monthly salary
/// credits in the window (each capped at the Regular SS ceiling), over 180. Null when no month in
/// the window has one, or when <paramref name="RatesOverridden"/>: the payroll settings override
/// both SSS rates, so the MSC can't be worked back from the share.
/// </summary>
public record SuggestedAllowanceDto(decimal? DailyAllowance, int MonthsFound, DateOnly WindowFrom, DateOnly WindowTo,
    bool RatesOverridden = false);

public record SetAllowanceRequest(decimal DailyAllowance);

public record ReimburseRequest(DateOnly ReimbursedOn, decimal ReimbursedAmount, string? Note);

/// <summary>
/// The note is nullable so a missing one reaches the service, which explains what is needed; a
/// non-nullable one would be [Required] to MVC, answered with a validation error that has no detail.
/// </summary>
public record DenyRequest(string? Note);

/// <summary>Every claim, newest first, and the benefit advanced but not yet reimbursed or denied.</summary>
public record MaternityClaimsSummaryDto(IReadOnlyList<MaternityClaimDto> Claims, decimal Outstanding);

/// <summary>An approved maternity leave request with no claim yet, which HR can open one for.</summary>
public record EligibleMaternityLeaveDto(Guid LeaveRequestId, Guid EmployeeId, string EmployeeName,
    DateOnly StartDate, DateOnly EndDate, decimal Days);
