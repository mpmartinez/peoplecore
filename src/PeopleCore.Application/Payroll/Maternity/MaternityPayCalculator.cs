using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.Application.Payroll.Maternity;

/// <summary>
/// The maternity advance, offset and warnings for a run - a regular run or a final pay - and the
/// claims its Mark Paid settles (RA 11210). Reads approved leave the way the payroll attendance bridge does
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

    public Task<MaternityRun> LoadAsync(PayrollRun run, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default)
        => LoadAsync(run, employeeIds, lookUpNames: true, ct);

    public async Task<IReadOnlyList<string>> WarningsAsync(PayrollRun run, CancellationToken ct = default)
    {
        if (run.Employees.Count == 0)
            return [];

        // Names come with the leave and the claims, which are all a warning is about.
        var data = await LoadAsync(run, run.Employees.Select(e => e.EmployeeId).ToList(), lookUpNames: false, ct);
        return run.Employees
            .OrderBy(e => data.NameOf(e.EmployeeId, e.Employee?.FullName), StringComparer.CurrentCulture)
            .SelectMany(e => data.WarningsFor(e.EmployeeId, AdvancedOn(e), e.Employee?.FullName))
            .ToList();
    }

    public async Task EnsureAdvancesCurrentAsync(PayrollRun run, bool exempt, CancellationToken ct = default)
        => await AdvancedClaimsAsync(run, exempt, "recompute it before approving.", ct);

    public async Task EnsureClaimsSetUpAsync(PayrollRun run, bool exempt, CancellationToken ct = default)
    {
        if (exempt || run.Employees.Count == 0)
            return;
        var data = await LoadAsync(run, run.Employees.Select(e => e.EmployeeId).ToList(), lookUpNames: false, ct);
        foreach (var entry in run.Employees.OrderBy(e => data.NameOf(e.EmployeeId, e.Employee?.FullName), StringComparer.CurrentCulture))
        {
            int days = data.DaysWithoutAllowance(entry.EmployeeId);
            if (days > 0)
                throw new DomainException(
                    $"Set up {data.NameOf(entry.EmployeeId, entry.Employee?.FullName)}'s maternity claim before approving; " +
                    $"this payroll covers {days} maternity day(s).");
        }
    }

    public async Task<PayrollRunEmployee?> EntryNotMatchingClaimsAsync(PayrollRun run, bool exempt, CancellationToken ct = default)
        => (await StaleEntryAsync(run, exempt, ct))?.Entry;

    public async Task<IReadOnlyList<MaternityClaim>> SettleAdvancesAsync(PayrollRun run, bool exempt, CancellationToken ct = default)
    {
        // An approved run whose entries no longer match the claims can be recomputed for it
        // (EntryNotMatchingClaimsAsync lets PayrollRunService.ComputeAsync do so), back to Draft.
        var settling = await AdvancedClaimsAsync(run, exempt, "recompute it before paying.", ct);
        foreach (var claim in settling)
        {
            claim.Status = MaternityClaimStatus.Advanced;
            claim.AdvanceRunId = run.Id;
            claim.AdvancedAt = run.PayDate;
        }
        return settling;
    }

    /// <summary>
    /// The claims the run's entries advance, each checked to still be the one the run was computed
    /// with: Draft, and its benefit what the entry advances. Every entry's offset and differential
    /// are checked too: worked out again from its regular pay before the offset and the claims as
    /// they are now (allowance, status - a claim marked not SSS-qualified since offsets nothing - and
    /// the exemption), they must be what the entry stores. Everything is checked before the caller
    /// changes any claim, so a refusal leaves them all as they were.
    /// </summary>
    /// <param name="whatToDo">How the "has changed" refusal ends: what HR can do about it at this step.</param>
    private async Task<IReadOnlyList<MaternityClaim>> AdvancedClaimsAsync(PayrollRun run, bool exempt, string whatToDo,
        CancellationToken ct)
    {
        if (await StaleEntryAsync(run, exempt, ct) is { } stale)
            throw new DomainException($"{stale.Name}'s maternity claim has changed since this payroll was computed; {whatToDo}");

        var advancing = run.Employees.Where(e => AdvancedOn(e) is not null).ToList();
        if (advancing.Count == 0)
            return [];

        var claims = (await _claims.GetForEmployeesAsync(advancing.Select(e => e.EmployeeId).Distinct().ToList(), ct) ?? [])
            .ToDictionary(c => c.Id);

        var advanced = new List<MaternityClaim>();
        foreach (var entry in advancing)
        {
            // Every advanced claim still matches (StaleEntryAsync); one advanced elsewhere since is
            // named instead.
            var claim = claims[entry.MaternityClaimId!.Value];
            if (claim.Status != MaternityClaimStatus.Draft)
                throw AlreadyAdvanced(await NameAsync(entry, claim.Employee?.FullName, ct), claim.AdvanceRun?.RunNumber);
            advanced.Add(claim);
        }
        return advanced;
    }

    /// <summary>
    /// The first entry the run's claims no longer give, and its employee's name, or null. Every
    /// entry's offset and differential are worked out again from its regular pay before the offset
    /// (<c>RegularPay + MaternityBenefitOffset</c>: the engine never takes more than there is) and the
    /// claims as they are now - allowance, status (a claim marked not SSS-qualified since offsets
    /// nothing), the leave still approved, and the exemption. An entry that advances a claim must
    /// still find it, with an allowance and the benefit it advances; a claim that has since been
    /// advanced elsewhere isn't stale here (the caller names that run instead).
    /// </summary>
    private async Task<(PayrollRunEmployee Entry, string Name)?> StaleEntryAsync(PayrollRun run, bool exempt,
        CancellationToken ct)
    {
        if (run.Employees.Count == 0)
            return null;

        var data = await LoadAsync(run, run.Employees.Select(e => e.EmployeeId).ToList(), lookUpNames: false, ct);
        foreach (var entry in run.Employees)
        {
            var now = data.For(entry.EmployeeId, advanceRequested: false,
                entry.RegularPay + entry.MaternityBenefitOffset, exempt);
            if (now.Offset != entry.MaternityBenefitOffset || now.Differential != entry.MaternityDifferential)
                return (entry, await NameAsync(entry, data.KnownNameOf(entry.EmployeeId), ct));
        }

        var advancing = run.Employees.Where(e => AdvancedOn(e) is not null).ToList();
        if (advancing.Count == 0)
            return null;
        var claims = (await _claims.GetForEmployeesAsync(advancing.Select(e => e.EmployeeId).Distinct().ToList(), ct) ?? [])
            .ToDictionary(c => c.Id);
        foreach (var entry in advancing)
        {
            claims.TryGetValue(entry.MaternityClaimId!.Value, out var claim);
            if (claim is { Status: not MaternityClaimStatus.Draft })
                continue;
            // Its leave cancelled or rejected since: the run would pay a benefit for leave that no
            // longer stands.
            if (claim is null || claim.DailyAllowance is null || claim.Benefit != entry.MaternityBenefitAdvance
                || MaternityRun.IsWithdrawn(claim))
                return (entry, await NameAsync(entry, claim?.Employee?.FullName, ct));
        }
        return null;
    }

    /// <summary>The entry's employee's name: as loaded with the entry, else as known, else looked up.</summary>
    private async Task<string> NameAsync(PayrollRunEmployee entry, string? known, CancellationToken ct)
        => entry.Employee?.FullName ?? known
           ?? (await _employees.GetByIdAsync(entry.EmployeeId, ct))?.FullName ?? entry.EmployeeId.ToString();

    /// <summary>The claim a stored entry advances, or null when it advances nothing.</summary>
    private static Guid? AdvancedOn(PayrollRunEmployee entry)
        => entry.AdvanceMaternityBenefit && entry.MaternityBenefitAdvance > 0m ? entry.MaternityClaimId : null;

    private async Task<MaternityRun> LoadAsync(PayrollRun run, IReadOnlyCollection<Guid> employeeIds, bool lookUpNames,
        CancellationToken ct)
    {
        var ids = employeeIds.Distinct().ToList();
        if (ids.Count == 0)
            return MaternityRun.None;
        var wanted = ids.ToHashSet();

        // A final pay with no salary days (regular payroll already paid up to the last working day)
        // stores that day alone as its period and pays no salary, so no leave day on it is covered.
        bool paysNoSalary = run.RunType == PayrollRunType.FinalPay && run.FinalPayInputs is { WorkingDays: 0m };
        var requests = paysNoSalary
            ? []
            : (await _leave.GetApprovedByPeriodAsync(run.PeriodStart, run.PeriodEnd, ct) ?? [])
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
    /// <summary>For no employees: nothing to advance, offset or warn about.</summary>
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

    /// <summary>
    /// A claim that can be advanced: Draft, with the SSS daily allowance set, for leave that is still
    /// Approved - a claim whose leave was cancelled or rejected waits to be moved to the refiled leave.
    /// </summary>
    internal static bool IsReady(MaternityClaim claim)
        => claim.Status == MaternityClaimStatus.Draft && claim.DailyAllowance is not null
           && claim.LeaveRequest is { Status: LeaveStatus.Approved };

    /// <summary>
    /// One employee's figures. The advance is the benefit of their ready claim, earliest leave first,
    /// that no other regular run carries. It is refused, naming the run, when every ready claim is
    /// carried elsewhere or the claim for leave still current was already advanced; and as none
    /// ready otherwise - an earlier pregnancy's claim, advanced long ago, isn't this one.
    /// </summary>
    public MaternityPay For(Guid employeeId, bool advanceRequested, decimal regularPayBeforeOffset, bool exempt)
    {
        if (_run is null)
            return MaternityPay.None;

        var advanced = advanceRequested ? ClaimToAdvance(employeeId) : null;
        var (offset, differential, offsetClaim) = Offset(employeeId, regularPayBeforeOffset, exempt);
        return new MaternityPay(advanced?.Benefit ?? 0m, offset, WarningsFor(employeeId, advanced?.Id),
            advanced?.Id ?? (offset > 0m ? offsetClaim : null), differential);
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
        if (_requests[employeeId].Any(r => DaysInPeriod(r) > 0 && !IsNotQualified(r) && ClaimFor(r)?.DailyAllowance is null))
            warnings.Add($"Maternity benefit not set up yet for {name}.");
        if (_claims[employeeId].Any(c => IsReady(c) && c.Id != claimAdvancedHere && !_advancedElsewhere.ContainsKey(c.Id)))
            warnings.Add($"Maternity benefit not advanced yet for {name}.");
        return warnings;
    }

    /// <summary>The employee's name as read with the leave and the claims, or null when neither had it.</summary>
    internal string? KnownNameOf(Guid employeeId) => _names.GetValueOrDefault(employeeId);

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

        // No ready claim. One already advanced for leave that hasn't ended before this period is this
        // pregnancy's, so the benefit was already advanced - on the latest such run. A claim for leave
        // that ended earlier is an earlier pregnancy's, and a denied claim no run advanced has no run
        // to name: then there is simply nothing ready.
        var advancedForCurrentLeave = claims
            .Where(c => c.Status is not (MaternityClaimStatus.Draft or MaternityClaimStatus.Voided or MaternityClaimStatus.NotQualified)
                        && (c.LeaveRequest is null || c.LeaveRequest.EndDate >= _run!.PeriodStart)
                        && c.AdvanceRun is not null)
            .OrderByDescending(c => c.AdvancedAt)
            .ThenByDescending(c => c.CreatedAt)
            .FirstOrDefault();
        if (advancedForCurrentLeave is not null)
            throw MaternityPayCalculator.AlreadyAdvanced(name, advancedForCurrentLeave.AdvanceRun!.RunNumber);

        // A claim that would be ready but for its leave, cancelled or rejected since: the way on is to
        // move it to the leave she refiled, or to take her off this payroll.
        if (claims.Any(c => c.Status == MaternityClaimStatus.Draft && c.DailyAllowance is not null && IsWithdrawn(c)))
            throw new DomainException(
                $"{name}'s maternity leave was cancelled; move her claim to the refiled leave, or take her off this payroll.");

        throw new DomainException($"{name} has no maternity claim ready to advance.");
    }

    /// <summary>
    /// The part of the regular pay SSS covers for the maternity days in the period, and the salary
    /// differential: the rest of those days' pay (<see cref="MaternityMath.MaternityDaysPay"/>). An
    /// exempt employer takes all of those days' pay off (the benefit is all the employee gets), so
    /// there is no differential. Otherwise the days are the ones a claim with an allowance covers:
    /// SSS covers each claim's daily allowance for its days, never more than those days' pay, so the
    /// pay for days outside the leave is never reduced. Days whose claim has no allowance yet are
    /// left as ordinary pay - no offset and no differential - and warned about. The claim returned is
    /// the one whose allowance the offset nets first (by start date), for the entry to record; none for
    /// an exempt offset, which nets no allowance.
    /// </summary>
    private (decimal Offset, decimal Differential, Guid? ClaimId) Offset(Guid employeeId, decimal regularPay, bool exempt)
    {
        var days = OwnDaysInPeriod(employeeId);
        int periodDays = _run!.PeriodEnd.DayNumber - _run.PeriodStart.DayNumber + 1;
        // The exempt offset doesn't use any claim's allowance, so the entry records no claim for it:
        // it must never lock the allowance (or the void) of a claim still being set up.
        if (exempt)
            // Leave whose claim isn't SSS-qualified has no benefit behind it: she is paid her salary.
            return (MaternityMath.ExemptOffset(regularPay, days.Where(d => !IsNotQualified(d.Request)).Sum(d => d.Days),
                periodDays), 0m, null);

        int coveredDays = 0;
        decimal covered = 0m;
        Guid? claimId = null;
        foreach (var (request, own) in days)
            if (own > 0 && !IsNotQualified(request) && ClaimFor(request) is { DailyAllowance: decimal allowance } claim)
            {
                coveredDays += own;
                covered += MaternityMath.Benefit(allowance, own);
                claimId ??= claim.Id;
            }
        if (coveredDays == 0)
            return (0m, 0m, null);

        var pay = MaternityMath.MaternityDaysPay(regularPay, coveredDays, periodDays);
        var offset = Math.Min(pay, covered);
        return (offset, MaternityMath.Differential(pay, offset), claimId);
    }

    /// <summary>
    /// The employee's maternity days in the period that no claim with an allowance covers - the days
    /// approval refuses to pay as ordinary salary.
    /// </summary>
    internal int DaysWithoutAllowance(Guid employeeId)
        => _run is null
            ? 0
            : OwnDaysInPeriod(employeeId)
                .Where(d => !IsNotQualified(d.Request) && ClaimFor(d.Request)?.DailyAllowance is null)
                .Sum(d => d.Days);

    /// <summary>
    /// The request's claim is marked not SSS-qualified: its days are ordinary salary - nothing to
    /// offset, set up or warn about.
    /// </summary>
    private bool IsNotQualified(LeaveRequest request) => ClaimFor(request)?.Status == MaternityClaimStatus.NotQualified;

    /// <summary>
    /// Each request's leave days in the period that no earlier request (by start date) already
    /// covers, so overlapping approved requests count each calendar day once - the union of their
    /// ranges. An overlapped day goes to the request that starts first, and so to its claim.
    /// </summary>
    private List<(LeaveRequest Request, int Days)> OwnDaysInPeriod(Guid employeeId)
    {
        var covered = new HashSet<DateOnly>();
        var result = new List<(LeaveRequest, int)>();
        foreach (var request in _requests[employeeId].OrderBy(r => r.StartDate).ThenBy(r => r.EndDate))
        {
            var from = request.StartDate > _run!.PeriodStart ? request.StartDate : _run.PeriodStart;
            var to = request.EndDate < _run.PeriodEnd ? request.EndDate : _run.PeriodEnd;
            int own = 0;
            for (var date = from; date <= to; date = date.AddDays(1))
                if (covered.Add(date))
                    own++;
            result.Add((request, own));
        }
        return result;
    }

    /// <summary>The claim's leave was cancelled or rejected after the claim was opened for it.</summary>
    internal static bool IsWithdrawn(MaternityClaim claim)
        => claim.LeaveRequest is { Status: LeaveStatus.Cancelled or LeaveStatus.Rejected };

    /// <summary>The request's claim, unless it was voided: a voided claim covers nothing.</summary>
    private MaternityClaim? ClaimFor(LeaveRequest request)
        => _claims[request.EmployeeId].FirstOrDefault(c => c.LeaveRequestId == request.Id
                                                           && c.Status != MaternityClaimStatus.Voided);

    private int DaysInPeriod(LeaveRequest request)
        => MaternityMath.DaysInPeriod(request.StartDate, request.EndDate, _run!.PeriodStart, _run.PeriodEnd);
}
