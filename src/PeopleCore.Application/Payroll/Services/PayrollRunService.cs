using System.Globalization;
using Microsoft.Extensions.Logging;
using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.FinalPay;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Application.Payroll.Validation;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using PeopleCore.Domain.Payroll;

namespace PeopleCore.Application.Payroll.Services;

public class PayrollRunService : IPayrollRunService
{
    private readonly IPayrollRunRepository _runRepo;
    private readonly IEmployeeCompensationRepository _compensationRepo;
    private readonly IEmployeeAllowanceRepository _allowanceRepo;
    private readonly IEmployeeLoanRepository _loanRepo;
    private readonly IPayrollSettingsRepository _settingsRepo;
    private readonly PayrollComputationService _computationService;
    private readonly IPayrollAttendanceBridge _attendanceBridge;
    private readonly IEmployeeRepository _employeeRepo;
    private readonly ISeparationRepository _separations;
    private readonly ILogger<PayrollRunService> _logger;
    private readonly IFinalPayService? _finalPay;

    /// <param name="finalPay">
    /// Recomputes final-pay runs, which are built from a separation rather than from a list of
    /// employees. Optional so callers that never see a final-pay run needn't supply one; computing
    /// a final-pay run without it is refused.
    /// </param>
    public PayrollRunService(
        IPayrollRunRepository runRepo,
        IEmployeeCompensationRepository compensationRepo,
        IEmployeeAllowanceRepository allowanceRepo,
        IEmployeeLoanRepository loanRepo,
        IPayrollSettingsRepository settingsRepo,
        PayrollComputationService computationService,
        IPayrollAttendanceBridge attendanceBridge,
        IEmployeeRepository employeeRepo,
        ISeparationRepository separations,
        ILogger<PayrollRunService> logger,
        IFinalPayService? finalPay = null)
    {
        _runRepo = runRepo;
        _compensationRepo = compensationRepo;
        _allowanceRepo = allowanceRepo;
        _loanRepo = loanRepo;
        _settingsRepo = settingsRepo;
        _computationService = computationService;
        _attendanceBridge = attendanceBridge;
        _employeeRepo = employeeRepo;
        _separations = separations;
        _logger = logger;
        _finalPay = finalPay;
    }

    public async Task<PayrollRunDto> CreateAsync(CreatePayrollRunRequest request, CancellationToken ct = default)
    {
        PayrollRunRequestValidator.Validate(request);
        await EnsureNoOneHasLeftAsync(request.Employees.Select(e => e.EmployeeId).ToList(),
            request.PeriodStart, request.PeriodEnd, ct);

        var year = request.PeriodStart.Year;
        var sequence = await _runRepo.CountForYearAsync(year, ct) + 1;

        var run = new PayrollRun
        {
            RunNumber = $"PAY-{year}-{sequence:D3}",
            PeriodStart = request.PeriodStart,
            PeriodEnd = request.PeriodEnd,
            PayDate = request.PayDate,
            Frequency = request.Frequency,
            Status = PayrollRunStatus.Draft,
            AttendancePeriodId = request.AttendancePeriodId
        };

        // No snapshots to honour on a brand new run, so the attendance is derived.
        run.Employees = await ComputeEntriesAsync(run, request.Employees, snapshots: null, ct);

        await _runRepo.AddWithEntriesAsync(run, ct);

        // PayrollRunEmployee.Employee is populated by EF fixup only when a run is reloaded via
        // GetWithEntriesAsync's .Include(...).ThenInclude(e => e.Employee) - Compute never sets
        // it, since it works from the employee's compensation, not the person (see that
        // property's remarks). Reloading here, rather than mapping names from data already in
        // hand, guarantees the create response reports the exact same names GET would - the two
        // can never drift onto two different lookups.
        var saved = await _runRepo.GetWithEntriesAsync(run.Id, ct)
            ?? throw new InvalidOperationException($"Payroll run {run.Id} was not found immediately after being saved.");

        return ToDto(saved);
    }

