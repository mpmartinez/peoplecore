using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities.Payroll;

namespace PeopleCore.Application.Payroll.OpeningBalances;

public interface IPayrollOpeningBalanceRepository : IRepository<PayrollOpeningBalance>
{
    /// <summary>The employee's opening balance for the year, with its <see cref="PayrollOpeningBalance.Employee"/>, or null when she has none.</summary>
    Task<PayrollOpeningBalance?> GetAsync(Guid employeeId, int year, CancellationToken ct = default);

    /// <summary>The employees' opening balances for the year (at most one each), each with its <see cref="PayrollOpeningBalance.Employee"/>.</summary>
    Task<IReadOnlyList<PayrollOpeningBalance>> GetForEmployeesAsync(IReadOnlyCollection<Guid> employeeIds, int year,
        CancellationToken ct = default);

    /// <summary>
    /// The employees' opening balances for the year, tracked so their changes save: what an import
    /// updates in place. Unlike <see cref="GetForEmployeesAsync"/> it loads no
    /// <see cref="PayrollOpeningBalance.Employee"/>.
    /// </summary>
    Task<IReadOnlyList<PayrollOpeningBalance>> GetForEmployeesForUpdateAsync(IReadOnlyCollection<Guid> employeeIds,
        int year, CancellationToken ct = default);

    /// <summary>Every opening balance for the year, each with its <see cref="PayrollOpeningBalance.Employee"/>, by last then first name.</summary>
    Task<IReadOnlyList<PayrollOpeningBalance>> GetForYearAsync(int year, CancellationToken ct = default);

    /// <summary>The years the employee has an opening balance for, latest first.</summary>
    Task<IReadOnlyList<int>> GetYearsForEmployeeAsync(Guid employeeId, CancellationToken ct = default);

    /// <summary>
    /// Adds a new opening balance. Two creates for the same employee and year at once both pass the
    /// service's "already has one" check, so the unique index on (employee, year) is the real guard:
    /// the loser gets the check's own <see cref="Domain.Exceptions.DomainException"/>
    /// ("{name} already has an opening balance for {Year}."), not a constraint-violation 500.
    /// </summary>
    Task<PayrollOpeningBalance> AddNewAsync(PayrollOpeningBalance balance, CancellationToken ct = default);

    /// <summary>
    /// Adds <paramref name="added"/> and saves them together with every change to balances this
    /// repository loaded, in one save: all of it or none. A balance a rival save added meanwhile
    /// trips the unique index; nothing is saved, the new balances are dropped from the context, and
    /// a <see cref="Domain.Exceptions.DomainException"/> says to import again.
    /// </summary>
    Task SaveAllAsync(IReadOnlyCollection<PayrollOpeningBalance> added, CancellationToken ct = default);
}
