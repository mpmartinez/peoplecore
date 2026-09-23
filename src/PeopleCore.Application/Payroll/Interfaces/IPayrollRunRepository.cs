using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities.Payroll;

namespace PeopleCore.Application.Payroll.Interfaces;

public interface IPayrollRunRepository : IRepository<PayrollRun>
{
    /// <summary>
    /// Loads a run together with its employee entries, each entry's Employee (for display) and
    /// its loan deduction lines - everything MarkPaidAsync, ComputeAsync and GetAsync need.
    /// </summary>
    Task<PayrollRun?> GetWithEntriesAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Regular runs already created for a period's year, for the next sequential PAY- run number.
    /// Final-pay runs are numbered on their own sequence (<see cref="GetLastFinalPaySequenceAsync"/>)
    /// and don't count here, so they leave no gaps in the PAY- numbers.
    /// </summary>
    Task<int> CountForYearAsync(int year, CancellationToken ct = default);

    /// <summary>
    /// The highest sequence among the FP-{<paramref name="payYear"/>}-nnn run numbers, or 0 if
    /// there are none, for the next FP- run number. It is the highest number and not a count
    /// because a run whose pay date moves to another year is renumbered onto that year's
    /// sequence, leaving a gap a count would fill with a number still in use.
    /// </summary>
    Task<int> GetLastFinalPaySequenceAsync(int payYear, CancellationToken ct = default);

    /// <summary>
    /// Saves a new final-pay run - with its entry, its <see cref="PayrollRun.FinalPayInputs"/> and
    /// their deductions - and links <paramref name="separation"/> to it, in one transaction.
    /// The link is only made while the separation has no final-pay run: if another request linked
    /// one first, nothing is saved and a <see cref="Domain.Exceptions.DomainException"/> is thrown,
    /// so two HR users can't give one separation two final pays.
    /// </summary>
    Task AddFinalPayRunAsync(PayrollRun run, Domain.Entities.Employees.Separation separation, CancellationToken ct = default);

    /// <summary>
    /// The paid run that paid <paramref name="employeeId"/> for a period containing <paramref name="date"/>,
    /// if any. Attendance on such a day is settled: changing it would leave the payslip disagreeing
    /// with the attendance it was computed from.
    /// </summary>
    Task<PayrollRun?> GetPaidRunCoveringAsync(Guid employeeId, DateOnly date, CancellationToken ct = default);

    /// <summary>
    /// A page of runs, newest first, together with entries so EmployeeCount/TotalGrossPay/
    /// TotalNetPay can be evaluated - see PayrollRunSummaryDto's remarks for why the totals are
    /// not instead computed in SQL.
    /// </summary>
    Task<(IReadOnlyList<PayrollRun> Items, int TotalCount)> GetPagedAsync(
        int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// Persists a brand-new run together with its freshly computed entries. Both DbSets are
    /// added explicitly rather than left to the Employees navigation: PayrollComputationService
    /// assigns each entry's Id before it is added, and an entity with a key already set that is
    /// only reached through a tracked parent's collection is mistaken by EF for an existing row,
    /// producing an UPDATE against a row that was never inserted.
    /// </summary>
    Task AddWithEntriesAsync(PayrollRun run, CancellationToken ct = default);

    /// <summary>
    /// Recomputes a run in place: deletes its current entries (cascading their loan deduction
    /// lines) and inserts <paramref name="newEntries"/>, alongside the run's own updated fields
    /// (Status, UpdatedAt, ...), in one transaction so a failure between the two steps cannot
    /// leave the run with no entries at all.
    /// <para>
    /// For a final-pay run, the loaded <see cref="PayrollRun.FinalPayInputs"/> is saved too, and its
    /// Deductions collection as it stands: deductions no longer in it are deleted and new ones are
    /// inserted (never mistaken for existing rows because their keys are already set).
    /// </para>
    /// </summary>
    Task ReplaceEntriesAsync(PayrollRun run, IReadOnlyList<PayrollRunEmployee> newEntries, CancellationToken ct = default);

    /// <summary>
    /// Deletes one entry from a loaded run (cascading its loan deduction lines and premium days),
    /// saving the run's own changed fields (Status, UpdatedAt) with it.
    /// </summary>
    Task RemoveEntryAsync(PayrollRun run, PayrollRunEmployee entry, CancellationToken ct = default);

    /// <summary>
    /// Every run the given employee appears in, newest pay date first, each loaded with its full
    /// <see cref="PayrollRun.Employees"/> list rather than a pre-filtered single entry. The
    /// caller is expected to pick out just its own entry from each run - the same thing
    /// PayslipService.GenerateAsync already does for a single run - so a run object is never
    /// itself a channel for another employee's figures to leak out, even if this query were
    /// ever widened to eager-load more per entry.
    /// </summary>
    Task<IReadOnlyList<PayrollRun>> GetRunsForEmployeeAsync(Guid employeeId, CancellationToken ct = default);

    /// <summary>
    /// The calendar years in which the employee appears in at least one <see cref="Domain.Enums.PayrollRunStatus.Paid"/>
    /// run, newest first, keyed on <see cref="PayrollRun.PayDate"/> rather than
    /// <see cref="PayrollRun.PeriodStart"/>. BIR taxes compensation in the year it is PAID, so a
    /// December-into-January period paid on 15 January is the later year's income - and in a
    /// semi-monthly cycle that is the ordinary case, not an edge case.
    /// </summary>
    Task<IReadOnlyList<int>> GetPaidYearsForEmployeeAsync(Guid employeeId, CancellationToken ct = default);

    /// <summary>
    /// Every <see cref="Domain.Enums.PayrollRunStatus.Paid"/> run the employee appears in whose
    /// <see cref="PayrollRun.PayDate"/> falls in <paramref name="year"/>, oldest pay date first,
    /// each loaded with its full <see cref="PayrollRun.Employees"/> list - the same shape, and for
    /// the same reason, as <see cref="GetRunsForEmployeeAsync"/>: the caller picks out its own
    /// entry, so a run object is never itself a channel for another employee's figures.
    /// <para>
    /// Only Paid runs are returned. A computed-but-unapproved run is not income yet, and
    /// <c>PayrollRunService.ComputeAsync</c> resets a run to Draft on every recompute, so
    /// admitting anything earlier would let a tax certificate move after it was issued.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<PayrollRun>> GetPaidRunsForEmployeeInYearAsync(
        Guid employeeId, int year, CancellationToken ct = default);

