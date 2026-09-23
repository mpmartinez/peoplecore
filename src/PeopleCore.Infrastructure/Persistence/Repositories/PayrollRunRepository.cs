using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class PayrollRunRepository : Repository<PayrollRun>, IPayrollRunRepository
{
    public PayrollRunRepository(AppDbContext context) : base(context) { }

    public async Task<PayrollRun?> GetWithEntriesAsync(Guid id, CancellationToken ct = default)
        => await Context.PayrollRuns
            .Include(r => r.Employees).ThenInclude(e => e.Employee)
            .Include(r => r.Employees).ThenInclude(e => e.LoanDeductionLines)
            .Include(r => r.Employees).ThenInclude(e => e.PremiumDays)
            .Include(r => r.FinalPayInputs).ThenInclude(fp => fp!.Deductions)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<int> CountForYearAsync(int year, CancellationToken ct = default)
        => await Context.PayrollRuns.CountAsync(
            r => r.RunType == PayrollRunType.Regular && r.PeriodStart.Year == year, ct);

    public async Task<int> GetLastFinalPaySequenceAsync(int payYear, CancellationToken ct = default)
    {
        // Keyed on the number itself, as the unique index on RunNumber is: a year holds only a
        // handful of final pays, so their suffixes are parsed here rather than in SQL.
        var prefix = $"FP-{payYear}-";
        var numbers = await Context.PayrollRuns
            .Where(r => r.RunType == PayrollRunType.FinalPay && r.RunNumber.StartsWith(prefix))
            .Select(r => r.RunNumber)
            .ToListAsync(ct);

        return numbers
            .Select(n => int.TryParse(n.AsSpan(prefix.Length), out var sequence) ? sequence : 0)
            .DefaultIfEmpty(0)
            .Max();
    }

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

    public async Task<IReadOnlyList<Guid>> GetEmployeeIdsWithPaidRunsInYearAsync(int year, CancellationToken ct = default)
        => await Context.PayrollRuns
            .Where(r => r.Status == PayrollRunStatus.Paid && r.PayDate.Year == year)
            .SelectMany(r => r.Employees)
            .Select(e => new { e.EmployeeId, e.Employee!.LastName, e.Employee.FirstName })
            .Distinct()
            .OrderBy(e => e.LastName).ThenBy(e => e.FirstName)
            .Select(e => e.EmployeeId)
            .ToListAsync(ct);

    public async Task<PayrollRun?> GetPaidRunCoveringAsync(Guid employeeId, DateOnly date, CancellationToken ct = default)
        => await Context.PayrollRuns
            .Where(r => r.Status == PayrollRunStatus.Paid && r.PeriodStart <= date && r.PeriodEnd >= date
                        && r.Employees.Any(e => e.EmployeeId == employeeId))
            .OrderByDescending(r => r.PayDate)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<PayrollRun>> GetPaidRunsInYearAsync(int year, CancellationToken ct = default)
        => await Context.PayrollRuns
            .Where(r => r.Status == PayrollRunStatus.Paid && r.PayDate.Year == year)
            .Include(r => r.Employees)
            .OrderBy(r => r.PayDate)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<PayrollRun>> GetPaidRunsByPeriodEndMonthAsync(int year, int month, CancellationToken ct = default)
    {
        var (first, next) = MonthRange(year, month);
        return await WithEmployeesAndIds(Context.PayrollRuns
                .Where(r => r.Status == PayrollRunStatus.Paid && r.PeriodEnd >= first && r.PeriodEnd < next))
            .OrderBy(r => r.PeriodEnd)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PayrollRun>> GetPaidRunsByPayMonthAsync(int year, int month, CancellationToken ct = default)
    {
        var (first, next) = MonthRange(year, month);
        return await WithEmployeesAndIds(Context.PayrollRuns
                .Where(r => r.Status == PayrollRunStatus.Paid && r.PayDate >= first && r.PayDate < next))
            .OrderBy(r => r.PayDate)
            .ToListAsync(ct);
    }

    public async Task<int> CountUnpaidRunsAsync(int year, int month, bool byPayDate, CancellationToken ct = default)
    {
        var (first, next) = MonthRange(year, month);
        var unpaid = Context.PayrollRuns.Where(r => r.Status != PayrollRunStatus.Paid);
        return await (byPayDate
            ? unpaid.CountAsync(r => r.PayDate >= first && r.PayDate < next, ct)
            : unpaid.CountAsync(r => r.PeriodEnd >= first && r.PeriodEnd < next, ct));
    }

    public async Task<int> CountUnpaidRunsPaidInYearAsync(int year, CancellationToken ct = default)
    {
        var first = new DateOnly(year, 1, 1);
        var next = first.AddYears(1);
        return await Context.PayrollRuns.CountAsync(r => r.Status != PayrollRunStatus.Paid && r.PayDate >= first && r.PayDate < next, ct);
    }

    // A half-open range so the index on the date column can be used, instead of the Year/Month
    // predicates it used to run as, which cannot.
    private static (DateOnly First, DateOnly Next) MonthRange(int year, int month)
    {
        var first = new DateOnly(year, month, 1);
        return (first, first.AddMonths(1));
    }

    private static IQueryable<PayrollRun> WithEmployeesAndIds(IQueryable<PayrollRun> runs)
        => runs.Include(r => r.Employees).ThenInclude(e => e.Employee!).ThenInclude(p => p.GovernmentIds)
               .AsSplitQuery();

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
        // FinalPayInputs and its Deductions get the same treatment for the same reason - every
        // AuditableEntity gets its Guid at construction, so a freshly built FinalPay run's inputs
        // graph is added explicitly rather than trusted to the reference/collection navigations.
        await Context.PayrollRuns.AddAsync(run, ct);
        await Context.PayrollRunEmployees.AddRangeAsync(run.Employees, ct);
        if (run.FinalPayInputs is not null)
        {
            await Context.FinalPayInputs.AddAsync(run.FinalPayInputs, ct);
            await Context.FinalPayDeductions.AddRangeAsync(run.FinalPayInputs.Deductions, ct);
        }
        await Context.SaveChangesAsync(ct);
    }

    public async Task AddFinalPayRunAsync(PayrollRun run, Separation separation, CancellationToken ct = default)
    {
        await using var tx = await Context.Database.BeginTransactionAsync(ct);

        // The link is written below by a conditional UPDATE, not by the change tracker: the
        // caller has already set separation.FinalPayRunId, and if the tracker saved that it would
        // overwrite a link another request made in the meantime. Marking the value as already
        // saved keeps SaveChanges from writing it. Change detection is off while doing so, or
        // Entry() would first run DetectChanges and flag the property Modified anyway.
        var autoDetect = Context.ChangeTracker.AutoDetectChangesEnabled;
        Context.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            var entry = Context.Entry(separation);
            if (entry.State != EntityState.Detached)
            {
                var link = entry.Property(s => s.FinalPayRunId);
                link.OriginalValue = link.CurrentValue;
                link.IsModified = false;
            }
        }
        finally
        {
            Context.ChangeTracker.AutoDetectChangesEnabled = autoDetect;
        }

        // Inserts the run, its entry and its inputs with their deductions - each added explicitly,
        // see AddWithEntriesAsync.
        await AddWithEntriesAsync(run, ct);

        // Links the separation only if nothing else has: a second final pay for one separation
        // must never be saved. Rolled back with the inserts above when it matches no row.
        var linked = await Context.Separations
            .Where(s => s.Id == separation.Id && s.FinalPayRunId == null)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.FinalPayRunId, run.Id), ct);
        if (linked == 0)
            throw new DomainException("This separation already has a final-pay run.");

        await tx.CommitAsync(ct);
    }

    public async Task ReplaceEntriesAsync(
        PayrollRun run, IReadOnlyList<PayrollRunEmployee> newEntries, CancellationToken ct = default)
    {
        // Both steps share a transaction: a failure between them would otherwise leave the run
        // with no entries at all. Deleting the entries cascades to their loan deduction lines and premium days.
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

        if (run.FinalPayInputs is { } inputs)
            TrackFinalPayDeductions(inputs);

        await Context.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// Brings the tracked deductions of a final-pay run's inputs in line with its Deductions
    /// collection: rows loaded earlier and since dropped from the collection are deleted, and new
    /// objects in it are inserted. Change detection is off throughout - left on, it would find the
    /// new deductions through the tracked inputs and, their keys already set, mark them Modified
    /// (an UPDATE of a row that doesn't exist) before they could be marked Added.
    /// </summary>
    private void TrackFinalPayDeductions(FinalPayInputs inputs)
    {
        var autoDetect = Context.ChangeTracker.AutoDetectChangesEnabled;
        Context.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            var current = new HashSet<FinalPayDeduction>(inputs.Deductions, ReferenceEqualityComparer.Instance);

            var dropped = Context.ChangeTracker.Entries<FinalPayDeduction>()
                .Where(e => e.Entity.FinalPayInputsId == inputs.Id && !current.Contains(e.Entity))
                .ToList();
            foreach (var entry in dropped)
                entry.State = entry.State == EntityState.Added ? EntityState.Detached : EntityState.Deleted;

            foreach (var deduction in inputs.Deductions)
            {
                var entry = Context.Entry(deduction);
                if (entry.State == EntityState.Detached)
                    entry.State = EntityState.Added;
            }
        }
        finally
        {
            Context.ChangeTracker.AutoDetectChangesEnabled = autoDetect;
        }
    }
}
