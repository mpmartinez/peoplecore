using System.Globalization;
using Microsoft.Extensions.Logging;
using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.FinalPay;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Application.Payroll.Maternity;
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
    private readonly IYearEndLeaveConversion? _yearEndLeave;
    private readonly TimeProvider _clock;
    private readonly IMaternityPayCalculator? _maternityPay;

    /// <param name="finalPay">
    /// Recomputes final-pay runs, which are built from a separation rather than from a list of
    /// employees. Optional so callers that never see a final-pay run needn't supply one; computing
    /// a final-pay run without it is refused.
    /// </param>
    /// <param name="yearEndLeave">
    /// Works out each employee's year-end convertible leave, for a December run that includes the
    /// conversion. Optional so callers that never see such a run needn't supply one;
    /// computing or paying one without it is refused.
    /// </param>
    /// <param name="clock">
    /// When a run changes - computed, approved, an employee removed, or paid - stamped on the run,
    /// and on the leave balances Mark Paid draws down, as final pay stamps them. Defaults to the
    /// system clock.
    /// </param>
    /// <param name="maternityPay">
    /// Works out each employee's maternity advance and offset on a regular run, the run's maternity
    /// warnings, and the claims Mark Paid settles. Optional so callers that never see maternity pay
    /// needn't supply one: without it a run has no maternity figures or warnings, and advancing a
    /// benefit is refused.
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
        IFinalPayService? finalPay = null,
        IYearEndLeaveConversion? yearEndLeave = null,
        TimeProvider? clock = null,
        IMaternityPayCalculator? maternityPay = null)
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
        _yearEndLeave = yearEndLeave;
        _clock = clock ?? TimeProvider.System;
        _maternityPay = maternityPay;
    }

    public async Task<PayrollRunDto> CreateAsync(CreatePayrollRunRequest request, CancellationToken ct = default)
    {
        PayrollRunRequestValidator.Validate(request);
        await EnsureNoOneHasLeftAsync(request.Employees.Select(e => e.EmployeeId).ToList(),
            request.PeriodStart, request.PeriodEnd, ct);

        // One past the year's highest PAY- number, not a count: a discarded run leaves a gap a
        // count would fill with a number still in use.
        var year = request.PeriodStart.Year;
        var sequence = await _runRepo.GetLastRegularSequenceAsync(year, ct) + 1;

        var run = new PayrollRun
        {
            RunNumber = $"PAY-{year}-{sequence:D3}",
            PeriodStart = request.PeriodStart,
            PeriodEnd = request.PeriodEnd,
            PayDate = request.PayDate,
            Frequency = request.Frequency,
            Status = PayrollRunStatus.Draft,
            AttendancePeriodId = request.AttendancePeriodId,
            IncludesLeaveConversion = request.IncludeLeaveConversion
        };
        if (run.IncludesLeaveConversion)
            EnsureLeaveConversionFits(run.RunType, run.PeriodEnd);

        // No snapshots to honour on a brand new run, so the attendance is derived.
        run.Employees = await ComputeEntriesAsync(run, request.Employees, snapshots: null, ct, creating: true);

        await _runRepo.AddWithEntriesAsync(run, ct);

        // PayrollRunEmployee.Employee is populated by EF fixup only when a run is reloaded via
        // GetWithEntriesAsync's .Include(...).ThenInclude(e => e.Employee) - Compute never sets
        // it, since it works from the employee's compensation, not the person (see that
        // property's remarks). Reloading here, rather than mapping names from data already in
        // hand, guarantees the create response reports the exact same names GET would - the two
        // can never drift onto two different lookups.
        var saved = await _runRepo.GetWithEntriesAsync(run.Id, ct)
            ?? throw new InvalidOperationException($"Payroll run {run.Id} was not found immediately after being saved.");

        return await ToDtoAsync(saved, ct);
    }

    public async Task ComputeAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await _runRepo.GetWithEntriesAsync(runId, ct)
            ?? throw new KeyNotFoundException($"Payroll run {runId} not found.");

        // A committed run is settled: an approver has signed off on these figures, or - worse,
        // for a paid run - they have already been used to retire loan balances. Recomputing
        // either would silently change what someone was, or is about to be, paid. A final pay is
        // the one exception to the first: it can be approved before clearance is complete, and
        // clearance is where deductions such as an unreturned laptop come up, so an approved
        // final pay can still be recomputed - and goes back to Draft below, to be approved again.
        // A regular run that converts leave is another: its leave can change after approval (a
        // request filed, rejected or cancelled), Mark Paid then refuses it, and a recompute - back
        // to Draft, to be approved again - is the way to pay it. So is one that includes the 13th
        // month: another run can pay some of it after approval, which Mark Paid refuses the same way.
        // And so is one whose deferred maternity contributions another run's payment changed since
        // it was computed, which Mark Paid refuses too.
        if (run.RunType == PayrollRunType.FinalPay && run.Status == PayrollRunStatus.Paid)
            throw new DomainException("A paid final pay can't be recomputed.");
        if (run.RunType != PayrollRunType.FinalPay
            && (run.Status == PayrollRunStatus.Paid
                || (run.Status == PayrollRunStatus.Approved && !run.IncludesLeaveConversion && !IncludesThirteenthMonth(run)
                    && await EntryWithStaleDeferredContributionsAsync(run, ct) is null)))
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
            run.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
            await _runRepo.ReplaceEntriesAsync(run, recomputed, ct);
            return;
        }

        await RecomputeRegularAsync(run, ct);
    }

    public async Task<PayrollRunDto> SetLeaveConversionAsync(Guid runId, bool include, CancellationToken ct = default)
    {
        var run = await _runRepo.GetWithEntriesAsync(runId, ct)
            ?? throw new KeyNotFoundException($"Payroll run {runId} not found.");

        if (run.Status == PayrollRunStatus.Paid)
            throw new DomainException("A paid payroll run can't be changed.");
        // A final pay converts the employee's leave its own way: turning the year-end conversion on
        // is the December rule's to refuse, and there's nothing here to turn off.
        if (run.RunType != PayrollRunType.Regular)
            throw new DomainException(include
                ? LeaveConversionIsForDecember
                : "A final pay's leave conversion can't be changed here.");
        // A request that changes nothing would still recompute the run - and so could recompute
        // an approved run without the conversion, which ComputeAsync refuses.
        if (include == run.IncludesLeaveConversion)
            throw new DomainException(include
                ? "This payroll already converts unused leave."
                : "This payroll already doesn't convert unused leave.");
        if (include)
            EnsureLeaveConversionFits(run.RunType, run.PeriodEnd);

        // The run's figures change with it, so it's recomputed - and, like any recompute, goes
        // back to Draft: an approval or submission of the old figures no longer stands. A refused
        // recompute (someone's leave already converted this year) saves nothing, the flag included.
        run.IncludesLeaveConversion = include;
        await RecomputeRegularAsync(run, ct);

        var saved = await _runRepo.GetWithEntriesAsync(run.Id, ct)
            ?? throw new InvalidOperationException($"Payroll run {run.Id} was not found immediately after being saved.");
        return await ToDtoAsync(saved, ct);
    }

    public async Task<PayrollRunDto> SetThirteenthMonthAsync(Guid runId, bool include, CancellationToken ct = default)
    {
        var run = await _runRepo.GetWithEntriesAsync(runId, ct)
            ?? throw new KeyNotFoundException($"Payroll run {runId} not found.");

        if (run.Status == PayrollRunStatus.Paid)
            throw new DomainException("A paid payroll run can't be changed.");
        // A final pay works out the employee's 13th month from their separation, its own way.
        if (run.RunType != PayrollRunType.Regular)
            throw new DomainException("A final pay's 13th month can't be changed here.");
        // A request that changes nothing would still recompute the run - and so could recompute an
        // approved run, which ComputeAsync refuses. A run where only some employees have it is a
        // change either way.
        if (run.Employees.All(e => e.IncludeThirteenthMonth == include))
            throw new DomainException(include
                ? "This payroll already includes the 13th month."
                : "This payroll already leaves out the 13th month.");

        // Allowed in any month: an advance pays part of it early, and the 13th month on a later run
        // nets out what was paid earlier in the year. The figures change, so the run is recomputed
        // and, like any recompute, goes back to Draft. The stored entries aren't touched until the
        // recompute succeeds: a refused one leaves the run as it was.
        await RecomputeRegularAsync(run, ct, includeThirteenthMonth: include);

        var saved = await _runRepo.GetWithEntriesAsync(run.Id, ct)
            ?? throw new InvalidOperationException($"Payroll run {run.Id} was not found immediately after being saved.");
        return await ToDtoAsync(saved, ct);
    }

    /// <summary>
    /// Recomputes a regular run in place from its stored inputs and sends it back to Draft.
    /// Shared by ComputeAsync, SetLeaveConversionAsync and SetThirteenthMonthAsync.
    /// </summary>
    /// <param name="includeThirteenthMonth">
    /// Every employee's 13th month setting for the recompute, or null to keep each entry's own.
    /// </param>
    private async Task RecomputeRegularAsync(PayrollRun run, CancellationToken ct, bool? includeThirteenthMonth = null)
    {
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
                e.EmployeeId, e.DaysWorked, OvertimeHours: null, HolidayDays: null,
                includeThirteenthMonth ?? e.IncludeThirteenthMonth, e.AdvanceMaternityBenefit))
            .ToList();

        var snapshots = new Dictionary<Guid, PayrollAttendanceInput>();
        foreach (var entry in run.Employees)
            snapshots[entry.EmployeeId] = FromSnapshot(entry);

        var entries = await ComputeEntriesAsync(run, employeeInputs, snapshots, ct);

        // The figures an approver would be asked to sign off on have changed, so any submission
        // no longer stands and the run goes back to draft to be resubmitted.
        run.Status = PayrollRunStatus.Draft;
        run.UpdatedAt = _clock.GetUtcNow().UtcDateTime;

        await _runRepo.ReplaceEntriesAsync(run, entries, ct);
    }

    /// <summary>
    /// Approves a run, freezing the figures it holds: this is the gate between computing and
    /// paying. ComputeAsync refuses to run once a regular run is Approved, so approving it locks in
    /// its numbers against any further recompute, and MarkPaidAsync then retires loan balances
    /// against exactly what was approved here. A final pay can still be recomputed or changed once
    /// approved, but that sends it back to Draft, so it is paid only as approved all the same - and
    /// so can a regular run with the year-end leave conversion, whose leave can change after it's
    /// approved, and one that includes the 13th month, some of which another run can pay after
    /// it's approved.
    /// </summary>
    public async Task ApproveAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await _runRepo.GetWithEntriesAsync(runId, ct)
            ?? throw new KeyNotFoundException($"Payroll run {runId} not found.");

        if (run.Status is not (PayrollRunStatus.Draft or PayrollRunStatus.Processing or PayrollRunStatus.ForApproval))
            throw new DomainException("Only draft, processing or for-approval payroll runs can be approved.");

        if (run.RunType == PayrollRunType.Regular)
            await EnsureNoOneHasLeftAsync(run, ct);

        // The check Mark Paid makes, made here too, so leave that changed since the run was
        // computed is caught before anyone approves figures that can't be paid.
        if (run.RunType == PayrollRunType.Regular && run.IncludesLeaveConversion)
            await YearEndLeavePaidOutAsync(run, ct);
        // Likewise a maternity claim the run advances, while a recompute can still put it right: an
        // approved regular run can't be recomputed. A final pay's maternity is checked the same way.
        if (_maternityPay is not null)
        {
            await _maternityPay.EnsureAdvancesCurrentAsync(run, ct);
            // Maternity days no claim with an allowance covers were paid as ordinary, taxable salary
            // with nothing netted; they can't be approved that way unless the employer is exempt.
            var settings = await _settingsRepo.GetDefaultAsync(ct);
            await _maternityPay.EnsureClaimsSetUpAsync(run, settings?.ExemptFromMaternityDifferential ?? false, ct);
        }

        run.Status = PayrollRunStatus.Approved;
        run.UpdatedAt = _clock.GetUtcNow().UtcDateTime;

        await _runRepo.UpdateAsync(run, ct);
    }

    public async Task MarkPaidAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await _runRepo.GetWithEntriesAsync(runId, ct)
            ?? throw new KeyNotFoundException($"Payroll run {runId} not found.");

        if (run.Status != PayrollRunStatus.Approved)
            throw new DomainException("Only approved payroll runs can be marked as paid.");

        // A final pay also pays out the employee's convertible leave, and so does a December run
        // that includes the year-end conversion: what each converted is checked against the
        // balances now, before anything changes, and recorded as used below.
        IReadOnlyList<LeavePaidOut> leavePaidOut = [];
        IReadOnlyList<MaternityClaim> maternityClaims = [];
        if (run.RunType == PayrollRunType.FinalPay)
        {
            await EnsureClearanceCompleteAsync(run, ct);
            var finalPay = _finalPay ?? throw new InvalidOperationException(
                "PayrollRunService was built without an IFinalPayService, so it can't pay a final-pay run.");
            leavePaidOut = await finalPay.LeavePaidOutAsync(run, ct);
            // A final pay can advance the maternity benefit too; its claim is settled the same way.
            maternityClaims = await SettleMaternityAdvancesAsync(run, ct);
        }
        else
        {
            await EnsureNoOneHasLeftAsync(run, ct);
            await EnsureThirteenthMonthNotPaidSinceAsync(run, ct);
            if (run.IncludesLeaveConversion)
                leavePaidOut = await YearEndLeavePaidOutAsync(run, ct);
            // The claims this run advances become Advanced, paid on this run's pay date. Discarding
            // the run instead never gets here, so its claims stay Draft.
            maternityClaims = await SettleMaternityAdvancesAsync(run, ct);
        }
        // Contributions deferred while maternity leave was covered by SSS: what each entry collects
        // must still match what the employee has outstanding.
        await EnsureDeferredContributionsCurrentAsync(run, ct);

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

        var now = _clock.GetUtcNow().UtcDateTime;
        var balances = LeavePayout.Apply(leavePaidOut, now);
        run.Status = PayrollRunStatus.Paid;
        run.UpdatedAt = now;

        // The run's status, the loans, the leave balances and the maternity claims go out in one
        // save, so they commit together or not at all.
        await _runRepo.SavePaidAsync(run, loans, balances, maternityClaims, ct);
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
        run.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
        await _runRepo.RemoveEntryAsync(run, entry, ct);

        return await ToDtoAsync(run, ct);
    }

    public async Task DiscardAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await _runRepo.GetWithEntriesAsync(runId, ct)
            ?? throw new KeyNotFoundException($"Payroll run {runId} not found.");

        // A final pay belongs to its separation, which links to it.
        if (run.RunType == PayrollRunType.FinalPay)
            throw new DomainException("A final pay can't be discarded here.");
        // A paid run has retired loan balances and drawn leave down; it's part of the record.
        if (run.Status == PayrollRunStatus.Paid)
            throw new DomainException("A paid payroll run can't be discarded.");

        // Nothing but the run changes: loans, leave balances and maternity claims only move at Mark
        // Paid, which this run never reached - a claim it would have advanced stays Draft, free for
        // another run to advance. Its entries, their loan deduction lines and premium days go with it.
        await _runRepo.DeleteAsync(run, ct);
    }

    public async Task<PayrollRunDto?> GetAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await _runRepo.GetWithEntriesAsync(runId, ct);
        return run is null ? null : await ToDtoAsync(run, ct);
    }

    public async Task<PayrollRunDto?> GetForPayslipAsync(Guid runId, CancellationToken ct = default)
    {
        var run = await _runRepo.GetWithEntriesAsync(runId, ct);
        return run is null ? null : await ToDtoAsync(run, ct, withWarnings: false);
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

    /// <summary>
    /// The days a run with the year-end conversion paid out, re-worked out now for every entry
    /// that converted any. They must still price - at the entry's own daily rate, with the pay
    /// year's de minimis days left now - to what the entry pays, split as it splits it: leave filed,
    /// rejected or cancelled since the run was computed would otherwise be recorded as paid out
    /// when it wasn't, or paid out without being recorded; and a conversion paid earlier in the
    /// year since then would move part of it from de minimis to taxable. Changes nothing.
    /// </summary>
    private async Task<IReadOnlyList<LeavePaidOut>> YearEndLeavePaidOutAsync(PayrollRun run, CancellationToken ct)
    {
        var converting = run.Employees.Where(e => e.LeaveConversionPay > 0m).ToList();
        if (converting.Count == 0)
            return [];

        var yearEndLeave = YearEndLeave();
        var earlierEntries = (await _runRepo.GetPaidRunsInYearAsync(run.PayDate.Year, ct) ?? [])
            .Where(r => r.Id != run.Id)
            .SelectMany(r => r.Employees)
            .ToLookup(e => e.EmployeeId);

        var paidOut = new List<LeavePaidOut>();
        foreach (var entry in converting)
        {
            var days = await yearEndLeave.DaysAsync(entry.EmployeeId, run.PeriodEnd.Year, ct);
            var deMinimisDaysLeft = LeavePayout.DeMinimisDaysLeft(earlierEntries[entry.EmployeeId]);
            if (!LeavePayout.StillPrices(days, entry, deMinimisDaysLeft))
            {
                var name = entry.Employee?.FullName
                    ?? (await _employeeRepo.GetByIdAsync(entry.EmployeeId, ct))?.FullName
                    ?? entry.EmployeeId.ToString();
                throw new DomainException(
                    $"{name}'s convertible leave has changed since this payroll was computed; recompute it before paying.");
            }
            paidOut.AddRange(days);
        }
        return paidOut;
    }

    /// <summary>
    /// Refuses a regular run whose 13th month was computed before more of it was paid elsewhere:
    /// for every entry that computed one, the 13th month the pay year's other Paid runs hold for
    /// the employee now must still be what the entry was netted of
    /// (<see cref="PayrollRunEmployee.ThirteenthMonthPaidEarlierInYear"/>). Paid runs can't be
    /// changed, so that total only grows; when it has, the entry would pay the difference twice.
    /// Entries computed before the figure was kept (null) aren't checked. Changes nothing.
    /// </summary>
    private async Task EnsureThirteenthMonthNotPaidSinceAsync(PayrollRun run, CancellationToken ct)
    {
        var computed = run.Employees.Where(e => e.ThirteenthMonthPaidEarlierInYear is not null).ToList();
        if (computed.Count == 0)
            return;

        var paidElsewhere = (await _runRepo.GetPaidRunsInYearAsync(run.PayDate.Year, ct) ?? [])
            .Where(r => r.Id != run.Id)
            .SelectMany(r => r.Employees.Select(e => (Run: r, Entry: e)))
            .ToLookup(x => x.Entry.EmployeeId);

        foreach (var entry in computed)
        {
            var paid = paidElsewhere[entry.EmployeeId].ToList();
            if (paid.Sum(x => x.Entry.ThirteenthMonth) <= entry.ThirteenthMonthPaidEarlierInYear)
                continue;

            // Which run paid it isn't recorded against the entry; the latest one marked Paid that
            // paid any 13th month is the likeliest, and is the one to look at.
            var latest = paid.Where(x => x.Entry.ThirteenthMonth > 0m).MaxBy(x => x.Run.UpdatedAt).Run;
            var name = entry.Employee?.FullName
                ?? (await _employeeRepo.GetByIdAsync(entry.EmployeeId, ct))?.FullName
                ?? entry.EmployeeId.ToString();
            throw new DomainException(
                $"{name}'s 13th month was paid on {latest.RunNumber} after this payroll was computed; recompute it before paying.");
        }
    }

    /// <summary>
    /// The 13th month goes only on a regular run paid in the year its period ends: it's due by
    /// Dec 24 (PD 851), and is worked out from the basic of the pay year. A Dec 16-31 run paid
    /// Jan 5 would work it out from the next year's basic - underpaying it - and count it as the
    /// next year's. Checked on create, on every recompute and when it's switched on.
    /// </summary>
    /// <param name="creating">
    /// True when the run is being created, whose pay date can still be changed; a recompute or the
    /// toggle can't change an existing run's pay date, so there the 13th month has to be left out.
    /// </param>
    private static void EnsureThirteenthMonthPaidInItsYear(PayrollRun run, bool creating)
    {
        if (run.PayDate.Year == run.PeriodEnd.Year)
            return;
        var year = run.PeriodEnd.Year;
        throw new DomainException(creating
            ? string.Create(CultureInfo.InvariantCulture,
                $"The {year} 13th month must be paid by Dec 24, {year}; give this payroll a pay date in {year}.")
            : string.Create(CultureInfo.InvariantCulture,
                $"The {year} 13th month must be paid by Dec 24, {year}; leave it out of this payroll and include it on one paid in {year}."));
    }

    /// <summary>
    /// Refuses a run whose collection of deferred contributions no longer holds: another run was
    /// paid since this one was computed, so what the employee has outstanding changed, and the entry
    /// would collect too much (what that run already collected) or too little (what it deferred).
    /// Changes nothing. See <see cref="EntryWithStaleDeferredContributionsAsync"/>.
    /// </summary>
    private async Task EnsureDeferredContributionsCurrentAsync(PayrollRun run, CancellationToken ct)
    {
        var stale = await EntryWithStaleDeferredContributionsAsync(run, ct);
        if (stale is null)
            return;
        var name = stale.Employee?.FullName
            ?? (await _employeeRepo.GetByIdAsync(stale.EmployeeId, ct))?.FullName
            ?? stale.EmployeeId.ToString();
        throw new DomainException(
            $"{name}'s deferred contributions have changed since this payroll was computed; recompute it before paying.");
    }

    /// <summary>
    /// The first entry whose collection of deferred contributions no longer matches what the
    /// employee has outstanding now, or null. Nothing extra is stored for this: an entry collects
    /// <see cref="PayrollComputationService.DeferredContributionsToCollect"/> of what was outstanding
    /// when it was computed and the cash it had left, and that cash is still on the entry (its net
    /// pay plus what it collected). So the collection is worked out again against the outstanding
    /// amount now; when it differs, a Paid run has deferred or collected some since. A change that
    /// wouldn't alter the collection - the entry had no cash for more anyway - isn't one.
    /// </summary>
    private async Task<PayrollRunEmployee?> EntryWithStaleDeferredContributionsAsync(PayrollRun run, CancellationToken ct)
    {
        if (run.Employees.Count == 0)
            return null;
        var outstanding = await DeferredContributionsOutstandingAsync(run.Employees.Select(e => e.EmployeeId).ToList(), ct);
        return run.Employees.FirstOrDefault(e =>
            PayrollComputationService.DeferredContributionsToCollect(
                outstanding.GetValueOrDefault(e.EmployeeId), e.NetPay + e.DeferredContributionsCollected)
            != e.DeferredContributionsCollected);
    }

    private async Task<Dictionary<Guid, decimal>> DeferredContributionsOutstandingAsync(IReadOnlyCollection<Guid> employeeIds,
        CancellationToken ct)
        => (await _runRepo.GetDeferredContributionsOutstandingAsync(employeeIds, ct) ?? [])
            .GroupBy(o => o.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Sum(o => o.Amount));

    private Task<IReadOnlyList<MaternityClaim>> SettleMaternityAdvancesAsync(PayrollRun run, CancellationToken ct)
    {
        if (_maternityPay is not null)
            return _maternityPay.SettleAdvancesAsync(run, ct);
        if (run.Employees.Any(e => e.AdvanceMaternityBenefit && e.MaternityBenefitAdvance > 0m))
            throw new InvalidOperationException(NoMaternityPay);
        return Task.FromResult<IReadOnlyList<MaternityClaim>>([]);
    }

    private const string NoMaternityPay =
        "PayrollRunService was built without an IMaternityPayCalculator, so it can't advance a maternity benefit.";

    private IYearEndLeaveConversion YearEndLeave() => _yearEndLeave ?? throw new InvalidOperationException(
        "PayrollRunService was built without an IYearEndLeaveConversion, so it can't convert year-end leave.");

    private const string LeaveConversionIsForDecember = "Year-end leave conversion goes on a December payroll.";

    /// <summary>
    /// Year-end leave conversion goes only on a Regular run whose period ends in December - a
    /// final pay converts the employee's leave its own way.
    /// </summary>
    private static void EnsureLeaveConversionFits(PayrollRunType runType, DateOnly periodEnd)
    {
        if (runType != PayrollRunType.Regular || periodEnd.Month != 12)
            throw new DomainException(LeaveConversionIsForDecember);
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
    /// <param name="creating">True when the run is being created rather than recomputed.</param>
    private async Task<List<PayrollRunEmployee>> ComputeEntriesAsync(
        PayrollRun run,
        IReadOnlyList<PayrollRunEmployeeInput> employees,
        IReadOnlyDictionary<Guid, PayrollAttendanceInput>? snapshots,
        CancellationToken ct,
        bool creating = false)
    {
        if (employees.Any(e => e.IncludeThirteenthMonth))
            EnsureThirteenthMonthPaidInItsYear(run, creating);

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
        // itself is never Paid while it can still be computed. The same runs give how much of the
        // exemption is used: their 13th month and their leave beyond de minimis, which share it.
        // Looked up only when someone in the run is receiving a 13th month or the run converts
        // leave, since nobody else's pay depends on it.
        var earlierInYear = new Dictionary<Guid, (decimal Basic, decimal ThirteenthMonth, decimal ExemptUsed)>();
        // The ten de minimis leave days are the pay year's too: what those runs paid as de minimis
        // leaves the rest (see LeavePayout.DeMinimisDaysLeft).
        var deMinimisDaysLeft = new Dictionary<Guid, decimal>();
        var ineligible = new HashSet<Guid>();
        IReadOnlyList<Domain.Entities.Employees.Employee> people = [];
        if (employees.Any(e => e.IncludeThirteenthMonth) || run.IncludesLeaveConversion)
        {
            var paidRuns = await _runRepo.GetPaidRunsInYearAsync(run.PayDate.Year, ct) ?? [];
            earlierInYear = paidRuns
                .Where(r => r.Id != run.Id)
                .SelectMany(r => r.Employees)
                .Where(e => employeeIds.Contains(e.EmployeeId))
                .GroupBy(e => e.EmployeeId)
                .ToDictionary(g => g.Key, g => (
                    g.Sum(e => e.RegularPay),
                    g.Sum(e => e.ThirteenthMonth),
                    g.Sum(e => e.ThirteenthMonthAndOtherBenefits)));
            deMinimisDaysLeft = paidRuns
                .Where(r => r.Id != run.Id)
                .SelectMany(r => r.Employees)
                .Where(e => employeeIds.Contains(e.EmployeeId))
                .GroupBy(e => e.EmployeeId)
                .ToDictionary(g => g.Key, g => LeavePayout.DeMinimisDaysLeft(g));

            people = await _employeeRepo.GetByIdsAsync(employeeIds, ct) ?? [];
            ineligible = people.Where(p => !p.Is13thMonthEligible).Select(p => p.Id).ToHashSet();
        }

        // The 13th month is paid once. Only Paid runs count as paid earlier above, so two unpaid
        // runs of the pay year that both included it would each pay the full amount due. Someone
        // ineligible is paid none of it on either, so isn't held to this - nor to the check below.
        var receivingThirteenthMonth = employees
            .Where(e => e.IncludeThirteenthMonth && !ineligible.Contains(e.EmployeeId))
            .Select(e => e.EmployeeId)
            .Distinct()
            .ToList();
        if (receivingThirteenthMonth.Count > 0)
        {
            var elsewhere = await _runRepo.GetUnpaidThirteenthMonthsInYearAsync(
                run.PayDate.Year, receivingThirteenthMonth, run.Id, ct) ?? [];
            if (elsewhere.Count > 0)
            {
                var first = elsewhere[0];
                var name = people.FirstOrDefault(p => p.Id == first.EmployeeId)?.FullName ?? first.EmployeeId.ToString();
                throw new DomainException(
                    $"{name}'s 13th month is already on {first.RunNumber}, which isn't paid yet; pay it or leave it out there first.");
            }

            // The 13th month is worked out from the basic on the pay year's Paid runs, so an
            // earlier cutoff that isn't paid yet would silently drop out of it.
            var unpaidBefore = await _runRepo.GetEarlierUnpaidRunsInYearAsync(
                run.PayDate.Year, run.PayDate, receivingThirteenthMonth, run.Id, ct) ?? [];
            if (unpaidBefore.Count > 0)
            {
                var first = unpaidBefore[0];
                var name = people.FirstOrDefault(p => p.Id == first.EmployeeId)?.FullName ?? first.EmployeeId.ToString();
                throw new DomainException(
                    $"{name} is on {first.RunNumber}, which isn't paid yet; pay it before computing the 13th month.");
            }
        }

        // Year-end leave conversion (a December run that includes it): each employee's unused
        // year-end leave for the period-end year, converted once a year - an employee whose leave
        // for the year another regular run has already converted, in any status, is refused.
        IYearEndLeaveConversion? yearEndLeave = null;
        int conversionYear = run.PeriodEnd.Year;
        if (run.IncludesLeaveConversion)
        {
            EnsureLeaveConversionFits(run.RunType, run.PeriodEnd);
            yearEndLeave = YearEndLeave();

            var converted = await _runRepo.GetLeaveConversionsInYearAsync(conversionYear, employeeIds, run.Id, ct) ?? [];
            if (converted.Count > 0)
            {
                var first = converted[0];
                var name = people.FirstOrDefault(p => p.Id == first.EmployeeId)?.FullName ?? first.EmployeeId.ToString();
                throw new DomainException(string.Create(CultureInfo.InvariantCulture,
                    $"{name}'s leave for {conversionYear} was already converted in {first.RunNumber}."));
            }
        }

        // Maternity pay (RA 11210), on a regular run: each employee's advance of the SSS benefit and
        // the part of regular pay SSS covers, read in one go for the whole run. A final pay has none.
        bool exempt = settings?.ExemptFromMaternityDifferential ?? false;
        MaternityRun? maternity = null;
        if (run.RunType == PayrollRunType.Regular)
        {
            if (_maternityPay is not null)
                maternity = await _maternityPay.LoadAsync(run, employeeIds, ct);
            else if (employees.Any(e => e.AdvanceMaternityBenefit))
                throw new InvalidOperationException(NoMaternityPay);
        }

        // Contributions deferred on the employees' earlier Paid entries (a period SSS maternity
        // covered), which this run collects as far as each entry's pay allows.
        var deferredOutstanding = await DeferredContributionsOutstandingAsync(employeeIds, ct);

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

            // Priced at the daily rate the engine gives the entry - the same helper, so the two
            // always agree; Mark Paid re-prices the days at the entry's DailyRate.
            decimal dailyRate = PayrollComputationService.DailyRateFor(compensation.BasicSalary, dailyRateFactor);
            LeaveConversionInput? leaveConversion = null;
            if (yearEndLeave is not null)
            {
                var days = await yearEndLeave.DaysAsync(employee.EmployeeId, conversionYear, ct);
                if (days.Count > 0)
                {
                    var (deMinimis, otherBenefits) = LeavePayout.Price(days, dailyRate,
                        deMinimisDaysLeft.GetValueOrDefault(employee.EmployeeId, FinalPayMath.DeMinimisVacationDays));
                    leaveConversion = new LeaveConversionInput(deMinimis, otherBenefits);
                }
            }

            // overtimeHours/holidayDays are left at their defaults: Compute reads them only when
            // attendance is null, and any caller override has already been folded into the
            // attendance record above so that the snapshot records what was actually paid.
            PayrollRunEmployee ComputeWith(MaternityInput? maternityInput) => _computationService.Compute(
                compensation, run,
                daysWorked: employee.DaysWorked ?? defaultDaysInPeriod,
                includeThirteenthMonth: employee.IncludeThirteenthMonth,
                rates: rates, attendance: attendance, dailyRateFactor: dailyRateFactor,
                thirteenthMonthPaidEarlierInYear:
                    earlierInYear.GetValueOrDefault(employee.EmployeeId).ThirteenthMonth,
                basicEarnedEarlierInYear: earlierInYear.GetValueOrDefault(employee.EmployeeId).Basic,
                isThirteenthMonthEligible: !ineligible.Contains(employee.EmployeeId),
                otherBenefitsExemptUsedEarlierInYear:
                    earlierInYear.GetValueOrDefault(employee.EmployeeId).ExemptUsed,
                leaveConversion: leaveConversion,
                maternity: maternityInput,
                deferredContributionsOutstanding: deferredOutstanding.GetValueOrDefault(employee.EmployeeId));

            // The offset is worked out against the regular pay after absences and tardiness and
            // before the offset itself - the entry's RegularPay without a maternity input, since the
            // engine strikes it there and the input changes nothing before it. So the entry is
            // computed without the input, then again with it when there is one: the engine stays
            // the one place regular pay is worked out.
            var entry = ComputeWith(null);
            var maternityPay = maternity?.For(employee.EmployeeId, employee.AdvanceMaternityBenefit, entry.RegularPay, exempt)
                ?? MaternityPay.None;
            if (maternityPay.Advance > 0m || maternityPay.Offset > 0m || maternityPay.Differential > 0m)
                entry = ComputeWith(new MaternityInput(maternityPay.Advance, maternityPay.Offset, maternityPay.Differential));
            entry.AdvanceMaternityBenefit = employee.AdvanceMaternityBenefit;
            entry.MaternityClaimId = maternityPay.ClaimId;

            if (leaveConversion is not null && entry.DailyRate != dailyRate)
                throw new InvalidOperationException(
                    $"Leave was priced at {dailyRate} a day but the entry's daily rate is {entry.DailyRate}.");

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

    /// <summary>
    /// The run as the API returns it. Its maternity warnings are worked out now rather than stored:
    /// a claim set up or advanced since the run was computed changes what HR still has to do. A
    /// payslip goes without them (<paramref name="withWarnings"/> false), and without their queries.
    /// </summary>
    private async Task<PayrollRunDto> ToDtoAsync(PayrollRun run, CancellationToken ct, bool withWarnings = true) => new(
        run.Id, run.RunNumber, run.PeriodLabel,
        run.PeriodStart, run.PeriodEnd, run.PayDate,
        run.Frequency, run.Status,
        run.EmployeeCount, run.TotalGrossPay, run.TotalDeductions, run.TotalNetPay,
        run.CreatedAt, run.AttendancePeriodId, run.EmployeesMissingAttendance,
        run.Employees.Select(ToEmployeeDto).ToList(), run.RunType, run.IncludesLeaveConversion,
        IncludesThirteenthMonth(run),
        _maternityPay is null || !withWarnings ? [] : await _maternityPay.WarningsAsync(run, ct));

    private static PayrollRunSummaryDto ToSummaryDto(PayrollRun run) => new(
        run.Id, run.RunNumber, run.PeriodLabel,
        run.PeriodStart, run.PeriodEnd, run.PayDate,
        run.Frequency, run.Status,
        run.EmployeeCount, run.TotalGrossPay, run.TotalNetPay,
        run.EmployeesMissingAttendance, run.CreatedAt, run.RunType, run.IncludesLeaveConversion,
        IncludesThirteenthMonth(run));

    /// <summary>A run includes the 13th month when any of its entries does.</summary>
    private static bool IncludesThirteenthMonth(PayrollRun run) => run.Employees.Any(e => e.IncludeThirteenthMonth);

    private static PayrollRunEmployeeDto ToEmployeeDto(PayrollRunEmployee e) => new(
        e.Id, e.EmployeeId, e.Employee?.FullName ?? string.Empty,
        e.Employee?.EmployeeNumber ?? string.Empty, e.DaysWorked,
        e.GrossPay, e.TotalDeductions, e.NetPay,
        e.RegularPay, e.OvertimePay, e.HolidayPay, e.NightDiffPay,
        e.TaxableAllowances, e.NonTaxableAllowances, e.ThirteenthMonth,
        e.AbsenceDeduction, e.TardinessDeduction,
        e.SSSEmployee, e.SSSEmployer, e.PhilHealthEmployee, e.PhilHealthEmployer,
        e.PagIbigEmployee, e.PagIbigEmployer, e.WithholdingTax, e.LoanDeductions, e.OtherDeductions,
        e.LeaveConversionPay, e.LeaveConversionNonTaxable, e.SeparationPay, e.RetirementPay, e.FinalPayNonTaxable,
        e.MaternityBenefitAdvance, e.MaternityBenefitOffset, e.MaternityDifferential,
        e.ContributionsDeferred, e.DeferredContributionsCollected);
}
