using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Domain.Entities.Payroll;
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

        Context.PayrollRuns.Update(run);
        await Context.PayrollRunEmployees.AddRangeAsync(newEntries, ct);

        await Context.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
