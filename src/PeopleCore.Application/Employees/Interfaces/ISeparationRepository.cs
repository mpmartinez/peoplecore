using PeopleCore.Domain.Entities.Employees;

namespace PeopleCore.Application.Employees.Interfaces;

public interface ISeparationRepository
{
    Task<Separation?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Separation?> GetOpenForEmployeeAsync(Guid employeeId, CancellationToken ct = default);
    Task<IReadOnlyList<Separation>> ListAsync(CancellationToken ct = default);
    Task AddAsync(Separation separation, CancellationToken ct = default);

    /// <summary>
    /// Inserts a new clearance item on an already-recorded separation. Every <c>AuditableEntity</c>
    /// gets its Guid at construction, so a new item added straight into a tracked separation's
    /// <see cref="Separation.ClearanceItems"/> and saved via <see cref="SaveAsync"/> looks exactly
    /// like an existing row to EF's change tracking - the insert goes out as an UPDATE that matches
    /// no row and fails with a concurrency error. This method tracks the item as Added explicitly,
    /// so callers never have to hit that trap.
    /// </summary>
    Task AddClearanceItemAsync(SeparationClearanceItem item, CancellationToken ct = default);

    Task SaveAsync(CancellationToken ct = default);
    Task DeleteAsync(Separation separation, CancellationToken ct = default);
}
