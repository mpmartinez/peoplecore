using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.Application.Payroll.Maternity;

/// <summary>
/// The maternity advance, offset and warnings for a regular run, and the claims its Mark Paid
/// settles (RA 11210). Reads approved leave the way the payroll attendance bridge does
/// (<see cref="ILeaveRequestRepository.GetApprovedByPeriodAsync"/>), keeping each employee's
/// requests of a maternity type, and the employees' claims.
/// </summary>
public sealed class MaternityPayCalculator : IMaternityPayCalculator
{
    private readonly ILeaveRequestRepository _leave;
    private readonly IMaternityClaimRepository _claims;
    private readonly IPayrollRunRepository _runs;
    private readonly IEmployeeRepository _employees;

    public MaternityPayCalculator(ILeaveRequestRepository leave, IMaternityClaimRepository claims,
        IPayrollRunRepository runs, IEmployeeRepository employees)
    {
        _leave = leave;
        _claims = claims;
        _runs = runs;
        _employees = employees;
    }

    public async Task<MaternityPay> ForAsync(PayrollRun run, Guid employeeId, bool advanceRequested,
        decimal regularPayBeforeOffset, bool exempt, CancellationToken ct = default)
        => (await LoadAsync(run, [employeeId], ct)).For(employeeId, advanceRequested, regularPayBeforeOffset, exempt);

    public Task<MaternityRun> LoadAsync(PayrollRun run, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default)
        => LoadAsync(run, employeeIds, lookUpNames: true, ct);

    public async Task<IReadOnlyList<string>> WarningsAsync(PayrollRun run, CancellationToken ct = default)
    {
        if (run.RunType != PayrollRunType.Regular || run.Employees.Count == 0)
            return [];

        // Names come with the leave and the claims, which are all a warning is about.
        var data = await LoadAsync(run, run.Employees.Select(e => e.EmployeeId).ToList(), lookUpNames: false, ct);
        return run.Employees
            .OrderBy(e => data.NameOf(e.EmployeeId, e.Employee?.FullName), StringComparer.CurrentCulture)
            .SelectMany(e => data.WarningsFor(e.EmployeeId, AdvancedOn(e), e.Employee?.FullName))
            .ToList();
    }

    public async Task<IReadOnlyList<MaternityClaim>> SettleAdvancesAsync(PayrollRun run, CancellationToken ct = default)
    {
        if (run.RunType != PayrollRunType.Regular)
            return [];
        var advancing = run.Employees.Where(e => AdvancedOn(e) is not null).ToList();
        if (advancing.Count == 0)
            return [];

        var claims = (await _claims.GetForEmployeesAsync(advancing.Select(e => e.EmployeeId).Distinct().ToList(), ct) ?? [])
            .ToDictionary(c => c.Id);

        // Every claim is checked before any is changed, so a refusal leaves them all as they were.
        var settling = new List<MaternityClaim>();
        foreach (var entry in advancing)
        {
            claims.TryGetValue(entry.MaternityClaimId!.Value, out var claim);
            var name = entry.Employee?.FullName ?? claim?.Employee?.FullName
                ?? (await _employees.GetByIdAsync(entry.EmployeeId, ct))?.FullName ?? entry.EmployeeId.ToString();

            if (claim is { Status: not MaternityClaimStatus.Draft })
                throw AlreadyAdvanced(name, claim.AdvanceRun?.RunNumber);
            if (claim is null || claim.DailyAllowance is null || claim.Benefit != entry.MaternityBenefitAdvance)
                throw new DomainException(
                    $"{name}'s maternity claim has changed since this payroll was computed; recompute it before paying.");
            settling.Add(claim);
        }

        foreach (var claim in settling)
        {
            claim.Status = MaternityClaimStatus.Advanced;
            claim.AdvanceRunId = run.Id;
            claim.AdvancedAt = run.PayDate;
        }
        return settling;
    }

    /// <summary>The claim a stored entry advances, or null when it advances nothing.</summary>
    private static Guid? AdvancedOn(PayrollRunEmployee entry)
        => entry.AdvanceMaternityBenefit && entry.MaternityBenefitAdvance > 0m ? entry.MaternityClaimId : null;

