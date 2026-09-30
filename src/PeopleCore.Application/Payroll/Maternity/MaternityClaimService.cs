using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Payroll.GovernmentReports;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.Application.Payroll.Maternity;

/// <summary>
/// The SSS maternity benefit claims (RA 11210). A claim is opened for an approved maternity leave
/// request, HR sets the SSS daily allowance (suggested from paid payroll), a payroll advances the
/// benefit (<c>PayrollRunService</c> marks the claim Advanced), and HR records what SSS reimbursed
/// or that it denied the claim.
/// </summary>
public sealed class MaternityClaimService : IMaternityClaimService
{
    private const int NoteMaxLength = 500;

    private readonly IMaternityClaimRepository _claims;
    private readonly ILeaveRequestRepository _leaveRequests;
    private readonly IPayrollRunRepository _runs;
    private readonly IPayrollSettingsRepository _settings;

    public MaternityClaimService(IMaternityClaimRepository claims, ILeaveRequestRepository leaveRequests,
        IPayrollRunRepository runs, IPayrollSettingsRepository settings)
    {
        _claims = claims;
        _leaveRequests = leaveRequests;
        _runs = runs;
        _settings = settings;
    }

    public async Task<MaternityClaimsSummaryDto> ListAsync(CancellationToken ct = default)
    {
        var claims = await _claims.GetAllAsync(ct);
        decimal outstanding = claims.Where(c => c.Status == MaternityClaimStatus.Advanced).Sum(c => c.Benefit);

        // The unpaid run advancing each Draft claim, by the query ReadyEmployeeIdsAsync uses. (A paid
        // run made its claim Advanced.) The earliest pay date comes first; that one is named.
        var drafts = claims.Where(c => c.Status == MaternityClaimStatus.Draft).Select(c => c.Id).ToList();
        var carriedBy = new Dictionary<Guid, string>();
        if (drafts.Count > 0)
        {
            foreach (var advance in await _runs.GetMaternityAdvancesAsync(drafts, Guid.Empty, ct) ?? [])
                carriedBy.TryAdd(advance.ClaimId, advance.RunNumber);
        }

        return new MaternityClaimsSummaryDto(
            claims.Select(c => ToDto(c) with { CarriedByRunNumber = carriedBy.GetValueOrDefault(c.Id) }).ToList(),
            outstanding);
    }

    public async Task<MaternityClaimDto> CreateAsync(Guid leaveRequestId, CancellationToken ct = default)
    {
        var request = await _leaveRequests.GetByIdAsync(leaveRequestId, ct)
                      ?? throw new KeyNotFoundException($"Leave request {leaveRequestId} not found.");
        if (request.Status != LeaveStatus.Approved || !request.LeaveType.IsMaternity)
            throw new DomainException("Only an approved maternity leave request can have a claim.");
        if (await _claims.GetByLeaveRequestAsync(leaveRequestId, ct) is not null)
            throw new DomainException($"{request.Employee.FullName} already has a maternity claim for this leave.");

        var claim = new MaternityClaim
        {
            LeaveRequestId = request.Id,
            LeaveRequest = request,
            EmployeeId = request.EmployeeId,
            Employee = request.Employee,
            Days = request.TotalDays,
            Status = MaternityClaimStatus.Draft,
        };
        // A rival create that commits between the check above and this save is refused by the
        // unique index, and AddNewAsync turns that into the same message.
        await _claims.AddNewAsync(claim, ct);
        return ToDto(claim);
    }

    /// <summary>
    /// For each month of the contribution window, the MSC behind the employee's SSS share over
    /// every Paid run whose period ends in the month (final pays included), the way the SSS
    /// remittance report works it out: a month whose cutoffs aren't all in is skipped, as its
    /// share would understate the MSC, and months with no entry or no SSS deducted have no MSC.
    /// Each month's MSC counts only up to the Regular SS ceiling. No suggestion when the payroll
    /// settings override both SSS rates, as the MSC can't be worked back from the share.
    /// </summary>
    public async Task<SuggestedAllowanceDto> SuggestAsync(Guid claimId, CancellationToken ct = default)
    {
        var claim = await GetAsync(claimId, ct);
        var (from, to) = MaternityMath.ContributionWindow(claim.LeaveRequest.StartDate);
        if (GovernmentReportMath.SssRatesOverridden(await _settings.GetDefaultAsync(ct)))
            return new SuggestedAllowanceDto(null, 0, from, to, RatesOverridden: true);

        var credits = new List<decimal>();
        for (var month = from; month <= to; month = month.AddMonths(1))
        {
            var entries = (await _runs.GetPaidRunsByPeriodEndMonthAsync(month.Year, month.Month, ct))
                .SelectMany(r => r.Employees
                    .Where(e => e.EmployeeId == claim.EmployeeId)
                    .Select(e => (Run: r, Entry: e)))
                .ToList();
            if (entries.Count == 0) continue;

            var cutoffs = entries.Where(x => x.Entry.SSSEmployee > 0m).Select(x => (x.Run.Frequency, x.Run.RunType));
            if (!GovernmentReportMath.IsFullSssMonth(cutoffs)) continue;

            var (msc, _) = GovernmentReportMath.SssCredit(entries.Sum(x => x.Entry.SSSEmployee));
            if (msc is decimal credit) credits.Add(Math.Min(credit, MaternityMath.RegularSsMscCeiling));
        }

        return new SuggestedAllowanceDto(MaternityMath.SuggestedDailyAllowance(credits), credits.Count, from, to);
    }