    public async Task ComputeAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await _runRepo.GetWithEntriesAsync(runId, ct)
            ?? throw new KeyNotFoundException($"Payroll run {runId} not found.");

        // A committed run is settled: an approver has signed off on these figures, or - worse,
        // for a paid run - they have already been used to retire loan balances. Recomputing
        // either would silently change what someone was, or is about to be, paid.
        if (run.Status is PayrollRunStatus.Approved or PayrollRunStatus.Paid)
            throw new DomainException("Only draft or for-approval payroll runs can be recomputed.");

        if (run.Employees.Count == 0)
            throw new DomainException("Payroll run has no employees to compute.");

        // A final-pay run is rebuilt from its separation and stored inputs - its short period's
        // working days, HR's overrides and deductions - and its tax is settled for the year, none
        // of which the regular path below knows about.
        if (run.RunType == PayrollRunType.FinalPay)
        {
            var finalPay = _finalPay ?? throw new InvalidOperationException(
                "PayrollRunService was built without an IFinalPayService, so it can't recompute a final-pay run.");
            var recomputed = await finalPay.RecomputeAsync(run, ct);

            run.Status = PayrollRunStatus.Draft;
            run.UpdatedAt = DateTime.UtcNow;
            await _runRepo.ReplaceEntriesAsync(run, recomputed, ct);
            return;
        }

        await EnsureNoOneHasLeftAsync(run, ct);

        // Recompute against current rates/settings, but from the attendance SNAPSHOT taken when
        // the run was created - never from the bridge. Re-deriving here would let a punch edited
        // after the fact change what someone was already told they would be paid.
        //
        // OvertimeHours and HolidayDays are deliberately passed as null overrides: the snapshot
        // below already carries them, split into the parts the entry's roll-up columns collapse
        // (see FromSnapshot). Passing the collapsed roll-ups as overrides would repay every
        // rest-day hour and special-holiday day at the ordinary rate.
        var employeeInputs = run.Employees
            .Select(e => new PayrollRunEmployeeInput(
                e.EmployeeId, e.DaysWorked, OvertimeHours: null, HolidayDays: null, e.IncludeThirteenthMonth))
            .ToList();

        var snapshots = new Dictionary<Guid, PayrollAttendanceInput>();
        foreach (var entry in run.Employees)
            snapshots[entry.EmployeeId] = FromSnapshot(entry);

        var entries = await ComputeEntriesAsync(run, employeeInputs, snapshots, ct);

        // The figures an approver would be asked to sign off on have changed, so any submission
        // no longer stands and the run goes back to draft to be resubmitted.
        run.Status = PayrollRunStatus.Draft;
        run.UpdatedAt = DateTime.UtcNow;

