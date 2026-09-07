using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class PayrollRunRepository : Repository<PayrollRun>, IPayrollRunRepository
{
    public PayrollRunRepository(AppDbContext context) : base(context) { }

    public async Task<PayrollRun?> GetWithEntriesAsync(Guid id, CancellationToken ct = default)
        => await Context.PayrollRuns
            .Include(r => r.Employees).ThenInclude(e => e.Employee)
            .Include(r => r.Employees).ThenInclude(e => e.LoanDeductionLines)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<int> CountForYearAsync(int year, CancellationToken ct = default)
        => await Context.PayrollRuns.CountAsync(r => r.PeriodStart.Year == year, ct);

    public async Task<IReadOnlyList<PayrollRun>> GetRunsForEmployeeAsync(Guid employeeId, CancellationToken ct = default)
        => await Context.PayrollRuns
            .Where(r => r.Employees.Any(e => e.EmployeeId == employeeId))
            .Include(r => r.Employees)
            .OrderByDescending(r => r.PayDate)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<int>> GetPaidYearsForEmployeeAsync(Guid employeeId, CancellationToken ct = default)
        => await Context.PayrollRuns
            .Where(r => r.Status == PayrollRunStatus.Paid && r.Employees.Any(e => e.EmployeeId == employeeId))
            .Select(r => r.PayDate.Year)
            .Distinct()
            .OrderByDescending(y => y)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<PayrollRun>> GetPaidRunsForEmployeeInYearAsync(
        Guid employeeId, int year, CancellationToken ct = default)
        => await Context.PayrollRuns
            .Where(r => r.Status == PayrollRunStatus.Paid
                        && r.PayDate.Year == year
                        && r.Employees.Any(e => e.EmployeeId == employeeId))
            .Include(r => r.Employees)
            .OrderBy(r => r.PayDate)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<PayrollRun> Items, int TotalCount)> GetPagedAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        var query = Context.PayrollRuns.AsQueryable();
        var total = await query.CountAsync(ct);

        // Entries are included so the run's computed totals can be evaluated, then projected
        // away by the service - the totals live on the entity so they cannot drift.
        var items = await query
            .Include(r => r.Employees)
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task AddWithEntriesAsync(PayrollRun run, CancellationToken ct = default)
    {
        // Added to both DbSets explicitly - see the interface's doc comment for why the
        // Employees navigation alone is not enough once Compute has assigned each entry's Id.
        await Context.PayrollRuns.AddAsync(run, ct);
        await Context.PayrollRunEmployees.AddRangeAsync(run.Employees, ct);
        await Context.SaveChangesAsync(ct);
    }

    public async Task ReplaceEntriesAsync(
        PayrollRun run, IReadOnlyList<PayrollRunEmployee> newEntries, CancellationToken ct = default)
    {
        // Both steps share a transaction: a failure between them would otherwise leave the run
        // with no entries at all. Deleting the entries cascades to their loan deduction lines.
        await using var tx = await Context.Database.BeginTransactionAsync(ct);

        await Context.PayrollRunEmployees
            .Where(e => e.PayrollRunId == run.Id)
            .ExecuteDeleteAsync(ct);

        // ExecuteDeleteAsync bypasses the change tracker, so the deleted entries are still
        // attached to run.Employees. Detach them before adding the replacements, or EF will
        // try to update rows that no longer exist.
        foreach (var stale in run.Employees.ToList())
            Context.Entry(stale).State = EntityState.Detached;
        run.Employees.Clear();

        await Context.PayrollRunEmployees.AddRangeAsync(newEntries, ct);

        await Context.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