    public async Task<MaternityClaimDto> SetAllowanceAsync(Guid claimId, SetAllowanceRequest request, CancellationToken ct = default)
    {
        var claim = await GetAsync(claimId, ct);
        if (claim.Status != MaternityClaimStatus.Draft)
            throw new DomainException("Only a draft claim's allowance can be changed.");
        if (request.DailyAllowance <= 0m)
            throw new DomainException("Enter the SSS daily maternity allowance.");
        // A run advancing the benefit was computed with it; changed, the run would advance the old
        // figure, and once approved it can't be recomputed. A paid run made the claim Advanced, so
        // the run found here is unpaid.
        await EnsureNoRunAdvancesAsync(claim, ct);
        // A paid cutoff already took the SSS benefit for its leave days off her pay at this
        // allowance; it can't be recomputed, so the allowance can't change under it.
        var netted = await _runs.GetPaidRunsNettingMaternityClaimAsync(claim.Id, ct) ?? [];
        if (netted.Count > 0)
            throw new DomainException($"{netted[0]} already netted this allowance; it can't change now.");

        // Stored to 2 dp (numeric(18,2)), and the benefit is worked from what is stored.
        claim.DailyAllowance = Math.Round(request.DailyAllowance, 2, MidpointRounding.AwayFromZero);
        claim.Benefit = MaternityMath.Benefit(claim.DailyAllowance.Value, claim.Days);
        await _claims.UpdateAsync(claim, ct);
        return ToDto(claim);
    }

    public async Task<MaternityClaimDto> ReimburseAsync(Guid claimId, ReimburseRequest request, CancellationToken ct = default)
    {
        var claim = await GetAsync(claimId, ct);
        if (claim.Status != MaternityClaimStatus.Advanced)
            throw new DomainException("Only an advanced claim can be reimbursed.");
        if (request.ReimbursedOn == default)
            throw new DomainException("Enter the date SSS reimbursed the claim.");
        // Compared and stored to 2 dp, as the benefit is (numeric(18,2)).
        decimal amount = Math.Round(request.ReimbursedAmount, 2, MidpointRounding.AwayFromZero);
        if (amount <= 0m)
            throw new DomainException("Enter the amount SSS reimbursed.");
        var note = Note(request.Note);
        if (amount != claim.Benefit && note is null)
            throw new DomainException("Explain why the reimbursement differs from the benefit.");

        claim.ReimbursedOn = request.ReimbursedOn;
        claim.ReimbursedAmount = amount;
        claim.Note = note;
        claim.Status = MaternityClaimStatus.Reimbursed;
        await _claims.UpdateAsync(claim, ct);
        return ToDto(claim);
    }

    public async Task<MaternityClaimDto> DenyAsync(Guid claimId, DenyRequest request, CancellationToken ct = default)
    {
        var claim = await GetAsync(claimId, ct);
        if (claim.Status != MaternityClaimStatus.Advanced)
            throw new DomainException("Only an advanced claim can be denied.");
        var note = Note(request.Note) ?? throw new DomainException("Explain why SSS denied the claim.");

        claim.Note = note;
        claim.Status = MaternityClaimStatus.Denied;
        await _claims.UpdateAsync(claim, ct);
        return ToDto(claim);
    }

    public async Task<MaternityClaimDto> VoidAsync(Guid claimId, VoidRequest request, CancellationToken ct = default)
    {
        var claim = await GetAsync(claimId, ct);
        if (claim.Status != MaternityClaimStatus.Draft)
            throw new DomainException("Only a draft claim can be voided.");
        var note = Note(request.Note) ?? throw new DomainException("Explain why the claim is voided.");
        await EnsureNoRunAdvancesAsync(claim, ct);

        claim.Note = note;
        claim.Status = MaternityClaimStatus.Voided;
        await _claims.UpdateAsync(claim, ct);
        return ToDto(claim);
    }

