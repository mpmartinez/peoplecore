using System.Globalization;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Application.Scheduling.Interfaces;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.Application.Payroll.FinalPay;

/// <summary>
/// Builds a separated employee's final pay as a single-employee <see cref="PayrollRunType.FinalPay"/>
/// run, computed by the ordinary <see cref="PayrollComputationService"/> with a
/// <see cref="FinalPayExtras"/> argument, and with the year's tax settled through the employee's
/// 2316 so the certificate balances by construction.
/// <para>
/// <b>Attendance.</b> The final period goes through the attendance bridge like any cutoff (the
/// spec's "normal computation and attendance bridge"). Its base pay is the daily rate times the
/// <i>salary days</i> (stored as <see cref="FinalPayInputs.WorkingDays"/>), never the days
/// attended. Which days the salary pays follows the daily-rate factor the rate itself is derived
/// with: under 365 the salary pays rest days, so every calendar day of the period counts; under
/// 313 or 261 it doesn't, so only the days the shift schedules count - Monday to Friday where none
/// is assigned. The bridge's absences and tardiness then come off that base once, in the engine,
/// per scheduled day absent, exactly as they come off a regular cutoff's salary share. Counting
/// attended days as the salary days would take every absence off twice. An unassigned day is paid
/// under the Monday-to-Friday fallback but, as the bridge never guesses a schedule, can't be
/// marked absent - the bridge's own choice to err toward paying rather than over-deducting wages.
/// </para>
/// <para>
/// <b>No salary left to pay.</b> When regular payroll has already paid past the last working
/// day (the day after the last Paid regular run ends is later than it), the final pay carries no
/// salary: its period is stored as the last working day alone with no salary days, and no
/// attendance is taken, since there's no salary for absences to come off. It still carries the
/// 13th month, leave conversion, separation or retirement pay, loans, HR's deductions and the tax
/// settle. A start HR gives that's after the last working day is still refused, as is one on or
/// before the end of a Paid regular run the employee was in - those days were paid already - and
/// one before the hire date.
/// </para>
/// <para>
/// <b>Unpaid regular runs.</b> A final pay isn't created, nor its period changed, while a regular
/// run that includes the employee and isn't Paid yet ends on or after the earlier of the final
/// period's start and the first of the separation month - it would pay the same days twice, pay
/// salary after the separation, or take the month's contributions again - or starts on or
/// before the last working day, since the final pay's 13th month and tax settle take it as the
/// employee's last pay. The run has to be paid first - or, when it starts after the last working
/// day, the employee taken off it. Once the final pay exists, regular runs refuse the employee
/// altogether (see PayrollRunService), so nothing is paid outside it.
/// </para>
/// <para>
/// <b>Contributions</b> top the separation month - the last working day's month - up to exactly
/// one month's SSS, PhilHealth and Pag-IBIG on the monthly basic, employee and employer shares
/// alike: the month's full contribution less what that month's Paid runs already deducted, never
/// below zero. The month's runs are picked as the SSS, PhilHealth and Pag-IBIG remittance reports
/// pick them - Paid runs whose period ends in the month - so those reports show one month.
/// </para>
/// <para>
/// <b>Years.</b> The 13th month belongs to the last working day's year: its basis is that year's
/// Paid runs plus this final pay's regular pay, less the 13th month already paid in it. The tax
/// settle and the 2316 it builds are the pay date's year's. The two differ only when the final
/// pay is made after the year end.
/// </para>
/// <para>
/// <b>Allowances</b> are paid for the final period's salary days, pro-rated like its base pay:
/// each allowance's monthly amount x 12 / factor x salary days (see
/// <see cref="PayrollComputationService.Compute"/>). They stay taxable or non-taxable exactly as
/// on a regular run. No salary days, no allowances.
/// </para>
/// </summary>
public sealed class FinalPayService : IFinalPayService
{
    private const int MaxDeductionLabelLength = 200;
    private const int MaxOverrideNoteLength = 500;

