using Microsoft.EntityFrameworkCore;
using Npgsql;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class SeparationRepository : ISeparationRepository
{
    /// <summary>Postgres SQLSTATE for a unique-constraint violation.</summary>
    private const string UniqueViolation = "23505";

    private readonly AppDbContext _context;

    public SeparationRepository(AppDbContext context) => _context = context;

    private IQueryable<Separation> WithDetails() =>
        _context.Separations.Include(s => s.ClearanceItems).Include(s => s.Employee).ThenInclude(e => e.Position)
            .Include(s => s.FinalPayRun);

    public async Task<Separation?> GetAsync(Guid id, CancellationToken ct = default)
        => await WithDetails().FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<Separation?> GetOpenForEmployeeAsync(Guid employeeId, CancellationToken ct = default)
        => await WithDetails().FirstOrDefaultAsync(s => s.EmployeeId == employeeId, ct);

    public async Task<IReadOnlyList<Separation>> ListAsync(CancellationToken ct = default)
        => await WithDetails().OrderByDescending(s => s.LastWorkingDay).AsSplitQuery().ToListAsync(ct);

    public async Task<IReadOnlyList<Separation>> GetForEmployeesAsync(IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default)
        => employeeIds.Count == 0
            ? []
            : await _context.Separations.Include(s => s.Employee).Include(s => s.FinalPayRun)
                .Where(s => employeeIds.Contains(s.EmployeeId))
                .ToListAsync(ct);

    /// <summary>
    /// Two HR users recording the same employee's separation at once both pass
    /// SeparationService's "does this employee already have one" check before either commits, so
    /// the unique index on <see cref="Separation.EmployeeId"/> is the real guard, not that check.
    /// The loser of the race gets the same <see cref="DomainException"/> the check itself throws,
    /// not a raw constraint-violation 500.
    /// </summary>
    public async Task AddAsync(Separation separation, CancellationToken ct = default)
    {
        _context.Separations.Add(separation);
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            var employee = separation.Employee ?? await _context.Employees.FindAsync([separation.EmployeeId], ct);
            throw new DomainException($"{employee?.FullName ?? "This employee"} already has a separation recorded.");
        }
    }

    public async Task AddClearanceItemAsync(SeparationClearanceItem item, CancellationToken ct = default)
    {
        // _context.Add, not separation.ClearanceItems.Add + SaveAsync: see the XML doc on the
        // interface method for why the collection route fails with a concurrency error instead of
        // inserting.
        _context.Add(item);
        await _context.SaveChangesAsync(ct);
    }

    public Task SaveAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);

    public async Task DeleteAsync(Separation separation, CancellationToken ct = default)
    {
        _context.Separations.Remove(separation);
        await _context.SaveChangesAsync(ct);
    }
}
