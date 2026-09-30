namespace PeopleCore.Application.Payroll.Maternity;

/// <summary>
/// The SSS maternity benefit claims (RA 11210): one per approved maternity leave request. HR sets
/// the daily allowance (suggested from paid payroll), payroll advances the benefit, and HR records
/// what SSS reimbursed or that it denied the claim.
/// </summary>
public interface IMaternityClaimService
{
    /// <summary>Every claim, newest first, with the outstanding total (the benefit of Advanced claims).</summary>
    Task<MaternityClaimsSummaryDto> ListAsync(CancellationToken ct = default);

    /// <summary>Opens a Draft claim for an approved maternity leave request, once per request.</summary>
    Task<MaternityClaimDto> CreateAsync(Guid leaveRequestId, CancellationToken ct = default);

    /// <summary>The daily allowance the SSS formula gives from the employee's paid payroll in the contribution window.</summary>
    Task<SuggestedAllowanceDto> SuggestAsync(Guid claimId, CancellationToken ct = default);

    /// <summary>Sets a Draft claim's daily allowance and the benefit that follows from it.</summary>
    Task<MaternityClaimDto> SetAllowanceAsync(Guid claimId, SetAllowanceRequest request, CancellationToken ct = default);

    /// <summary>Records what SSS reimbursed on an Advanced claim.</summary>
    Task<MaternityClaimDto> ReimburseAsync(Guid claimId, ReimburseRequest request, CancellationToken ct = default);

    /// <summary>Records that SSS denied an Advanced claim.</summary>
    Task<MaternityClaimDto> DenyAsync(Guid claimId, DenyRequest request, CancellationToken ct = default);

    /// <summary>
    /// Marks a Draft claim not SSS-qualified - no unpaid run advancing it, no Paid run netting it - with
    /// a note: her leave days are then paid and taxed as ordinary salary.
    /// </summary>
    Task<MaternityClaimDto> MarkNotQualifiedAsync(Guid claimId, NotQualifiedRequest request, CancellationToken ct = default);

    /// <summary>Sets a claim marked not SSS-qualified back to Draft, while no Paid run covers days of its leave.</summary>
    Task<MaternityClaimDto> ReopenAsync(Guid claimId, CancellationToken ct = default);

    /// <summary>Voids a Draft claim that no unpaid run advances, with a note saying why.</summary>
    Task<MaternityClaimDto> VoidAsync(Guid claimId, VoidRequest request, CancellationToken ct = default);

    /// <summary>
    /// Moves a claim whose leave was cancelled or rejected to the employee's refiled maternity leave:
    /// an approved one with no claim. The claim takes its days; a Draft claim's benefit follows them,
    /// while one already advanced keeps the benefit that was paid.
    /// </summary>
    Task<MaternityClaimDto> RelinkAsync(Guid claimId, RelinkRequest request, CancellationToken ct = default);

    /// <summary>Approved maternity leave requests that have no claim yet, earliest leave first.</summary>
    Task<IReadOnlyList<EligibleMaternityLeaveDto>> EligibleAsync(CancellationToken ct = default);

    /// <summary>
    /// The employees with a claim ready to advance - Draft, with an allowance, for leave that is still
    /// Approved - that no run already advances.
    /// </summary>
    Task<IReadOnlyList<Guid>> ReadyEmployeeIdsAsync(CancellationToken ct = default);
}
