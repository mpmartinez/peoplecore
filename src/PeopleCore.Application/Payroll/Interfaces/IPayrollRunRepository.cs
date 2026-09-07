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

    /// <summary>Runs already created for a period's year, for the next sequential run number.</summary>
    Task<int> CountForYearAsync(int year, CancellationToken ct = default);

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
    /// </summary>
    Task ReplaceEntriesAsync(PayrollRun run, IReadOnlyList<PayrollRunEmployee> newEntries, CancellationToken ct = default);

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
    /// run whose <see cref="PayrollRun.PayDate"/> falls in <paramref name="year"/>. Backs
    /// GenerateAll's "every employee's 2316 for the year" bulk action - the mirror image of
    /// <see cref="GetPaidYearsForEmployeeAsync"/> (years for one employee) and
    /// <see cref="GetPaidRunsForEmployeeInYearAsync"/> (runs for one employee in one year): this
    /// one runs across employees instead of across years, but keeps the same Paid-only,
    /// pay-date-year rule so a certificate can never be generated for an unapproved run's figures
    /// or attributed to the wrong tax year.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetEmployeeIdsWithPaidRunsInYearAsync(int year, CancellationToken ct = default);
}