    private readonly ISeparationRepository _separations;
    private readonly IPayrollRunRepository _runs;
    private readonly IEmployeeCompensationRepository _compensations;
    private readonly IEmployeeLoanRepository _loans;
    private readonly IEmployeeAllowanceRepository _allowances;
    private readonly ILeaveBalanceRepository _leaveBalances;
    private readonly IShiftService _shifts;
    private readonly IPayrollAttendanceBridge _attendance;
    private readonly IPayrollSettingsRepository _settings;
    private readonly IBir2316Service _bir2316;
    private readonly PayrollComputationService _engine;
    private readonly TimeProvider _clock;

    public FinalPayService(
        ISeparationRepository separations,
        IPayrollRunRepository runs,
        IEmployeeCompensationRepository compensations,
        IEmployeeLoanRepository loans,
        IEmployeeAllowanceRepository allowances,
        ILeaveBalanceRepository leaveBalances,
        IShiftService shifts,
        IPayrollAttendanceBridge attendance,
        IPayrollSettingsRepository settings,
        IBir2316Service bir2316,
        PayrollComputationService engine,
        TimeProvider clock)
    {
        _separations = separations;
        _runs = runs;
        _compensations = compensations;
        _loans = loans;
        _allowances = allowances;
        _leaveBalances = leaveBalances;
        _shifts = shifts;
        _attendance = attendance;
        _settings = settings;
        _bir2316 = bir2316;
        _engine = engine;
        _clock = clock;
    }

    public async Task<FinalPaySummaryDto> CreateAsync(Guid separationId, FinalPayRequest request, CancellationToken ct = default)
    {
        var separation = await LoadSeparationAsync(separationId, ct);

        if (separation.FinalPayRunId is Guid existingId)
        {
            var existing = separation.FinalPayRun ?? await _runs.GetByIdAsync(existingId, ct);
            throw new DomainException(
                $"{separation.Employee.FullName} already has a final-pay run ({existing?.RunNumber}).");
        }

        var compensation = await LoadCompensationAsync(separation, ct);
        Validate(request);
        var period = await ResolvePeriodAsync(separation, request.PeriodStart, ct);

        var run = new PayrollRun
        {
            RunNumber = await NextRunNumberAsync(request.PayDate.Year, ct),
            RunType = PayrollRunType.FinalPay,
            Frequency = compensation.PayFrequency,
            Status = PayrollRunStatus.Draft,
        };
        var inputs = new FinalPayInputs { PayrollRunId = run.Id, SeparationId = separation.Id };
        run.FinalPayInputs = inputs;
        await ApplyRequestAsync(run, inputs, separation, request, period, ct);

        var attendance = await DeriveAttendanceAsync(run, inputs, separation.EmployeeId, ct);
        var (entry, figures) = await ComputeSettledAsync(run, inputs, separation, compensation, attendance, ct);
        run.Employees = [entry];

        // Saved in one go: the run, its entry, its inputs and the separation's link.
        separation.FinalPayRunId = run.Id;
        await _runs.AddFinalPayRunAsync(run, separation, ct);

        return await SummaryAsync(separation, run, entry, figures, ct);
    }

    public async Task<FinalPaySummaryDto> UpdateAsync(Guid separationId, FinalPayRequest request, CancellationToken ct = default)
    {
        var separation = await LoadSeparationAsync(separationId, ct);
        if (separation.FinalPayRunId is not Guid runId)
            throw new DomainException($"{separation.Employee.FullName} has no final-pay run yet.");

        var run = await _runs.GetWithEntriesAsync(runId, ct)
            ?? throw new KeyNotFoundException($"Payroll run {runId} not found.");

        // Same line PayrollRunService.ComputeAsync draws for a final pay: paid figures have
        // already retired loan balances, but approved ones can still change - approval can come
        // before clearance is complete, and clearance is where deductions such as an unreturned
        // laptop come up. The change sends the run back to Draft below, to be approved again.
        if (run.Status == PayrollRunStatus.Paid)
            throw new DomainException("A paid final pay can't be changed.");

        var inputs = run.FinalPayInputs
            ?? throw new InvalidOperationException($"Final-pay run {run.RunNumber} has no final-pay inputs.");

        var compensation = await LoadCompensationAsync(separation, ct);
        Validate(request);
        var period = await ResolvePeriodAsync(separation, request.PeriodStart, ct);

        // The number follows the pay date's year, so moving the pay date into another year moves
        // the run onto that year's sequence.
        if (request.PayDate.Year != run.PayDate.Year)
            run.RunNumber = await NextRunNumberAsync(request.PayDate.Year, ct);
        run.Frequency = compensation.PayFrequency;
        await ApplyRequestAsync(run, inputs, separation, request, period, ct);

        // HR is redefining the run, possibly its period, so attendance is derived afresh; only a
        // plain recompute (RecomputeAsync) holds to the snapshot.
        var attendance = await DeriveAttendanceAsync(run, inputs, separation.EmployeeId, ct);
        var (entry, figures) = await ComputeSettledAsync(run, inputs, separation, compensation, attendance, ct);

        // Changed figures need a fresh approval, as on any recompute.
        run.Status = PayrollRunStatus.Draft;
        run.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
        await _runs.ReplaceEntriesAsync(run, [entry], ct);

        return await SummaryAsync(separation, run, entry, figures, ct);
    }

