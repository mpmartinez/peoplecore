using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities.Payroll;

namespace PeopleCore.Application.Payroll.Interfaces;

public interface IEmployeeCompensationRepository : IRepository<EmployeeCompensation>
{
    Task<EmployeeCompensation?> GetByEmployeeIdAsync(Guid employeeId, CancellationToken ct = default);

    /// <summary>Batch load for computing a whole run's worth of employees at once.</summary>
    Task<IReadOnlyList<EmployeeCompensation>> GetByEmployeeIdsAsync(IEnumerable<Guid> employeeIds, CancellationToken ct = default);
}
