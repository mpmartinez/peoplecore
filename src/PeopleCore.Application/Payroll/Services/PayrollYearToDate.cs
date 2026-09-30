using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Application.Payroll.OpeningBalances;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;

namespace PeopleCore.Application.Payroll.Services;

/// <summary>
/// What an employee was paid earlier in a year, as the 13th month, the ₱90,000 exemption and the
/// de minimis leave-days cap need it: the year's Paid runs plus her opening balance for the year
/// (what she was paid before PeopleCore).
/// </summary>
/// <param name="BasicEarned">
/// Basic salary earned: the runs' <see cref="PayrollRunEmployee.RegularPay"/> plus the balance's
/// <see cref="PayrollOpeningBalance.BasicSalary"/>.
/// </param>
/// <param name="ThirteenthMonthPaid">
/// 13th month already paid: the runs' <see cref="PayrollRunEmployee.ThirteenthMonth"/> plus the
/// balance's <see cref="PayrollOpeningBalance.ThirteenthMonthPaid"/>.
/// </param>
/// <param name="ExemptUsed">
/// How much of the ₱90,000 exemption for 13th month and other benefits is used: the runs'
/// <see cref="PayrollRunEmployee.ThirteenthMonthAndOtherBenefits"/> plus the balance's
/// <see cref="PayrollOpeningBalance.ThirteenthMonthPaid"/> and
/// <see cref="PayrollOpeningBalance.OtherBenefitsPaid"/>.
/// </param>
/// <param name="DeMinimisLeaveDaysUsed">
/// Of the ten de minimis leave days, those already used: what the runs paid as de minimis
/// (<see cref="LeavePayout.DeMinimisDaysUsed"/>) plus the balance's
/// <see cref="PayrollOpeningBalance.DeMinimisLeaveDays"/>.
/// </param>
public sealed record YearToDate(decimal BasicEarned, decimal ThirteenthMonthPaid, decimal ExemptUsed,
    decimal DeMinimisLeaveDaysUsed)
{
    /// <summary>Nothing paid earlier in the year.</summary>
    public static readonly YearToDate None = new(0m, 0m, 0m, 0m);

    /// <summary>What's left of the ten de minimis leave days (<see cref="LeavePayout.DeMinimisDaysLeft"/>).</summary>
    public decimal DeMinimisLeaveDaysLeft => LeavePayout.DeMinimisDaysLeft(DeMinimisLeaveDaysUsed);
}

/// <summary>
/// The one source for "earlier this year": every figure PeopleCore adds up from a year's Paid runs
/// for an employee comes from here, so her opening balance for the year is added in one place.
/// Each method keeps the selection of runs its callers made before it existed.
/// </summary>
public interface IPayrollYearToDate
{
    /// <summary>
    /// Each employee's figures for <paramref name="year"/>: the Paid runs whose pay date falls in it
    /// (<see cref="IPayrollRunRepository.GetPaidRunsInYearAsync"/>), less
    /// <paramref name="excludeRunId"/>, plus her opening balance for the year. Every requested
    /// employee is in the result, with <see cref="YearToDate.None"/> when she has nothing. The
    /// runs and the balances are each read once.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, YearToDate>> ForAsync(IReadOnlyCollection<Guid> employeeIds, int year,
        Guid? excludeRunId, CancellationToken ct = default);

    /// <summary>
    /// One employee's figures for <paramref name="year"/>, as <see cref="ForAsync"/>, read with the
    /// query for her runs alone (<see cref="IPayrollRunRepository.GetPaidRunsForEmployeeInYearAsync"/>)
    /// rather than every employee's - what final pay, which is one employee's, reads.
    /// </summary>
    Task<YearToDate> ForEmployeeAsync(Guid employeeId, int year, Guid? excludeRunId, CancellationToken ct = default);

    /// <summary>
    /// Each employee's figures for <paramref name="before"/>'s year from the Paid runs paid before
    /// it, plus her opening balance for the year - what a month's 1601-C treats as paid earlier in
    /// the year when it is given the month's first day. Every requested employee is in the result.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, YearToDate>> PaidBeforeAsync(IReadOnlyCollection<Guid> employeeIds, DateOnly before,
        CancellationToken ct = default);
}