    public async Task<FinalPaySummaryDto?> GetAsync(Guid separationId, CancellationToken ct = default)
    {
        var separation = await LoadSeparationAsync(separationId, ct);
        if (separation.FinalPayRunId is not Guid runId)
            return null;

        var run = await _runs.GetWithEntriesAsync(runId, ct);
        if (run?.FinalPayInputs is not { } inputs)
            return null;

        var entry = run.Employees.FirstOrDefault(e => e.EmployeeId == separation.EmployeeId)
            ?? throw new InvalidOperationException($"Final-pay run {run.RunNumber} has no entry for its employee.");

        // The statutory figures are shown at the rate the entry was computed at.
        var compensation = await _compensations.GetByEmployeeIdAsync(separation.EmployeeId, ct);
        var figures = await FiguresAsync(separation, inputs, compensation?.BasicSalary ?? 0m, entry.DailyRate, ct);

        return await SummaryAsync(separation, run, entry, figures, ct);
    }

    public async Task<List<PayrollRunEmployee>> RecomputeAsync(PayrollRun run, CancellationToken ct = default)
    {
        if (run.RunType != PayrollRunType.FinalPay || run.FinalPayInputs is not { } inputs)
            throw new InvalidOperationException($"Payroll run {run.RunNumber} is not a final-pay run with its inputs loaded.");

        var separation = await LoadSeparationAsync(inputs.SeparationId, ct);
        var compensation = await LoadCompensationAsync(separation, ct);

        // Rebuilt from what was stored: the working days as counted when HR set the period (a
        // later schedule change must not move them), and the attendance snapshot on the current
        // entry - the same rule a regular recompute follows, so an attendance edit made after the
        // fact can't change what the employee was already told.
        var current = run.Employees.FirstOrDefault(e => e.EmployeeId == separation.EmployeeId);
        var attendance = current is not null
            ? PayrollRunService.FromSnapshot(current)
            : await DeriveAttendanceAsync(run, inputs, separation.EmployeeId, ct);

        var (entry, _) = await ComputeSettledAsync(run, inputs, separation, compensation, attendance, ct);
        return [entry];
    }

    // ── Inputs ───────────────────────────────────────────────────────────────

    private async Task<Separation> LoadSeparationAsync(Guid separationId, CancellationToken ct)
        => await _separations.GetAsync(separationId, ct)
           ?? throw new KeyNotFoundException($"Separation {separationId} not found.");

    private async Task<EmployeeCompensation> LoadCompensationAsync(Separation separation, CancellationToken ct)
        => await _compensations.GetByEmployeeIdAsync(separation.EmployeeId, ct)
           ?? throw new DomainException($"{separation.Employee.FullName} has no compensation record.");

