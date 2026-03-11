using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities.Leave;

namespace PeopleCore.Application.Leave.Interfaces;

public interface ILeaveBalanceRepository : IRepository<LeaveBalance>
{
    Task<LeaveBalance?> GetByEmployeeAndTypeAsync(Guid employeeId, Guid leaveTypeId, int year, CancellationToken ct = default);
    Task<IReadOnlyList<LeaveBalance>> GetByEmployeeAsync(Guid employeeId, int? year = null, CancellationToken ct = default);
}