    /// <summary>
    /// Every distinct employee id appearing in a <see cref="Domain.Enums.PayrollRunStatus.Paid"/>
    /// run whose <see cref="PayrollRun.PayDate"/> falls in <paramref name="year"/>, ordered by the
    /// employee's last name then first name. Backs GenerateAll's "every employee's 2316 for the
    /// year" bulk action - the mirror image of <see cref="GetPaidYearsForEmployeeAsync"/> (years
    /// for one employee) and <see cref="GetPaidRunsForEmployeeInYearAsync"/> (runs for one
    /// employee in one year): this one runs across employees instead of across years, but keeps
    /// the same Paid-only, pay-date-year rule so a certificate can never be generated for an
    /// unapproved run's figures or attributed to the wrong tax year.
    /// <para>
    /// The name ordering is deliberate, not incidental: without it, a merged PDF's page order is
    /// whatever order the database happens to return, and two exports of the same year are not
    /// comparable page-for-page.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<Guid>> GetEmployeeIdsWithPaidRunsInYearAsync(int year, CancellationToken ct = default);

    /// <summary>
    /// Every <see cref="Domain.Enums.PayrollRunStatus.Paid"/> run whose <see cref="PayrollRun.PayDate"/>
    /// falls in <paramref name="year"/>, across ALL employees, each loaded with its full
    /// <see cref="PayrollRun.Employees"/> list. Backs <c>Bir2316Service.BuildAllAsync</c>: fetching
    /// every employee's runs in one query and grouping the entries in memory is what lets a bulk
    /// "generate all" run avoid re-querying (and re-materialising every OTHER employee's entries
    /// out of) the same year's runs once per employee.
    /// </summary>
    Task<IReadOnlyList<PayrollRun>> GetPaidRunsInYearAsync(int year, CancellationToken ct = default);

    /// <summary>
    /// Every Paid run whose period ends in the month, each loaded with its entries, their employees
    /// and those employees' government IDs. SSS, PhilHealth and Pag-IBIG contributions are for the
    /// month the pay was earned, so a Dec 16-31 cutoff paid on 5 January belongs to December.
    /// </summary>
    Task<IReadOnlyList<PayrollRun>> GetPaidRunsByPeriodEndMonthAsync(int year, int month, CancellationToken ct = default);

    /// <summary>
    /// Every Paid run paid in the month, loaded as <see cref="GetPaidRunsByPeriodEndMonthAsync"/>.
    /// BIR 1601-C reports tax withheld in the month compensation was paid.
    /// </summary>
    Task<IReadOnlyList<PayrollRun>> GetPaidRunsByPayMonthAsync(int year, int month, CancellationToken ct = default);

    /// <summary>
    /// Runs in the month, on the given basis, that are not Paid yet, so a report can say what it
    /// leaves out.
    /// </summary>
    Task<int> CountUnpaidRunsAsync(int year, int month, bool byPayDate, CancellationToken ct = default);

    /// <summary>
    /// Runs whose pay date falls in the year that aren't Paid yet, for the 1604-C's "not included" note.
    /// </summary>
    Task<int> CountUnpaidRunsPaidInYearAsync(int year, CancellationToken ct = default);
}