    private static void Validate(FinalPayRequest request)
    {
        if (request.PayDate == default)
            throw new DomainException("The pay date is required.");

        if ((request.SeparationPayOverride is not null || request.RetirementPayOverride is not null)
            && string.IsNullOrWhiteSpace(request.OverrideNote))
            throw new DomainException("Explain the separation or retirement pay override.");

        if (request.SeparationPayOverride < 0m || request.RetirementPayOverride < 0m)
            throw new DomainException("The separation or retirement pay override can't be negative.");

        if (request.OverrideNote?.Trim().Length > MaxOverrideNoteLength)
            throw new DomainException($"The override note can't be longer than {MaxOverrideNoteLength} characters.");

        foreach (var deduction in request.Deductions ?? [])
        {
            if (string.IsNullOrWhiteSpace(deduction.Label) || deduction.Amount <= 0m)
                throw new DomainException("Each deduction needs a label and an amount above zero.");
            if (deduction.Label.Trim().Length > MaxDeductionLabelLength)
                throw new DomainException($"A deduction label can't be longer than {MaxDeductionLabelLength} characters.");
        }
    }

    /// <summary>
    /// Where the final period starts, and whether it pays no salary at all (see the class remarks).
    /// </summary>
    private sealed record FinalPeriod(DateOnly Start, bool NoSalary);

    /// <summary>
    /// HR's start, or by default the day after the employee's last Paid regular run ended - the
    /// first day nobody has paid them for yet - or, when they were never paid, the first of the
    /// last working day's month. A default start past the last working day means regular payroll
    /// already paid the whole of it: the period is then the last working day alone, with no
    /// salary. Refused while an unpaid regular run the employee is in overlaps the period.
    /// </summary>
    private async Task<FinalPeriod> ResolvePeriodAsync(Separation separation, DateOnly? requested, CancellationToken ct)
    {
        var lastDay = separation.LastWorkingDay;
        var runs = await _runs.GetRunsForEmployeeAsync(separation.EmployeeId, ct);
        var regular = runs.Where(r => r.RunType == PayrollRunType.Regular).ToList();

        FinalPeriod period;
        if (requested is DateOnly start)
        {
            if (start > lastDay)
                throw new DomainException("The final pay period can't start after the last working day.");

            var hired = separation.Employee.HireDate;
            if (start < hired)
                throw new DomainException(string.Create(CultureInfo.InvariantCulture,
                    $"{separation.Employee.FullName} was hired on {hired:MMM d, yyyy}; start final pay on or after that."));

            var paidThrough = regular
                .Where(r => r.Status == PayrollRunStatus.Paid && r.PeriodEnd >= start)
                .MaxBy(r => r.PeriodEnd);
            if (paidThrough is not null)
                throw new DomainException(string.Create(CultureInfo.InvariantCulture,
                    $"Payroll {paidThrough.RunNumber} already paid up to {paidThrough.PeriodEnd:MMM d, yyyy}; start final pay after that."));

            period = new FinalPeriod(start, NoSalary: false);
        }
        else
        {
            var defaultStart = DefaultStart(separation, regular);
            period = defaultStart > lastDay
                ? new FinalPeriod(lastDay, NoSalary: true)
                : new FinalPeriod(defaultStart, NoSalary: false);
        }

        // Refused while an unpaid regular run the employee is in (GetRunsForEmployeeAsync returns
        // only those) either ends on or after the earlier of the final period's start and the
        // first of the separation month, or starts on or before the last working day. The first
        // catches a run overlapping the final period, one after the last working day (salary
        // after separation), and one earlier in the separation month - any of which, paid later,
        // would take the month's contributions again. The second catches any earlier run still
        // unpaid: the final pay's 13th month and tax settle take it as the employee's last pay,
        // so a run paid after it would fall outside both. Between them they catch every unpaid
        // run - one starting after the last working day always ends after the start. A run that
        // starts after the last working day has nothing to pay them, so they come off it rather
        // than wait for it to be paid.
        var from = Min(period.Start, new DateOnly(lastDay.Year, lastDay.Month, 1));
        var unpaid = regular
            .Where(r => r.Status != PayrollRunStatus.Paid && (r.PeriodEnd >= from || r.PeriodStart <= lastDay))
            .OrderBy(r => r.PeriodStart)
            .FirstOrDefault();
        if (unpaid is not null)
            throw new DomainException(unpaid.PeriodStart > lastDay
                ? $"Payroll {unpaid.RunNumber} covers {unpaid.PeriodLabel} after {separation.Employee.FullName}'s last working day; take them off it before creating final pay."
                : $"Payroll {unpaid.RunNumber} covers {unpaid.PeriodLabel} and isn't paid yet; pay it before creating final pay.");

        return period;
    }

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

