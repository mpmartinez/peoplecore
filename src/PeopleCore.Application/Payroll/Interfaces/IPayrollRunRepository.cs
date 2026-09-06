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
}
