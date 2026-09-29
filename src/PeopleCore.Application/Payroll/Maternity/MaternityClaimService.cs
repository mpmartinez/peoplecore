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

    public MaternityClaimService(IMaternityClaimRepository claims, ILeaveRequestRepository leaveRequests, IPayrollRunRepository runs)
    {
        _claims = claims;
        _leaveRequests = leaveRequests;
        _runs = runs;
    }

    public async Task<MaternityClaimsSummaryDto> ListAsync(CancellationToken ct = default)
    {
        var claims = await _claims.GetAllAsync(ct);
        decimal outstanding = claims.Where(c => c.Status == MaternityClaimStatus.Advanced).Sum(c => c.Benefit);
        return new MaternityClaimsSummaryDto(claims.Select(ToDto).ToList(), outstanding);
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
        await _claims.AddAsync(claim, ct);
        return ToDto(claim);
    }

    /// <summary>
    /// For each month of the contribution window, the MSC behind the employee's SSS share over the
    /// Paid regular runs whose period ends in the month - the month the SSS remittance report puts
    /// a run in. Months with no entry, or no SSS deducted, have no MSC and are skipped.
    /// </summary>
    public async Task<SuggestedAllowanceDto> SuggestAsync(Guid claimId, CancellationToken ct = default)
    {
        var claim = await GetAsync(claimId, ct);
        var (from, to) = MaternityMath.ContributionWindow(claim.LeaveRequest.StartDate);

        var credits = new List<decimal>();
        for (var month = from; month <= to; month = month.AddMonths(1))
        {
            var entries = (await _runs.GetPaidRunsByPeriodEndMonthAsync(month.Year, month.Month, ct))
                .Where(r => r.RunType == PayrollRunType.Regular)
                .SelectMany(r => r.Employees)
                .Where(e => e.EmployeeId == claim.EmployeeId)
                .ToList();
            if (entries.Count == 0) continue;

            var (msc, _) = GovernmentReportMath.SssCredit(entries.Sum(e => e.SSSEmployee));
            if (msc is decimal credit) credits.Add(credit);
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
        if (request.ReimbursedAmount <= 0m)
            throw new DomainException("Enter the amount SSS reimbursed.");
        var note = Note(request.Note);
        if (request.ReimbursedAmount != claim.Benefit && note is null)
            throw new DomainException("Explain why the reimbursement differs from the benefit.");

        claim.ReimbursedOn = request.ReimbursedOn;
        claim.ReimbursedAmount = request.ReimbursedAmount;
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

    public async Task<IReadOnlyList<EligibleMaternityLeaveDto>> EligibleAsync(CancellationToken ct = default)
        => (await _claims.GetUnclaimedApprovedRequestsAsync(ct))
            .Select(r => new EligibleMaternityLeaveDto(r.Id, r.EmployeeId, r.Employee.FullName, r.StartDate, r.EndDate, r.TotalDays))
            .ToList();

    public async Task<IReadOnlyList<Guid>> ReadyEmployeeIdsAsync(CancellationToken ct = default)
        => (await _claims.GetAllAsync(ct))
            .Where(c => c.Status == MaternityClaimStatus.Draft && c.DailyAllowance is not null)
            .Select(c => c.EmployeeId)
            .Distinct()
            .ToList();

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
        c.ReimbursedOn, c.ReimbursedAmount, c.Note);
}