    /// <summary>
    /// The first day nobody has paid the employee for: the day after their last Paid regular run
    /// ended or, when they were never paid, the first of the last working day's month - or their
    /// hire date, when they were hired later in that month.
    /// </summary>
    private static DateOnly DefaultStart(Separation separation, IEnumerable<PayrollRun> regularRuns)
    {
        var lastDay = separation.LastWorkingDay;
        var lastPaidEnd = regularRuns
            .Where(r => r.Status == PayrollRunStatus.Paid)
            .Select(r => (DateOnly?)r.PeriodEnd)
            .Max();
        if (lastPaidEnd is DateOnly end)
            return end.AddDays(1);

        var firstOfMonth = new DateOnly(lastDay.Year, lastDay.Month, 1);
        var hired = separation.Employee.HireDate;
        return hired > firstOfMonth ? hired : firstOfMonth;
    }

    /// <summary>
    /// Where the stored period stands against today's default start. <c>NoSalary</c>: the
    /// no-salary path - no salary days, and a default start past the last working day - as opposed
    /// to a start HR chose whose period happens to hold none. <c>StartIsDefault</c>: the stored
    /// start is what a null start gives, either the default start itself or, on the no-salary
    /// path, the last working day. Inferred, since the inputs don't record whether HR gave one.
    /// </summary>
    private async Task<(bool NoSalary, bool StartIsDefault)> PeriodAgainstDefaultAsync(
        Separation separation, PayrollRun run, FinalPayInputs inputs, CancellationToken ct)
    {
        var runs = await _runs.GetRunsForEmployeeAsync(separation.EmployeeId, ct);
        var defaultStart = DefaultStart(separation, runs.Where(r => r.RunType == PayrollRunType.Regular));
        bool noSalary = inputs.WorkingDays == 0m && defaultStart > separation.LastWorkingDay;
        return (noSalary, noSalary || run.PeriodStart == defaultStart);
    }

    private async Task<string> NextRunNumberAsync(int payYear, CancellationToken ct)
    {
        // One past the year's highest number, not its count: Update moves a run whose pay date
        // changes year onto the other sequence, and a count would then reissue a number in use.
        var sequence = await _runs.GetLastFinalPaySequenceAsync(payYear, ct) + 1;
        return $"FP-{payYear}-{sequence:D3}";
    }

    /// <summary>Writes the request onto the run and its inputs, counting the period's salary days.</summary>
    private async Task ApplyRequestAsync(PayrollRun run, FinalPayInputs inputs, Separation separation,
        FinalPayRequest request, FinalPeriod period, CancellationToken ct)
    {
        run.PeriodStart = period.Start;
        run.PeriodEnd = separation.LastWorkingDay;
        run.PayDate = request.PayDate;

        var settings = await _settings.GetDefaultAsync(ct);
        inputs.WorkingDays = period.NoSalary
            ? 0m
            : await CountSalaryDaysAsync(
                separation.EmployeeId, run.PeriodStart, run.PeriodEnd, settings?.DailyRateFactor, ct);
        inputs.SeparationPayOverride = request.SeparationPayOverride;
        inputs.RetirementPayOverride = request.RetirementPayOverride;
        inputs.OverrideNote = string.IsNullOrWhiteSpace(request.OverrideNote) ? null : request.OverrideNote.Trim();
        // New objects, never edits of the loaded ones: the repository deletes whatever loaded
        // deduction is no longer in the list and inserts the rest.
        inputs.Deductions = (request.Deductions ?? [])
            .Select(d => new FinalPayDeduction { FinalPayInputsId = inputs.Id, Label = d.Label.Trim(), Amount = d.Amount })
            .ToList();
    }