    private async Task<MaternityRun> LoadAsync(PayrollRun run, IReadOnlyCollection<Guid> employeeIds, bool lookUpNames,
        CancellationToken ct)
    {
        var ids = employeeIds.Distinct().ToList();
        if (run.RunType != PayrollRunType.Regular || ids.Count == 0)
            return MaternityRun.None;
        var wanted = ids.ToHashSet();

        var requests = (await _leave.GetApprovedByPeriodAsync(run.PeriodStart, run.PeriodEnd, ct) ?? [])
            .Where(r => wanted.Contains(r.EmployeeId) && r.LeaveType is { IsMaternity: true })
            .ToList();
        var claims = (await _claims.GetForEmployeesAsync(ids, ct) ?? [])
            .Where(c => wanted.Contains(c.EmployeeId))
            .ToList();

        // Advances on other runs. This run's own stored entry is left out: a recompute replaces it.
        var ready = claims.Where(MaternityRun.IsReady).Select(c => c.Id).ToList();
        var advancedElsewhere = ready.Count == 0
            ? new Dictionary<Guid, string>()
            : (await _runs.GetMaternityAdvancesAsync(ready, run.Id, ct) ?? [])
                .GroupBy(a => a.ClaimId)
                .ToDictionary(g => g.Key, g => g.First().RunNumber);

        var names = new Dictionary<Guid, string>();
        foreach (var employee in requests.Select(r => r.Employee).Concat(claims.Select(c => c.Employee)))
            if (employee is not null)
                names.TryAdd(employee.Id, employee.FullName);
        var unnamed = ids.Where(id => !names.ContainsKey(id)).ToList();
        if (lookUpNames && unnamed.Count > 0)
            foreach (var employee in await _employees.GetByIdsAsync(unnamed, ct) ?? [])
                names.TryAdd(employee.Id, employee.FullName);

        return new MaternityRun(run, requests.ToLookup(r => r.EmployeeId), claims.ToLookup(c => c.EmployeeId),
            advancedElsewhere, names);
    }

    internal static DomainException AlreadyAdvanced(string name, string? runNumber)
        => new($"{name}'s maternity benefit was already advanced on {runNumber ?? "an earlier payroll"}.");
}

/// <summary>
/// What <see cref="MaternityPayCalculator"/> read for a run's employees: their approved maternity
/// leave overlapping the period, their claims, and the other runs advancing those claims. Answers
/// each employee's figures without going back to the database.
/// </summary>
public sealed class MaternityRun
{
    /// <summary>For a final pay, or no employees: nothing to advance, offset or warn about.</summary>
    internal static readonly MaternityRun None = new(null, Enumerable.Empty<LeaveRequest>().ToLookup(r => r.EmployeeId),
        Enumerable.Empty<MaternityClaim>().ToLookup(c => c.EmployeeId), new Dictionary<Guid, string>(), new Dictionary<Guid, string>());

    private readonly PayrollRun? _run;
    private readonly ILookup<Guid, LeaveRequest> _requests;
    private readonly ILookup<Guid, MaternityClaim> _claims;
    private readonly IReadOnlyDictionary<Guid, string> _advancedElsewhere;
    private readonly IReadOnlyDictionary<Guid, string> _names;

    internal MaternityRun(PayrollRun? run, ILookup<Guid, LeaveRequest> requests, ILookup<Guid, MaternityClaim> claims,
        IReadOnlyDictionary<Guid, string> advancedElsewhere, IReadOnlyDictionary<Guid, string> names)
    {
        _run = run;
        _requests = requests;
        _claims = claims;
        _advancedElsewhere = advancedElsewhere;
        _names = names;
    }

    /// <summary>A claim that can be advanced: Draft, with the SSS daily allowance set.</summary>
    internal static bool IsReady(MaternityClaim claim)
        => claim.Status == MaternityClaimStatus.Draft && claim.DailyAllowance is not null;

