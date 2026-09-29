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

    /// <summary>Approved maternity leave requests that have no claim yet, earliest leave first.</summary>
    Task<IReadOnlyList<EligibleMaternityLeaveDto>> EligibleAsync(CancellationToken ct = default);

    /// <summary>The employees with a Draft claim that has an allowance: the ones a payroll can advance the benefit to.</summary>
    Task<IReadOnlyList<Guid>> ReadyEmployeeIdsAsync(CancellationToken ct = default);
}