        await _runRepo.ReplaceEntriesAsync(run, entries, ct);
    }

    /// <summary>
    /// Approves a run, freezing the figures it holds: this is the gate between computing and
    /// paying. ComputeAsync refuses to run once a run is Approved, so approving a run locks in
    /// its numbers against any further recompute, and MarkPaidAsync then retires loan balances
    /// against exactly what was approved here.
    /// </summary>
    public async Task ApproveAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await _runRepo.GetWithEntriesAsync(runId, ct)
            ?? throw new KeyNotFoundException($"Payroll run {runId} not found.");

        if (run.Status is not (PayrollRunStatus.Draft or PayrollRunStatus.Processing or PayrollRunStatus.ForApproval))
            throw new DomainException("Only draft, processing or for-approval payroll runs can be approved.");

        if (run.RunType == PayrollRunType.Regular)
            await EnsureNoOneHasLeftAsync(run, ct);

        run.Status = PayrollRunStatus.Approved;
        run.UpdatedAt = DateTime.UtcNow;

        await _runRepo.UpdateAsync(run, ct);
    }

    public async Task MarkPaidAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await _runRepo.GetWithEntriesAsync(runId, ct)
            ?? throw new KeyNotFoundException($"Payroll run {runId} not found.");

        if (run.Status != PayrollRunStatus.Approved)
            throw new DomainException("Only approved payroll runs can be marked as paid.");

        if (run.RunType == PayrollRunType.FinalPay)
            await EnsureClearanceCompleteAsync(run, ct);
        else
            await EnsureNoOneHasLeftAsync(run, ct);

        var loanIds = run.Employees
            .SelectMany(e => e.LoanDeductionLines)
            .Select(l => l.EmployeeLoanId)
            .Distinct()
            .ToList();

        var loans = loanIds.Count == 0
            ? []
            : await _loanRepo.GetByIdsAsync(loanIds, ct);
        var loansById = loans.ToDictionary(l => l.Id);

        // Retire balances from what was actually withheld in this run (each line's Amount), not
        // by re-deriving the instalment from EmployeeLoan.MonthlyDeduction - the schedule may
        // have changed, or the deduction may have been capped, since the run was computed.
        foreach (var line in run.Employees.SelectMany(e => e.LoanDeductionLines))
        {
            if (!loansById.TryGetValue(line.EmployeeLoanId, out var loan))
                continue;

            loan.RemainingBalance = Math.Max(0m, loan.RemainingBalance - line.Amount);
            if (loan.RemainingBalance <= 0m)
                loan.IsActive = false;
        }

        run.Status = PayrollRunStatus.Paid;
        run.UpdatedAt = DateTime.UtcNow;

        if (loans.Count > 0)
            await _loanRepo.UpdateRangeAsync(loans, ct);
        await _runRepo.UpdateAsync(run, ct);
    }

    public async Task<PayrollRunDto> RemoveEmployeeAsync(Guid runId, Guid employeeId, CancellationToken ct = default)
    {
        var run = await _runRepo.GetWithEntriesAsync(runId, ct)
            ?? throw new KeyNotFoundException($"Payroll run {runId} not found.");

        // A final pay is built for its one employee from their separation.
        if (run.RunType == PayrollRunType.FinalPay)
            throw new DomainException("A final-pay run's employee can't be removed.");

        // A paid run has already retired loan balances. An approved one can still lose someone -
        // it has to, when they are separated after approval and the run can't otherwise be paid -
        // and goes back to Draft below to be approved again.
        if (run.Status == PayrollRunStatus.Paid)
            throw new DomainException("A paid payroll run can't be changed.");

        var entry = run.Employees.FirstOrDefault(e => e.EmployeeId == employeeId)
            ?? throw new KeyNotFoundException($"Employee {employeeId} is not on payroll run {run.RunNumber}.");
        if (run.Employees.Count == 1)
            throw new DomainException("A payroll run needs at least one employee.");

        // The stored count of employees without a shift schedule keeps no names, so the bridge is
        // asked whether this one was among them - the same question that produced the count.
        if (run.EmployeesMissingAttendance > 0)
        {
            var bridged = await _attendanceBridge.BuildAsync([employeeId], run.PeriodStart, run.PeriodEnd, ct);
            if (bridged.EmployeesWithoutSchedule.Contains(employeeId))
                run.EmployeesMissingAttendance--;
        }

        // The run's totals change, so any submission or approval no longer stands - as on a
        // recompute.
        run.Status = PayrollRunStatus.Draft;
        run.UpdatedAt = DateTime.UtcNow;
        await _runRepo.RemoveEntryAsync(run, entry, ct);

        return ToDto(run);
    }

    public async Task<PayrollRunDto?> GetAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await _runRepo.GetWithEntriesAsync(runId, ct);
        return run is null ? null : ToDto(run);
    }

    public async Task<PagedResult<PayrollRunSummaryDto>> GetPagedAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        var (items, total) = await _runRepo.GetPagedAsync(page, pageSize, ct);
        return PagedResult<PayrollRunSummaryDto>.Create(
            items.Select(ToSummaryDto).ToList(), total, page, pageSize);
    }

    /// <summary>
    /// A final pay is released only once the separation's clearance is complete: every item
    /// cleared, and at least one item to clear.
    /// </summary>
    private async Task EnsureClearanceCompleteAsync(PayrollRun run, CancellationToken ct)
    {
        var separationId = run.FinalPayInputs?.SeparationId
            ?? throw new InvalidOperationException($"Final-pay run {run.RunNumber} has no final-pay inputs.");
        var separation = await _separations.GetAsync(separationId, ct)
            ?? throw new KeyNotFoundException($"Separation {separationId} not found.");

        if (separation.ClearanceComplete)
            return;
        if (separation.ClearanceItems.Count == 0)
            throw new DomainException("Add the separation's clearance items and clear them before paying final pay.");

        var outstanding = separation.ClearanceItems
            .Where(i => i.ClearedAt is null)
            .OrderBy(i => i.SortOrder)
            .Select(i => i.Name);
        throw new DomainException($"Clear {string.Join(", ", outstanding)} before paying final pay.");
    }

    private Task EnsureNoOneHasLeftAsync(PayrollRun run, CancellationToken ct)
        => EnsureNoOneHasLeftAsync(run.Employees.Select(e => e.EmployeeId).ToList(), run.PeriodStart, run.PeriodEnd, ct);

    /// <summary>
    /// Keeps people who have left off a regular run: anyone whose last working day is before the
    /// period (their pay for it, if any, goes in their final pay), and anyone whose final pay has
    /// been started, in any status and whatever the two periods. The final pay's 13th month, tax
    /// settle and contributions all take it as the employee's last pay, so a regular run paid
    /// after it would fall outside all three - a Dec 1-15 run carrying the 13th month, say,
    /// would pay it a second time. Someone leaving during the period, with no final pay yet,
    /// stays - the run still pays them up to that day.
    /// </summary>
    private async Task EnsureNoOneHasLeftAsync(IReadOnlyList<Guid> employeeIds, DateOnly periodStart,
        DateOnly periodEnd, CancellationToken ct)
    {
        var separations = (await _separations.GetForEmployeesAsync(employeeIds, ct) ?? [])
            .ToDictionary(s => s.EmployeeId);

        foreach (var employeeId in employeeIds)
        {
            if (!separations.TryGetValue(employeeId, out var separation))
                continue;

            var name = separation.Employee.FullName;
            if (separation.LastWorkingDay < periodStart)
                throw new DomainException(string.Create(CultureInfo.InvariantCulture,
                    $"{name} left on {separation.LastWorkingDay:MMM d, yyyy}; take them off this payroll - their pay goes in final pay."));

            if (separation.FinalPayRunId is not null || separation.FinalPayRun is not null)
                throw new DomainException(
                    $"{name}'s final pay has been started; the rest of their pay goes there. Take them off this payroll.");
        }
    }

    /// <summary>
    /// Computes an entry per employee. Shared by CreateAsync and ComputeAsync so the two can
    /// never drift on rates or the rate basis.
    /// </summary>
    /// <param name="snapshots">
    /// The attendance to compute from, keyed by employee, when the caller already holds it - a
    /// recompute, which must reproduce the run rather than re-read attendance. Null asks the
    /// bridge to derive it, which is what creating a run does.
    /// </param>
    private async Task<List<PayrollRunEmployee>> ComputeEntriesAsync(
        PayrollRun run,
        IReadOnlyList<PayrollRunEmployeeInput> employees,
        IReadOnlyDictionary<Guid, PayrollAttendanceInput>? snapshots,
        CancellationToken ct)
    {
        var settings = await _settingsRepo.GetDefaultAsync(ct);
        var rates = ToRates(settings);
        decimal dailyRateFactor = settings?.DailyRateFactor ?? 365m;

        var employeeIds = employees.Select(e => e.EmployeeId).Distinct().ToList();

        var compensations = await _compensationRepo.GetByEmployeeIdsAsync(employeeIds, ct);
        var allowancesByEmployee = (await _allowanceRepo.GetByEmployeeIdsAsync(employeeIds, ct))
            .ToLookup(a => a.EmployeeId);
        var loansByEmployee = (await _loanRepo.GetByEmployeeIdsAsync(employeeIds, ct))
            .Where(l => l.IsActive)
            .ToLookup(l => l.EmployeeId);

        // Scheduled days for the period when a caller does not override DaysWorked. Absences are
        // not taken from here - they come off the derived AbsenceDays below - so this stays the
        // nominal period length.
        decimal defaultDaysInPeriod = run.Frequency == PayFrequency.SemiMonthly ? 11m : 22m;

        var attendanceByEmployee = snapshots ?? await DeriveAttendanceAsync(run, employeeIds, ct);

        // The 13th month is one twelfth of the basic earned in the pay year, less any part of it
        // already paid - which also uses up the 90,000 exemption first. Only Paid runs count,
        // keyed on PayDate - the same basis BIR Form 2316 totals the year on - and this run
        // itself is never Paid while it can still be computed. Looked up only when someone in
        // the run is receiving a 13th month, since nobody else's pay depends on it.
        var earlierInYear = new Dictionary<Guid, (decimal Basic, decimal ThirteenthMonth)>();
        var ineligible = new HashSet<Guid>();
        if (employees.Any(e => e.IncludeThirteenthMonth))
        {
            var paidRuns = await _runRepo.GetPaidRunsInYearAsync(run.PayDate.Year, ct) ?? [];
            earlierInYear = paidRuns
                .Where(r => r.Id != run.Id)
                .SelectMany(r => r.Employees)
                .Where(e => employeeIds.Contains(e.EmployeeId))
                .GroupBy(e => e.EmployeeId)
                .ToDictionary(g => g.Key, g => (g.Sum(e => e.RegularPay), g.Sum(e => e.ThirteenthMonth)));

            var people = await _employeeRepo.GetByIdsAsync(employeeIds, ct) ?? [];
            ineligible = people.Where(p => !p.Is13thMonthEligible).Select(p => p.Id).ToHashSet();
        }

        var entries = new List<PayrollRunEmployee>();
        foreach (var employee in employees)
        {
            var compensation = compensations.FirstOrDefault(c => c.EmployeeId == employee.EmployeeId)
                ?? throw new DomainException($"Employee {employee.EmployeeId} has no compensation record.");

            // Populated from their own repositories - EmployeeCompensation.Allowances and
            // .Loans are not mapped in EF (see that entity's remarks).
            compensation.Allowances = allowancesByEmployee[employee.EmployeeId].ToList();
            compensation.Loans = loansByEmployee[employee.EmployeeId].ToList();

            // An employee the bridge never saw simply accumulates zeros; missing attendance is
            // not an error (see IPayrollAttendanceBridge).
            var attendance = ApplyOverrides(
                attendanceByEmployee.TryGetValue(employee.EmployeeId, out var derived)
                    ? derived
                    : new PayrollAttendanceInput(),
                employee);

            // overtimeHours/holidayDays are left at their defaults: Compute reads them only when
            // attendance is null, and any caller override has already been folded into the
            // attendance record above so that the snapshot records what was actually paid.
            var entry = _computationService.Compute(
                compensation, run,
                daysWorked: employee.DaysWorked ?? defaultDaysInPeriod,
                includeThirteenthMonth: employee.IncludeThirteenthMonth,
                rates: rates, attendance: attendance, dailyRateFactor: dailyRateFactor,
                thirteenthMonthPaidEarlierInYear:
                    earlierInYear.GetValueOrDefault(employee.EmployeeId).ThirteenthMonth,
                basicEarnedEarlierInYear: earlierInYear.GetValueOrDefault(employee.EmployeeId).Basic,
                isThirteenthMonthEligible: !ineligible.Contains(employee.EmployeeId));

            SnapshotAttendance(entry, attendance);

            entries.Add(entry);
        }

        return entries;
    }

    /// <summary>
    /// Derives the period's attendance for every employee in the run and records how many of them
    /// could not be scheduled at all - a condition operations has to see before anyone is paid,
    /// because those employees are treated as fully present rather than absent.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, PayrollAttendanceInput>> DeriveAttendanceAsync(
        PayrollRun run, IReadOnlyList<Guid> employeeIds, CancellationToken ct)
    {
        var bridged = await _attendanceBridge.BuildAsync(employeeIds, run.PeriodStart, run.PeriodEnd, ct);

        run.EmployeesMissingAttendance = bridged.EmployeesWithoutSchedule.Count;

        if (bridged.EmployeesWithoutSchedule.Count > 0)
        {
            _logger.LogWarning(
                "Payroll run {RunNumber} for {PeriodStart:yyyy-MM-dd}..{PeriodEnd:yyyy-MM-dd}: " +
                "{WithoutSchedule} of {EmployeeCount} employees had no shift schedule for the period. " +
                "They were treated as fully present and deducted no absence.",
                run.RunNumber, run.PeriodStart, run.PeriodEnd,
                bridged.EmployeesWithoutSchedule.Count, employeeIds.Count);
        }

        return bridged.Inputs;
    }

    /// <summary>
    /// Folds a caller's manual corrections into the derived attendance, so that one record is
    /// both what the figures are computed from and what gets snapshotted. Null leaves the derived
    /// value alone; see <see cref="PayrollRunEmployeeInput"/> for why an override collapses the
    /// split it cannot express.
    /// </summary>
    private static PayrollAttendanceInput ApplyOverrides(
        PayrollAttendanceInput derived, PayrollRunEmployeeInput input)
    {
        if (input.OvertimeHours is null && input.HolidayDays is null)
            return derived;

        // The override replaces the breakdown's hours or days wholesale, so it is applied to the
        // breakdown itself - the collapsed totals alone would be ignored once one exists.
        var days = derived.ResolvePremiumDays().ToList();

        if (input.OvertimeHours is decimal overtimeHours)
        {
            days = days.Select(d => d with { Hours = 0m, OvertimeHours = 0m }).ToList();
            days.Add(new PremiumDayInput(WorkDayType.Ordinary, OvertimeHours: overtimeHours));
            derived = derived with { OvertimeHours = overtimeHours, RestDayOTHours = 0m };
        }

        if (input.HolidayDays is decimal holidayDays)
        {
            days = days.Select(d => d with { Days = 0m }).ToList();
            days.Add(new PremiumDayInput(WorkDayType.RegularHoliday, Days: holidayDays));
            derived = derived with { HolidayRegularDays = holidayDays, HolidaySpecialDays = 0m };
        }

        return derived with { PremiumDays = Merge(days) };
    }

    /// <summary>One row per kind of day, dropping any left with nothing on it.</summary>
    private static List<PremiumDayInput> Merge(IEnumerable<PremiumDayInput> days) => days
        .GroupBy(d => d.DayType)
        .Select(g => new PremiumDayInput(g.Key,
            g.Sum(d => d.Days), g.Sum(d => d.Hours), g.Sum(d => d.OvertimeHours), g.Sum(d => d.NightDiffHours)))
        .Where(d => d.Days != 0m || d.Hours != 0m || d.OvertimeHours != 0m || d.NightDiffHours != 0m)
        .ToList();

    /// <summary>
    /// Snapshots onto the entry the attendance its figures were struck from. The entry's own
    /// OvertimeHours and HolidayDays are roll-ups Compute wrote; these seven are the parts, and a
    /// recompute is rebuilt from them (<see cref="FromSnapshot"/>) rather than from today's punches.
    /// </summary>
    internal static void SnapshotAttendance(PayrollRunEmployee entry, PayrollAttendanceInput attendance)
    {
        entry.AbsenceDays        = attendance.AbsenceDays;
        entry.LateMinutes        = attendance.LateMinutes;
        entry.UndertimeMinutes   = attendance.UndertimeMinutes;
        entry.NightDiffHours     = attendance.NightDiffHours;
        entry.RestDayOTHours     = attendance.RestDayOTHours;
        entry.HolidayRegularDays = attendance.HolidayRegularDays;
        entry.HolidaySpecialDays = attendance.HolidaySpecialDays;
    }

    /// <summary>
    /// Rebuilds the attendance a stored entry was computed from, so a recompute reproduces it
    /// exactly. An entry saved before the per-day breakdown existed has no premium days, and
    /// its totals are repriced the way they were then.
    /// </summary>
    internal static PayrollAttendanceInput FromSnapshot(PayrollRunEmployee entry) => new()
    {
        // entry.OvertimeHours is the TOTAL that Compute wrote back; the input wants the
        // ordinary part only, or every rest-day hour reprices from 1.69x down to 1.25x.
        OvertimeHours      = entry.OvertimeHours - entry.RestDayOTHours,
        RestDayOTHours     = entry.RestDayOTHours,
        AbsenceDays        = entry.AbsenceDays,
        LateMinutes        = entry.LateMinutes,
        UndertimeMinutes   = entry.UndertimeMinutes,
        NightDiffHours     = entry.NightDiffHours,
        HolidayRegularDays = entry.HolidayRegularDays,
        HolidaySpecialDays = entry.HolidaySpecialDays,
        PremiumDays        = entry.PremiumDays
            .Select(d => new PremiumDayInput(d.DayType, d.Days, d.Hours, d.OvertimeHours, d.NightDiffHours))
            .ToList()
    };

    internal static ContributionRates ToRates(PayrollSettings? settings) => settings is null
        ? new ContributionRates()
        : new ContributionRates
        {
            PhilHealthRate = settings.PhilHealthRate,
            PhilHealthMinShare = settings.PhilHealthMinShare,
            PhilHealthMaxShare = settings.PhilHealthMaxShare,
            PagIbigEmployeeRate = settings.PagIbigEmployeeRate,
            PagIbigLowEmployeeRate = settings.PagIbigLowEmployeeRate,
            PagIbigLowRateThreshold = settings.PagIbigLowRateThreshold,
            PagIbigEmployerRate = settings.PagIbigEmployerRate,
            PagIbigMaxFundSalary = settings.PagIbigMaxFundSalary,
            SSSEmployeeRate = settings.SSSEmployeeRate,
            SSSEmployerRate = settings.SSSEmployerRate
        };

    private static PayrollRunDto ToDto(PayrollRun run) => new(
        run.Id, run.RunNumber, run.PeriodLabel,
        run.PeriodStart, run.PeriodEnd, run.PayDate,
        run.Frequency, run.Status,
        run.EmployeeCount, run.TotalGrossPay, run.TotalDeductions, run.TotalNetPay,
        run.CreatedAt, run.AttendancePeriodId, run.EmployeesMissingAttendance,
        run.Employees.Select(ToEmployeeDto).ToList(), run.RunType);

    private static PayrollRunSummaryDto ToSummaryDto(PayrollRun run) => new(
        run.Id, run.RunNumber, run.PeriodLabel,
        run.PeriodStart, run.PeriodEnd, run.PayDate,
        run.Frequency, run.Status,
        run.EmployeeCount, run.TotalGrossPay, run.TotalNetPay,
        run.EmployeesMissingAttendance, run.CreatedAt, run.RunType);

    private static PayrollRunEmployeeDto ToEmployeeDto(PayrollRunEmployee e) => new(
        e.Id, e.EmployeeId, e.Employee?.FullName ?? string.Empty,
        e.Employee?.EmployeeNumber ?? string.Empty, e.DaysWorked,
        e.GrossPay, e.TotalDeductions, e.NetPay,
        e.RegularPay, e.OvertimePay, e.HolidayPay, e.NightDiffPay,
        e.TaxableAllowances, e.NonTaxableAllowances, e.ThirteenthMonth,
        e.AbsenceDeduction, e.TardinessDeduction,
        e.SSSEmployee, e.SSSEmployer, e.PhilHealthEmployee, e.PhilHealthEmployer,
        e.PagIbigEmployee, e.PagIbigEmployer, e.WithholdingTax, e.LoanDeductions, e.OtherDeductions,
        e.LeaveConversionPay, e.LeaveConversionNonTaxable, e.SeparationPay, e.RetirementPay, e.FinalPayNonTaxable);
}