    public async Task<MaternityClaimDto> RelinkAsync(Guid claimId, RelinkRequest request, CancellationToken ct = default)
    {
        var claim = await GetAsync(claimId, ct);
        if (!IsWithdrawn(claim.LeaveRequest))
            throw new DomainException("Only a claim whose leave was cancelled can be moved.");

        var target = await _leaveRequests.GetByIdAsync(request.LeaveRequestId, ct);
        if (target is null || target.EmployeeId != claim.EmployeeId || target.Status != LeaveStatus.Approved
            || target.LeaveType is not { IsMaternity: true }
            || await _claims.GetByLeaveRequestAsync(target.Id, ct) is not null)
            throw new DomainException("Choose an approved maternity leave of the same employee that has no claim.");

        claim.LeaveRequestId = target.Id;
        claim.LeaveRequest = target;
        claim.Days = target.TotalDays;
        // A Draft claim's benefit is still to be advanced, so it follows the refiled leave's days; one
        // already advanced keeps the benefit that was paid - what SSS is asked to reimburse.
        if (claim.Status == MaternityClaimStatus.Draft)
            claim.Benefit = claim.DailyAllowance is decimal allowance ? MaternityMath.Benefit(allowance, claim.Days) : 0m;
        await _claims.UpdateAsync(claim, ct);
        return ToDto(claim);
    }

    /// <summary>Refuses while an unpaid run advances the claim's benefit (a paid one made it Advanced).</summary>
    private async Task EnsureNoRunAdvancesAsync(MaternityClaim claim, CancellationToken ct)
    {
        var carrying = await _runs.GetMaternityAdvancesAsync([claim.Id], Guid.Empty, ct) ?? [];
        if (carrying.Count > 0)
            throw new DomainException($"{carrying[0].RunNumber} advances this benefit; discard it or pay it first.");
    }

    /// <summary>The leave was cancelled or rejected after the claim was opened for it.</summary>
    private static bool IsWithdrawn(Domain.Entities.Leave.LeaveRequest? leave)
        => leave is { Status: LeaveStatus.Cancelled or LeaveStatus.Rejected };

    public async Task<IReadOnlyList<EligibleMaternityLeaveDto>> EligibleAsync(CancellationToken ct = default)
        => (await _claims.GetUnclaimedApprovedRequestsAsync(ct))
            .Select(r => new EligibleMaternityLeaveDto(r.Id, r.EmployeeId, r.Employee.FullName, r.StartDate, r.EndDate, r.TotalDays))
            .ToList();

    public async Task<IReadOnlyList<Guid>> ReadyEmployeeIdsAsync(CancellationToken ct = default)
    {
        // Ready means the claim's leave is still Approved: a claim for leave cancelled or rejected
        // since, awaiting a move to the refiled leave, isn't.
        var ready = (await _claims.GetAllAsync(ct))
            .Where(c => c.Status == MaternityClaimStatus.Draft && c.DailyAllowance is not null
                        && c.LeaveRequest is { Status: LeaveStatus.Approved })
            .ToList();
        if (ready.Count == 0)
            return [];

        // A claim an unpaid run already advances can't go on another one. (A paid run made its
        // claim Advanced, so it isn't ready to begin with.)
        var carried = (await _runs.GetMaternityAdvancesAsync(ready.Select(c => c.Id).ToList(), Guid.Empty, ct) ?? [])
            .Select(a => a.ClaimId)
            .ToHashSet();
        return ready
            .Where(c => !carried.Contains(c.Id))
            .Select(c => c.EmployeeId)
            .Distinct()
            .ToList();
    }

    private async Task<MaternityClaim> GetAsync(Guid claimId, CancellationToken ct)
        => await _claims.GetByIdAsync(claimId, ct)
           ?? throw new KeyNotFoundException($"Maternity claim {claimId} not found.");

    /// <summary>The note trimmed, or null when blank. Refused over the column's 500 characters.</summary>
    private static string? Note(string? note)
    {
        var trimmed = note?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (trimmed.Length > NoteMaxLength)
            throw new DomainException($"Keep the note to {NoteMaxLength} characters.");
        return trimmed;
    }

    private static MaternityClaimDto ToDto(MaternityClaim c) => new(
        c.Id, c.LeaveRequestId, c.EmployeeId, c.Employee.FullName,
        c.LeaveRequest.StartDate, c.LeaveRequest.EndDate, c.Days, c.DailyAllowance, c.Benefit,
        c.Status, c.AdvanceRunId, c.AdvanceRunId is null ? null : c.AdvanceRun?.RunNumber, c.AdvancedAt,
        c.ReimbursedOn, c.ReimbursedAmount, c.Note, LeaveCancelled: IsWithdrawn(c.LeaveRequest));
}
