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

    public Task<int> GetLastRegularSequenceAsync(int year, CancellationToken ct = default)
        => GetLastSequenceAsync(PayrollRunType.Regular, $"PAY-{year}-", ct);

    public Task<int> GetLastFinalPaySequenceAsync(int payYear, CancellationToken ct = default)
        => GetLastSequenceAsync(PayrollRunType.FinalPay, $"FP-{payYear}-", ct);

    /// <summary>
    /// The highest numeric suffix among the run numbers of the type that start with the prefix, or
    /// 0. Keyed on the number itself, as the unique index on RunNumber is: a year holds a few dozen
    /// runs at most, so their suffixes are parsed here rather than in SQL.
    /// </summary>
    private async Task<int> GetLastSequenceAsync(PayrollRunType type, string prefix, CancellationToken ct)
    {
        var numbers = await Context.PayrollRuns
            .Where(r => r.RunType == type && r.RunNumber.StartsWith(prefix))
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

    public async Task SavePaidAsync(PayrollRun run, IReadOnlyCollection<EmployeeLoan> loans,
        IReadOnlyCollection<Domain.Entities.Leave.LeaveBalance> leaveBalances,
        IReadOnlyCollection<MaternityClaim> maternityClaims, CancellationToken ct = default)
    {
        // Normally all of them were loaded through this request's context and are tracked already;
        // any that weren't are attached as existing rows to update. One SaveChanges is one
        // transaction, so the run can't be Paid without its loans retired, its leave used and its
        // maternity advances recorded.
        Attach(run);
        foreach (var loan in loans)
            Attach(loan);
        foreach (var balance in leaveBalances)
            Attach(balance);
        foreach (var claim in maternityClaims)
            Attach(claim);
        await Context.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<MaternityAdvanceInRun>> GetMaternityAdvancesAsync(
        IReadOnlyCollection<Guid> claimIds, Guid excludeRunId, CancellationToken ct = default)
    {
        var ids = claimIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        return await Context.PayrollRunEmployees
            .Where(e => e.MaternityClaimId != null && ids.Contains(e.MaternityClaimId.Value)
                        && e.AdvanceMaternityBenefit
                        && e.MaternityBenefitAdvance > 0m
                        && e.PayrollRunId != excludeRunId)
            .OrderBy(e => e.PayrollRun.PayDate)
            .Select(e => new MaternityAdvanceInRun(e.MaternityClaimId!.Value, e.PayrollRun.RunNumber))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DeferredContributionsOutstanding>> GetDeferredContributionsOutstandingAsync(
        IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default)
    {
        var ids = employeeIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        var sums = await Context.PayrollRunEmployees
            .Where(e => ids.Contains(e.EmployeeId) && e.PayrollRun.Status == PayrollRunStatus.Paid
                        && (e.ContributionsDeferred != 0m || e.DeferredContributionsCollected != 0m))
            .GroupBy(e => e.EmployeeId)
            .Select(g => new { EmployeeId = g.Key, Amount = g.Sum(e => e.ContributionsDeferred - e.DeferredContributionsCollected) })
            .ToListAsync(ct);
        return sums
            .Where(s => s.Amount != 0m)
            .Select(s => new DeferredContributionsOutstanding(s.EmployeeId, s.Amount))
            .ToList();
    }

    public async Task<IReadOnlyList<string>> GetPaidRunsNettingMaternityClaimAsync(Guid claimId, CancellationToken ct = default)
    {
        var runs = await Context.PayrollRunEmployees
            .Where(e => e.MaternityClaimId == claimId && e.MaternityBenefitOffset > 0m
                        && e.PayrollRun.Status == PayrollRunStatus.Paid)
            .Select(e => new { e.PayrollRun.RunNumber, e.PayrollRun.PayDate })
            .Distinct()
            .ToListAsync(ct);
        return runs.OrderBy(r => r.PayDate).ThenBy(r => r.RunNumber).Select(r => r.RunNumber).ToList();
    }

    public async Task<IReadOnlyList<MaternityNettingInRun>> GetPaidRunsNettingMaternityClaimsAsync(
        IReadOnlyCollection<Guid> claimIds, CancellationToken ct = default)
    {
        var ids = claimIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        var rows = await Context.PayrollRunEmployees
            .Where(e => e.MaternityClaimId != null && ids.Contains(e.MaternityClaimId.Value)
                        && e.MaternityBenefitOffset > 0m && e.PayrollRun.Status == PayrollRunStatus.Paid)
            .Select(e => new { ClaimId = e.MaternityClaimId!.Value, e.PayrollRun.RunNumber, e.PayrollRun.PayDate })
            .Distinct()
            .ToListAsync(ct);
        return rows.OrderBy(r => r.PayDate).ThenBy(r => r.RunNumber)
            .Select(r => new MaternityNettingInRun(r.ClaimId, r.RunNumber))
            .ToList();
    }

    public async Task<IReadOnlyList<string>> GetPaidRunsCoveringPeriodAsync(Guid employeeId, DateOnly from, DateOnly to,
        CancellationToken ct = default)
    {
        var runs = await Context.PayrollRunEmployees
            .Where(e => e.EmployeeId == employeeId && e.PayrollRun.Status == PayrollRunStatus.Paid
                        && e.PayrollRun.PeriodStart <= to && e.PayrollRun.PeriodEnd >= from)
            .Select(e => new { e.PayrollRun.RunNumber, e.PayrollRun.PayDate })
            .Distinct()
            .ToListAsync(ct);
        return runs.OrderBy(r => r.PayDate).ThenBy(r => r.RunNumber).Select(r => r.RunNumber).ToList();
    }

    public async Task<IReadOnlyList<string>> GetPaidRunsOffsettingPeriodAsync(Guid employeeId, DateOnly from, DateOnly to,
        CancellationToken ct = default)
    {
        var runs = await Context.PayrollRunEmployees
            .Where(e => e.EmployeeId == employeeId && e.MaternityBenefitOffset > 0m
                        && e.PayrollRun.Status == PayrollRunStatus.Paid
                        && e.PayrollRun.PeriodStart <= to && e.PayrollRun.PeriodEnd >= from)
            .Select(e => new { e.PayrollRun.RunNumber, e.PayrollRun.PayDate })
            .Distinct()
            .ToListAsync(ct);
        return runs.OrderBy(r => r.PayDate).ThenBy(r => r.RunNumber).Select(r => r.RunNumber).ToList();
    }

    private void Attach<TEntity>(TEntity entity) where TEntity : class
    {
        if (Context.Entry(entity).State == EntityState.Detached)
            Context.Set<TEntity>().Update(entity);
    }

    public async Task<IReadOnlyList<LeaveConvertedInRun>> GetLeaveConversionsInYearAsync(
        int periodEndYear, IReadOnlyCollection<Guid> employeeIds, Guid excludeRunId, CancellationToken ct = default)
    {
        var first = new DateOnly(periodEndYear, 1, 1);
        var next = first.AddYears(1);
        var ids = employeeIds.ToList();
        return await Context.PayrollRunEmployees
            .Where(e => ids.Contains(e.EmployeeId)
                        && e.LeaveConversionPay > 0m
                        && e.PayrollRunId != excludeRunId
                        && e.PayrollRun.RunType == PayrollRunType.Regular
                        && e.PayrollRun.PeriodEnd >= first && e.PayrollRun.PeriodEnd < next)
            .OrderBy(e => e.PayrollRun.PeriodEnd)
            .Select(e => new LeaveConvertedInRun(e.EmployeeId, e.PayrollRun.RunNumber))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ThirteenthMonthInRun>> GetUnpaidThirteenthMonthsInYearAsync(
        int payYear, IReadOnlyCollection<Guid> employeeIds, Guid excludeRunId, CancellationToken ct = default)
    {
        var first = new DateOnly(payYear, 1, 1);
        var next = first.AddYears(1);
        var ids = employeeIds.ToList();
        return await Context.PayrollRunEmployees
            .Where(e => ids.Contains(e.EmployeeId)
                        && e.IncludeThirteenthMonth
                        && e.PayrollRunId != excludeRunId
                        && e.PayrollRun.RunType == PayrollRunType.Regular
                        && e.PayrollRun.Status != PayrollRunStatus.Paid
                        && e.PayrollRun.PayDate >= first && e.PayrollRun.PayDate < next)
            .OrderBy(e => e.PayrollRun.PayDate)
            .Select(e => new ThirteenthMonthInRun(e.EmployeeId, e.PayrollRun.RunNumber))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<UnpaidRunEntry>> GetUnpaidRegularRunEntriesInYearAsync(int payYear,
        CancellationToken ct = default)
    {
        // Jan 1 to Dec 31 rather than up to the next Jan 1: an opening balance can be for 9999,
        // whose next year DateOnly can't hold. Nor the pay date's year: Npgsql stores Dec 31, 9999
        // (DateOnly.MaxValue) as 'infinity', whose year Postgres can't make an integer of.
        var first = new DateOnly(payYear, 1, 1);
        var last = new DateOnly(payYear, 12, 31);
        return await Context.PayrollRunEmployees
            .Where(e => e.PayrollRun.RunType == PayrollRunType.Regular
                        && e.PayrollRun.Status != PayrollRunStatus.Paid
                        && e.PayrollRun.PayDate >= first && e.PayrollRun.PayDate <= last)
            .OrderBy(e => e.PayrollRun.PayDate).ThenBy(e => e.PayrollRun.RunNumber)
            .Select(e => new UnpaidRunEntry(e.EmployeeId, e.PayrollRun.RunNumber, e.PayrollRun.PayDate))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<EarlierUnpaidRun>> GetEarlierUnpaidRunsInYearAsync(
        int payYear, DateOnly payDate, IReadOnlyCollection<Guid> employeeIds, Guid excludeRunId,
        CancellationToken ct = default)
    {
        var first = new DateOnly(payYear, 1, 1);
        var ids = employeeIds.ToList();
        return await Context.PayrollRunEmployees
            .Where(e => ids.Contains(e.EmployeeId)
                        && e.PayrollRunId != excludeRunId
                        && e.PayrollRun.RunType == PayrollRunType.Regular
                        && e.PayrollRun.Status != PayrollRunStatus.Paid
                        && e.PayrollRun.PayDate >= first && e.PayrollRun.PayDate < payDate)
            .OrderBy(e => e.PayrollRun.PayDate)
            .Select(e => new EarlierUnpaidRun(e.EmployeeId, e.PayrollRun.RunNumber))
            .ToListAsync(ct);
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

    public async Task RemoveEntryAsync(PayrollRun run, PayrollRunEmployee entry, CancellationToken ct = default)
    {
        // Removed through its own DbSet: the database cascades the entry's loan deduction lines and
        // premium days, and EF deletes the tracked ones with it. The run's own changes are saved
        // alongside, as it is tracked.
        run.Employees.Remove(entry);
        Context.PayrollRunEmployees.Remove(entry);
        await Context.SaveChangesAsync(ct);
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
