using Microsoft.EntityFrameworkCore;
using Npgsql;
using PeopleCore.Application.Payroll.OpeningBalances;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class PayrollOpeningBalanceRepository : Repository<PayrollOpeningBalance>, IPayrollOpeningBalanceRepository
{
    /// <summary>Postgres SQLSTATE for a unique-constraint violation.</summary>
    private const string UniqueViolation = "23505";

    public PayrollOpeningBalanceRepository(AppDbContext context) : base(context) { }

    public async Task<PayrollOpeningBalance> AddNewAsync(PayrollOpeningBalance balance, CancellationToken ct = default)
    {
        Context.PayrollOpeningBalances.Add(balance);
        try
        {
            await Context.SaveChangesAsync(ct);
            return balance;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            // Detached so a later save on this context doesn't try the insert again.
            Context.Entry(balance).State = EntityState.Detached;
            var employee = balance.Employee ?? await Context.Employees.FindAsync([balance.EmployeeId], ct);
            throw new DomainException(
                $"{employee?.FullName ?? "This employee"} already has an opening balance for {balance.Year}.");
        }
    }

    public async Task SaveAllAsync(IReadOnlyCollection<PayrollOpeningBalance> added, CancellationToken ct = default)
    {
        Context.PayrollOpeningBalances.AddRange(added);
        try
        {
            // One SaveChanges: EF sends its inserts and updates in one transaction.
            await Context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            // Dropped, so a later save on this context doesn't try the inserts again. The loaded
            // balances' changes stay pending; nothing else saves in an import's scope.
            foreach (var balance in added)
                Context.Entry(balance).State = EntityState.Detached;
            throw new DomainException(
                "Someone else added an opening balance for an employee in this file. Import it again.");
        }
    }

    /// <summary>The balance with its employee loaded.</summary>
    public override async Task<PayrollOpeningBalance?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await Context.PayrollOpeningBalances
            .Include(b => b.Employee)
            .FirstOrDefaultAsync(b => b.Id == id, ct);

    public async Task<PayrollOpeningBalance?> GetAsync(Guid employeeId, int year, CancellationToken ct = default)
        => await Context.PayrollOpeningBalances
            .Include(b => b.Employee)
            .FirstOrDefaultAsync(b => b.EmployeeId == employeeId && b.Year == year, ct);

    public async Task<IReadOnlyList<PayrollOpeningBalance>> GetForEmployeesAsync(
        IReadOnlyCollection<Guid> employeeIds, int year, CancellationToken ct = default)
    {
        var ids = employeeIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        return await Context.PayrollOpeningBalances
            .Where(b => b.Year == year && ids.Contains(b.EmployeeId))
            .Include(b => b.Employee)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PayrollOpeningBalance>> GetForYearAsync(int year, CancellationToken ct = default)
        => await Context.PayrollOpeningBalances
            .Where(b => b.Year == year)
            .Include(b => b.Employee)
            .OrderBy(b => b.Employee.LastName)
            .ThenBy(b => b.Employee.FirstName)
            .ToListAsync(ct);
}