    /// <summary>
    /// The days in the period the salary pays. Under a factor that pays rest days (365) that is
    /// every calendar day; otherwise (313, 261) the days the employee's shift schedules - rest
    /// days excluded - with Monday to Friday standing in for any day no shift is assigned. Never
    /// the days attended: absences come off separately, through the attendance bridge (see the
    /// class remarks).
    /// </summary>
    private async Task<decimal> CountSalaryDaysAsync(Guid employeeId, DateOnly from, DateOnly to,
        decimal? dailyRateFactor, CancellationToken ct)
    {
        if (PayrollComputationService.PaysRestDays(dailyRateFactor))
            return to.DayNumber - from.DayNumber + 1;

        decimal days = 0m;
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var schedule = await _shifts.ResolveShiftForDayAsync(employeeId, date, ct);
            bool works = schedule is null
                ? date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
                : !schedule.IsRestDay;
            if (works) days++;
        }
        return days;
    }

    /// <summary>
    /// The period's attendance from the bridge - none when there are no salary days, as there is
    /// then no salary for absences or tardiness to come off.
    /// </summary>
    private async Task<PayrollAttendanceInput> DeriveAttendanceAsync(PayrollRun run, FinalPayInputs inputs,
        Guid employeeId, CancellationToken ct)
    {
        if (inputs.WorkingDays == 0m)
        {
            run.EmployeesMissingAttendance = 0;
            return new PayrollAttendanceInput();
        }

        var bridged = await _attendance.BuildAsync([employeeId], run.PeriodStart, run.PeriodEnd, ct);
        run.EmployeesMissingAttendance = bridged.EmployeesWithoutSchedule.Count;
        return bridged.Inputs.TryGetValue(employeeId, out var input) ? input : new PayrollAttendanceInput();
    }

    // ── Computing ────────────────────────────────────────────────────────────

    /// <summary>
    /// The statutory final-pay figures: service years, separation or retirement pay (HR's override
    /// replacing the computed one), what of that is non-taxable, and the leave paid out.
    /// </summary>
    private sealed record Figures(
        int ServiceYears,
        decimal? ComputedSeparationOrRetirementPay,
        decimal SeparationPay,
        decimal RetirementPay,
        decimal SeparationAndRetirementNonTaxable,
        IReadOnlyList<FinalPayLeaveLineDto> LeaveLines,
        decimal LeaveNonTaxable,
        decimal LeaveTaxable);

    private async Task<Figures> FiguresAsync(Separation separation, FinalPayInputs inputs,
        decimal monthlyBasic, decimal dailyRate, CancellationToken ct)
    {
        var employee = separation.Employee;
        var lastDay = separation.LastWorkingDay;
        int years = FinalPayMath.ServiceYears(employee.HireDate, lastDay);

        // Separation pay for an authorized cause and retirement pay for an eligible retiree are
        // non-taxable (NIRC Sec. 32(B)(6)(a) and (b)). An override keeps that treatment only in
        // those two cases; anything else HR adds - a goodwill payment on a resignation, an early
        // retirement under a company plan - is taxable compensation.
        decimal computedSeparation = 0m, computedRetirement = 0m;
        bool separationNonTaxable = false, retirementNonTaxable = false;
        decimal? computedShown = null;
        switch (separation.Type)
        {
            case SeparationType.AuthorizedCause:
                computedSeparation = separation.AuthorizedCause is { } cause
                    ? FinalPayMath.SeparationPay(cause, monthlyBasic, years)
                    : 0m;
                separationNonTaxable = true;
                computedShown = computedSeparation;
                break;

            case SeparationType.Retirement:
                retirementNonTaxable = FinalPayMath.IsRetirementEligible(employee.DateOfBirth, lastDay, years);
                computedRetirement = retirementNonTaxable ? FinalPayMath.RetirementPay(dailyRate, years) : 0m;
                computedShown = computedRetirement;
                break;
        }

        decimal separationPay = inputs.SeparationPayOverride ?? computedSeparation;
        decimal retirementPay = inputs.RetirementPayOverride ?? computedRetirement;
        decimal nonTaxable = (separationNonTaxable ? separationPay : 0m) + (retirementNonTaxable ? retirementPay : 0m);

        // Every convertible type's remaining days in the last working day's year.
        var balances = await _leaveBalances.GetByEmployeeAsync(separation.EmployeeId, lastDay.Year, ct);
        var leaveLines = balances
            .Where(b => b.LeaveType is { IsConvertibleToCash: true } && b.RemainingDays > 0m)
            .Select(b => new FinalPayLeaveLineDto(b.LeaveType.Name, b.RemainingDays, b.LeaveType.CountsAsVacationForDeMinimis))
            .ToList();
        var (leaveNonTaxable, leaveTaxable) =
            FinalPayMath.LeaveConversion(leaveLines.Select(l => (l.Days, l.CountsAsVacation)), dailyRate);

        return new Figures(years, computedShown, separationPay, retirementPay, nonTaxable,
                           leaveLines, leaveNonTaxable, leaveTaxable);
    }

    /// <summary>
    /// Computes the entry twice: once to see what its own withholding would be, and again with the
    /// year's tax settled - the 2316's tax due, built over the pay year's Paid runs plus this draft
    /// entry, less everything already withheld (this employer's other runs, a previous employer,
    /// the PERA credit). The result can be negative: a refund.
    /// </summary>
    private async Task<(PayrollRunEmployee Entry, Figures Figures)> ComputeSettledAsync(
        PayrollRun run, FinalPayInputs inputs, Separation separation, EmployeeCompensation compensation,
        PayrollAttendanceInput attendance, CancellationToken ct)
    {
        var employee = separation.Employee;
        var employeeId = separation.EmployeeId;
        int payYear = run.PayDate.Year;

        var settings = await _settings.GetDefaultAsync(ct);
        var rates = PayrollRunService.ToRates(settings);
        decimal? factor = settings?.DailyRateFactor;
        decimal dailyRate = PayrollComputationService.DailyRateFor(compensation.BasicSalary, factor);

        // Neither loans nor allowances are mapped on EmployeeCompensation (see its remarks). The
        // engine pro-rates the allowances over the salary days (see the class remarks).
        compensation.Loans = (await _loans.GetByEmployeeIdsAsync([employeeId], ct)).Where(l => l.IsActive).ToList();
        compensation.Allowances = (await _allowances.GetByEmployeeIdsAsync([employeeId], ct))
            .Where(a => a.EmployeeId == employeeId)
            .ToList();

        // The 13th month is the last working day's year's: one twelfth of the basic earned that
        // year - its Paid runs, selected by pay date as for any run, plus this final pay's own
        // regular pay - less the 13th month already paid in it. A final pay made after the year
        // end still owes the year the employee worked. The tax settle below stays on the pay
        // year: that's the certificate the payment lands on.
        int thirteenthMonthYear = separation.LastWorkingDay.Year;
        var earlier = (await _runs.GetPaidRunsForEmployeeInYearAsync(employeeId, thirteenthMonthYear, ct))
            .Where(r => r.Id != run.Id && r.Status == PayrollRunStatus.Paid && r.PayDate.Year == thirteenthMonthYear)
            .SelectMany(r => r.Employees.Where(e => e.EmployeeId == employeeId))
            .ToList();
        decimal basicEarlier = earlier.Sum(e => e.RegularPay);
        decimal thirteenthEarlier = earlier.Sum(e => e.ThirteenthMonth);

        // What the separation month's Paid runs already deducted, the month taken as the
        // remittance reports take it (GetPaidRunsByPeriodEndMonthAsync) - this run excluded.
        var lastDay = separation.LastWorkingDay;
        var deductedInMonth = ContributionShares.Sum(
            (await _runs.GetPaidRunsByPeriodEndMonthAsync(lastDay.Year, lastDay.Month, ct))
                .Where(r => r.Id != run.Id)
                .SelectMany(r => r.Employees.Where(e => e.EmployeeId == employeeId)));

        var figures = await FiguresAsync(separation, inputs, compensation.BasicSalary, dailyRate, ct);
        var extras = new FinalPayExtras(
            inputs.WorkingDays,
            figures.LeaveNonTaxable,
            figures.LeaveTaxable,
            figures.SeparationPay,
            figures.RetirementPay,
            figures.SeparationAndRetirementNonTaxable,
            inputs.Deductions.Select(d => (d.Label, d.Amount)).ToList(),
            WithholdingTaxOverride: null,
            ContributionsDeductedInMonth: deductedInMonth);

        PayrollRunEmployee Compute(FinalPayExtras finalPay) => _engine.Compute(
            compensation, run,
            daysWorked: inputs.WorkingDays,
            includeThirteenthMonth: true,
            rates: rates,
            attendance: attendance,
            dailyRateFactor: factor,
            thirteenthMonthPaidEarlierInYear: thirteenthEarlier,
            basicEarnedEarlierInYear: basicEarlier,
            isThirteenthMonthEligible: employee.Is13thMonthEligible,
            finalPay: finalPay);

        var draft = Compute(extras);

        // run.PayDate.Year is the year passed, as BuildWithDraftEntryAsync requires; the draft
        // entry is this employee's own.
        var certificate = await _bir2316.BuildWithDraftEntryAsync(employeeId, payYear, run, draft, ct)
            ?? throw new DomainException(
                $"{employee.FullName}'s {payYear} BIR 2316 couldn't be built to settle the final pay's tax.");

        decimal withheldElsewhere = certificate.Item25A_PresentTaxWithheld - draft.WithholdingTax;
        decimal settled = Math.Round(
            certificate.Item24_TaxDue - withheldElsewhere
            - certificate.Item25B_PrevTaxWithheld - certificate.Item27_PeraTaxCredit, 2);

        var entry = Compute(extras with { WithholdingTaxOverride = settled });
        PayrollRunService.SnapshotAttendance(entry, attendance);
        return (entry, figures);
    }

    // ── Summary ──────────────────────────────────────────────────────────────

    private async Task<FinalPaySummaryDto> SummaryAsync(Separation separation, PayrollRun run,
        PayrollRunEmployee entry, Figures figures, CancellationToken ct)
    {
        var inputs = run.FinalPayInputs!;
        var (noSalary, startIsDefault) = await PeriodAgainstDefaultAsync(separation, run, inputs, ct);

        return new FinalPaySummaryDto(
            run.Id, run.RunNumber, run.Status,
            run.PeriodStart, run.PeriodEnd, run.PayDate,
            inputs.WorkingDays,
            noSalary,
            startIsDefault,
            entry.LeaveConversionPay, entry.LeaveConversionNonTaxable, figures.LeaveLines,
            entry.SeparationPay, entry.RetirementPay, figures.ComputedSeparationOrRetirementPay,
            inputs.SeparationPayOverride, inputs.RetirementPayOverride,
            inputs.OverrideNote, figures.ServiceYears,
            inputs.Deductions.Select(d => new FinalPayDeductionDto(d.Label, d.Amount)).ToList(),
            await LoanLinesAsync(separation.EmployeeId, run, entry, ct),
            entry.WithholdingTax, entry.GrossPay, entry.NetPay,
            separation.ClearanceComplete,
            separation.ClearanceItems
                .Where(i => i.ClearedAt is null)
                .OrderBy(i => i.SortOrder)
                .Select(i => i.Name)
                .ToList());
    }

    /// <summary>
    /// Each loan's balance, what the entry deducted from it, and what's left uncovered. Once the
    /// run is Paid the deduction has already come off the loan (and a loan paid off is no longer
    /// active), so the balance shown is the one before paying.
    /// </summary>
    private async Task<IReadOnlyList<FinalPayLoanLineDto>> LoanLinesAsync(Guid employeeId, PayrollRun run,
        PayrollRunEmployee entry, CancellationToken ct)
    {
        var loans = (await _loans.GetByEmployeeIdsAsync([employeeId], ct)).Where(l => l.IsActive).ToList();
        var retiredIds = entry.LoanDeductionLines
            .Select(l => l.EmployeeLoanId)
            .Where(id => loans.All(l => l.Id != id))
            .Distinct()
            .ToList();
        if (retiredIds.Count > 0)
            loans.AddRange(await _loans.GetByIdsAsync(retiredIds, ct));

        bool paid = run.Status == PayrollRunStatus.Paid;
        return loans
            .Select(loan =>
            {
                decimal deducted = entry.LoanDeductionLines.Where(l => l.EmployeeLoanId == loan.Id).Sum(l => l.Amount);
                decimal balance = paid ? loan.RemainingBalance + deducted : loan.RemainingBalance;
                return new FinalPayLoanLineDto(loan.LoanType.ToString(), balance, deducted, Math.Max(0m, balance - deducted));
            })
            .ToList();
    }
}
