using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Interfaces;
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

    public PayrollRunService(
        IPayrollRunRepository runRepo,
        IEmployeeCompensationRepository compensationRepo,
        IEmployeeAllowanceRepository allowanceRepo,
        IEmployeeLoanRepository loanRepo,
        IPayrollSettingsRepository settingsRepo,
        PayrollComputationService computationService)
    {
        _runRepo = runRepo;
        _compensationRepo = compensationRepo;
        _allowanceRepo = allowanceRepo;
        _loanRepo = loanRepo;
        _settingsRepo = settingsRepo;
        _computationService = computationService;
    }

    public async Task<PayrollRunDto> CreateAsync(CreatePayrollRunRequest request, CancellationToken ct = default)
    {
        if (request.Employees is not { Count: > 0 })
            throw new DomainException("A payroll run must include at least one employee.");

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

        run.Employees = await ComputeEntriesAsync(run, request.Employees, ct);

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

        // Recompute against current rates/settings, but keep the per-employee inputs already on
        // the run - DaysWorked, OvertimeHours, HolidayDays and IncludeThirteenthMonth are Phase 1
        // explicit inputs (see PayrollRunEmployeeInput's remarks), not derived from anything a
        // recompute could re-derive, so re-defaulting them here would silently discard whatever
        // was recorded when the run was created.
        var employeeInputs = run.Employees
            .Select(e => new PayrollRunEmployeeInput(
                e.EmployeeId, e.DaysWorked, e.OvertimeHours, e.HolidayDays, e.IncludeThirteenthMonth))
            .ToList();

        var entries = await ComputeEntriesAsync(run, employeeInputs, ct);

        // The figures an approver would be asked to sign off on have changed, so any submission
        // no longer stands and the run goes back to draft to be resubmitted.
        run.Status = PayrollRunStatus.Draft;
        run.UpdatedAt = DateTime.UtcNow;

        await _runRepo.ReplaceEntriesAsync(run, entries, ct);
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

    /// <summary>
    /// Computes an entry per employee. Shared by CreateAsync and ComputeAsync so the two can
    /// never drift on rates or the rate basis.
    /// </summary>
    private async Task<List<PayrollRunEmployee>> ComputeEntriesAsync(
        PayrollRun run, IReadOnlyList<PayrollRunEmployeeInput> employees, CancellationToken ct)
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

        // Scheduled days for the period when a caller does not override DaysWorked;
        // attendance-driven absences are Phase 2 (see PayrollComputationService's own remarks
        // on PayrollAttendanceInput).
        decimal defaultDaysInPeriod = run.Frequency == PayFrequency.SemiMonthly ? 11m : 22m;

        var entries = new List<PayrollRunEmployee>();
        foreach (var employee in employees)
        {
            var compensation = compensations.FirstOrDefault(c => c.EmployeeId == employee.EmployeeId)
                ?? throw new DomainException($"Employee {employee.EmployeeId} has no compensation record.");

            // Populated from their own repositories - EmployeeCompensation.Allowances and
            // .Loans are not mapped in EF (see that entity's remarks).
            compensation.Allowances = allowancesByEmployee[employee.EmployeeId].ToList();
            compensation.Loans = loansByEmployee[employee.EmployeeId].ToList();

            entries.Add(_computationService.Compute(
                compensation, run,
                daysWorked: employee.DaysWorked ?? defaultDaysInPeriod,
                overtimeHours: employee.OvertimeHours,
                holidayDays: employee.HolidayDays,
                includeThirteenthMonth: employee.IncludeThirteenthMonth,
                rates: rates, dailyRateFactor: dailyRateFactor));
        }

        return entries;
    }

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

    private static PayrollRunEmployeeDto ToEmployeeDto(PayrollRunEmployee e) => new(
        e.Id, e.EmployeeId, e.Employee?.FullName ?? string.Empty,
        e.GrossPay, e.TotalDeductions, e.NetPay,
        e.RegularPay, e.OvertimePay, e.HolidayPay, e.NightDiffPay,
        e.TaxableAllowances, e.NonTaxableAllowances, e.ThirteenthMonth,
        e.SSSEmployee, e.SSSEmployer, e.PhilHealthEmployee, e.PhilHealthEmployer,
        e.PagIbigEmployee, e.PagIbigEmployer, e.WithholdingTax, e.LoanDeductions, e.OtherDeductions);
}