public sealed class PayrollYearToDate : IPayrollYearToDate
{
    private readonly IPayrollRunRepository _runs;
    private readonly IPayrollOpeningBalanceRepository? _balances;

    /// <param name="balances">
    /// The opening balances. Optional so the services that build one for themselves when none is
    /// injected (their tests) keep working: without it the figures are the Paid runs' alone.
    /// </param>
    public PayrollYearToDate(IPayrollRunRepository runs, IPayrollOpeningBalanceRepository? balances = null)
    {
        _runs = runs;
        _balances = balances;
    }

    public async Task<IReadOnlyDictionary<Guid, YearToDate>> ForAsync(IReadOnlyCollection<Guid> employeeIds, int year,
        Guid? excludeRunId, CancellationToken ct = default)
    {
        var runs = (await _runs.GetPaidRunsInYearAsync(year, ct) ?? [])
            .Where(r => r.Id != excludeRunId);
        return await SumAsync(employeeIds, year, runs, ct);
    }

    public async Task<YearToDate> ForEmployeeAsync(Guid employeeId, int year, Guid? excludeRunId,
        CancellationToken ct = default)
    {
        // The status and year are checked again here, as final pay always has: the query promises
        // them, and a figure on a certificate mustn't depend on that alone.
        var runs = (await _runs.GetPaidRunsForEmployeeInYearAsync(employeeId, year, ct) ?? [])
            .Where(r => r.Id != excludeRunId && r.Status == PayrollRunStatus.Paid && r.PayDate.Year == year);
        return (await SumAsync([employeeId], year, runs, ct))[employeeId];
    }

    public async Task<IReadOnlyDictionary<Guid, YearToDate>> PaidBeforeAsync(IReadOnlyCollection<Guid> employeeIds,
        DateOnly before, CancellationToken ct = default)
    {
        var runs = (await _runs.GetPaidRunsInYearAsync(before.Year, ct) ?? [])
            .Where(r => r.PayDate < before);
        return await SumAsync(employeeIds, before.Year, runs, ct);
    }

    private async Task<IReadOnlyDictionary<Guid, YearToDate>> SumAsync(IReadOnlyCollection<Guid> employeeIds, int year,
        IEnumerable<PayrollRun> runs, CancellationToken ct)
    {
        var ids = employeeIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, YearToDate>();

        var wanted = ids.ToHashSet();
        var entries = runs
            .SelectMany(r => r.Employees)
            .Where(e => wanted.Contains(e.EmployeeId))
            .ToLookup(e => e.EmployeeId);
        var balances = (_balances is null ? null : await _balances.GetForEmployeesAsync(ids, year, ct)) ?? [];

        return ids.ToDictionary(id => id, id =>
        {
            var mine = entries[id].ToList();
            var balance = balances.FirstOrDefault(b => b.EmployeeId == id && b.Year == year);
            return new YearToDate(
                BasicEarned: mine.Sum(e => e.RegularPay) + (balance?.BasicSalary ?? 0m),
                ThirteenthMonthPaid: mine.Sum(e => e.ThirteenthMonth) + (balance?.ThirteenthMonthPaid ?? 0m),
                ExemptUsed: mine.Sum(e => e.ThirteenthMonthAndOtherBenefits)
                            + (balance is null ? 0m : balance.ThirteenthMonthPaid + balance.OtherBenefitsPaid),
                DeMinimisLeaveDaysUsed: LeavePayout.DeMinimisDaysUsed(mine) + (balance?.DeMinimisLeaveDays ?? 0m));
        });
    }
}
