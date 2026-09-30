using PeopleCore.Domain.Enums;

namespace PeopleCore.Application.Payroll.Maternity;

/// <summary>A maternity claim as HR sees it: the leave it covers, the benefit, and where it stands with SSS.</summary>
/// <param name="CarriedByRunNumber">
/// On a Draft claim in the list, the unpaid run that advances its benefit (its allowance is locked
/// until that run is paid or discarded). Null otherwise, and on the claim a command answers with.
/// </param>
/// <param name="LeaveCancelled">
/// The claim's leave request was cancelled or rejected, so the claim can be moved to the leave the
/// employee refiled (<see cref="RelinkRequest"/>).
/// </param>
/// <param name="NettedByRunNumber">
/// On a Draft claim in the list, the first Paid run whose offset netted its allowance: the allowance
/// is then locked and the claim can't be voided. Null otherwise, and on the claim a command answers with.
/// </param>
public record MaternityClaimDto(Guid Id, Guid LeaveRequestId, Guid EmployeeId, string EmployeeName,
    DateOnly LeaveStart, DateOnly LeaveEnd, decimal Days, decimal? DailyAllowance, decimal Benefit,
    MaternityClaimStatus Status, Guid? AdvanceRunId, string? AdvanceRunNumber, DateOnly? AdvancedAt,
    DateOnly? ReimbursedOn, decimal? ReimbursedAmount, string? Note, string? CarriedByRunNumber = null,
    bool LeaveCancelled = false, string? NettedByRunNumber = null);

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

/// <summary>Why a Draft claim is voided. Nullable for the same reason as <see cref="DenyRequest"/>.</summary>
public record VoidRequest(string? Note);

/// <summary>The approved maternity leave, of the same employee and with no claim, to move a claim to.</summary>
public record RelinkRequest(Guid LeaveRequestId);

/// <summary>Every claim, newest first, and the benefit advanced but not yet reimbursed or denied.</summary>
public record MaternityClaimsSummaryDto(IReadOnlyList<MaternityClaimDto> Claims, decimal Outstanding);

/// <summary>An approved maternity leave request with no claim yet, which HR can open one for.</summary>
public record EligibleMaternityLeaveDto(Guid LeaveRequestId, Guid EmployeeId, string EmployeeName,
    DateOnly StartDate, DateOnly EndDate, decimal Days);
