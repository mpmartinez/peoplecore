using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities.Payroll;

namespace PeopleCore.Application.Payroll.Interfaces;

/// <summary>
/// Not named in the task brief's file list, but required by its own instruction: allowances are
/// not mapped as an EF navigation on EmployeeCompensation (they are keyed by EmployeeId and
/// stand alone - see EmployeeCompensation's remarks), so the run service must load them from
/// their own repository and populate the collection itself before calling PayrollComputationService.
/// </summary>
public interface IEmployeeAllowanceRepository : IRepository<EmployeeAllowance>
{
    Task<IReadOnlyList<EmployeeAllowance>> GetByEmployeeIdsAsync(IEnumerable<Guid> employeeIds, CancellationToken ct = default);
}