    /// <summary>
    /// One employee's figures. The advance is the benefit of their ready claim, earliest leave first,
    /// that no other regular run carries; it is refused when there is none.
    /// </summary>
    public MaternityPay For(Guid employeeId, bool advanceRequested, decimal regularPayBeforeOffset, bool exempt)
    {
        if (_run is null)
            return MaternityPay.None;

        var advanced = advanceRequested ? ClaimToAdvance(employeeId) : null;
        var offset = Offset(employeeId, regularPayBeforeOffset, exempt);
        return new MaternityPay(advanced?.Benefit ?? 0m, offset, WarningsFor(employeeId, advanced?.Id), advanced?.Id);
    }

    /// <summary>
    /// What HR still has to do for the employee: set up the claim for leave in the period, and
    /// advance a ready claim no run carries yet.
    /// </summary>
    /// <param name="claimAdvancedHere">The claim this run advances for the employee, if any.</param>
    public IReadOnlyList<string> WarningsFor(Guid employeeId, Guid? claimAdvancedHere, string? knownName = null)
    {
        if (_run is null)
            return [];

        var warnings = new List<string>();
        var name = NameOf(employeeId, knownName);
        if (_requests[employeeId].Any(r => DaysInPeriod(r) > 0 && ClaimFor(r)?.DailyAllowance is null))
            warnings.Add($"Maternity benefit not set up yet for {name}.");
        if (_claims[employeeId].Any(c => IsReady(c) && c.Id != claimAdvancedHere && !_advancedElsewhere.ContainsKey(c.Id)))
            warnings.Add($"Maternity benefit not advanced yet for {name}.");
        return warnings;
    }

    internal string NameOf(Guid employeeId, string? knownName)
        => _names.GetValueOrDefault(employeeId) ?? knownName ?? employeeId.ToString();

    private MaternityClaim ClaimToAdvance(Guid employeeId)
    {
        var name = NameOf(employeeId, null);
        var claims = _claims[employeeId].ToList();
        var ready = claims.Where(IsReady)
            .OrderBy(c => c.LeaveRequest?.StartDate ?? DateOnly.MaxValue)
            .ThenBy(c => c.CreatedAt)
            .ToList();

        var free = ready.FirstOrDefault(c => !_advancedElsewhere.ContainsKey(c.Id));
        if (free is not null)
            return free;
        if (ready.Count > 0)
            throw MaternityPayCalculator.AlreadyAdvanced(name, _advancedElsewhere[ready[0].Id]);

        // A claim still in Draft only needs its allowance. With none, the latest one was advanced.
        if (claims.All(c => c.Status != MaternityClaimStatus.Draft)
            && claims.OrderByDescending(c => c.AdvancedAt).ThenByDescending(c => c.CreatedAt).FirstOrDefault() is { } done)
            throw MaternityPayCalculator.AlreadyAdvanced(name, done.AdvanceRun?.RunNumber);

        throw new DomainException($"{name} has no maternity claim ready to advance.");
    }

    /// <summary>
    /// The part of the regular pay SSS covers for the maternity days in the period. An exempt
    /// employer takes all of those days' pay off (the benefit is all the employee gets); otherwise
    /// it is the claim's daily allowance for each day, never more than the regular pay.
    /// </summary>
    private decimal Offset(Guid employeeId, decimal regularPay, bool exempt)
    {
        var requests = _requests[employeeId].OrderBy(r => r.StartDate).ToList();
        if (exempt)
        {
            int periodDays = _run!.PeriodEnd.DayNumber - _run.PeriodStart.DayNumber + 1;
            return MaternityMath.ExemptOffset(regularPay, requests.Sum(DaysInPeriod), periodDays);
        }

        decimal offset = 0m;
        foreach (var request in requests)
            if (ClaimFor(request)?.DailyAllowance is decimal allowance)
                offset += MaternityMath.Offset(regularPay - offset, allowance, DaysInPeriod(request));
        return offset;
    }

    private MaternityClaim? ClaimFor(LeaveRequest request)
        => _claims[request.EmployeeId].FirstOrDefault(c => c.LeaveRequestId == request.Id);

    private int DaysInPeriod(LeaveRequest request)
        => MaternityMath.DaysInPeriod(request.StartDate, request.EndDate, _run!.PeriodStart, _run.PeriodEnd);
}
