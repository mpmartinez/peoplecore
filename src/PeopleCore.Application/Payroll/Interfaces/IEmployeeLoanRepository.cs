using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities.Payroll;

namespace PeopleCore.Application.Payroll.Interfaces;

/// <summary>
/// Not named in the task brief's file list, but required by its own instruction: loans are not
/// mapped as an EF navigation on EmployeeCompensation (see EmployeeCompensation's remarks), so
/// the run service must load them from their own repository - both to populate the collection
/// before computing, and to retire balances by EmployeeLoanId when a run is marked paid.
/// </summary>
public interface IEmployeeLoanRepository : IRepository<EmployeeLoan>
{
    /// <summary>Active loans for a set of employees, to populate compensation before computing.</summary>
    Task<IReadOnlyList<EmployeeLoan>> GetByEmployeeIdsAsync(IEnumerable<Guid> employeeIds, CancellationToken ct = default);

    /// <summary>The specific loans a run's deduction lines refer to, for retiring their balances.</summary>
    Task<IReadOnlyList<EmployeeLoan>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);

    Task UpdateRangeAsync(IEnumerable<EmployeeLoan> loans, CancellationToken ct = default);
}
