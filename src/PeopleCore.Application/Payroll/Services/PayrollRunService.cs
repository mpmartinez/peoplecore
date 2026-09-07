using Microsoft.Extensions.Logging;
using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Payroll.DTOs;
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
    private readonly ILogger<PayrollRunService> _logger;

    public PayrollRunService(
        IPayrollRunRepository runRepo,
        IEmployeeCompensationRepository compensationRepo,
        IEmployeeAllowanceRepository allowanceRepo,
        IEmployeeLoanRepository loanRepo,
        IPayrollSettingsRepository settingsRepo,
        PayrollComputationService computationService,
        IPayrollAttendanceBridge attendanceBridge,
        ILogger<PayrollRunService> logger)
    {
        _runRepo = runRepo;
        _compensationRepo = compensationRepo;
        _allowanceRepo = allowanceRepo;
        _loanRepo = loanRepo;
        _settingsRepo = settingsRepo;
        _computationService = computationService;
        _attendanceBridge = attendanceBridge;
        _logger = logger;
    }

    public async Task<PayrollRunDto> CreateAsync(CreatePayrollRunRequest request, CancellationToken ct = default)
    {
        PayrollRunRequestValidator.Validate(request);

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
                rates: rates, attendance: attendance, dailyRateFactor: dailyRateFactor);

            // Snapshot the inputs the figures above were struck from. The entry's own
            // OvertimeHours and HolidayDays are roll-ups Compute wrote; these seven are the
            // parts, and a recompute is rebuilt from them rather than from today's punches.
            entry.AbsenceDays        = attendance.AbsenceDays;
            entry.LateMinutes        = attendance.LateMinutes;
            entry.UndertimeMinutes   = attendance.UndertimeMinutes;
            entry.NightDiffHours     = attendance.NightDiffHours;
            entry.RestDayOTHours     = attendance.RestDayOTHours;
            entry.HolidayRegularDays = attendance.HolidayRegularDays;
            entry.HolidaySpecialDays = attendance.HolidaySpecialDays;

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
        if (input.OvertimeHours is decimal overtimeHours)
            derived = derived with { OvertimeHours = overtimeHours, RestDayOTHours = 0m };

        if (input.HolidayDays is decimal holidayDays)
            derived = derived with { HolidayRegularDays = holidayDays, HolidaySpecialDays = 0m };

        return derived;
    }

    /// <summary>
    /// Rebuilds the attendance a stored entry was computed from, so a recompute reproduces it
    /// exactly.
    /// </summary>
    private static PayrollAttendanceInput FromSnapshot(PayrollRunEmployee entry) => new()
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
        HolidaySpecialDays = entry.HolidaySpecialDays
    };

    private static ContributionRates ToRates(PayrollSettings? settings) => settings is null
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
        run.Employees.Select(ToEmployeeDto).ToList());

    private static PayrollRunSummaryDto ToSummaryDto(PayrollRun run) => new(
        run.Id, run.RunNumber, run.PeriodLabel,
        run.PeriodStart, run.PeriodEnd, run.PayDate,
        run.Frequency, run.Status,
        run.EmployeeCount, run.TotalGrossPay, run.TotalNetPay,
        run.EmployeesMissingAttendance, run.CreatedAt);

    private static PayrollRunEmployeeDto ToEmployeeDto(PayrollRunEmployee e) => new(
        e.Id, e.EmployeeId, e.Employee?.FullName ?? string.Empty,
        e.Employee?.EmployeeNumber ?? string.Empty, e.DaysWorked,
        e.GrossPay, e.TotalDeductions, e.NetPay,
        e.RegularPay, e.OvertimePay, e.HolidayPay, e.NightDiffPay,
        e.TaxableAllowances, e.NonTaxableAllowances, e.ThirteenthMonth,
        e.AbsenceDeduction, e.TardinessDeduction,
        e.SSSEmployee, e.SSSEmployer, e.PhilHealthEmployee, e.PhilHealthEmployer,
        e.PagIbigEmployee, e.PagIbigEmployer, e.WithholdingTax, e.LoanDeductions, e.OtherDeductions);
}
