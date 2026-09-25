using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities.Leave;

namespace PeopleCore.Application.Leave.Interfaces;

public interface ILeaveBalanceRepository : IRepository<LeaveBalance>
{
    Task<LeaveBalance?> GetByEmployeeAndTypeAsync(Guid employeeId, Guid leaveTypeId, int year, CancellationToken ct = default);
    Task<IReadOnlyList<LeaveBalance>> GetByEmployeeAsync(Guid employeeId, int? year = null, CancellationToken ct = default);
    Task<IReadOnlyList<LeaveBalance>> GetByYearAsync(int year, CancellationToken ct = default);

    /// <summary>
    /// Inserts a year's first balance row (a YearlyAllowance year, on first filing). If a concurrent
    /// filing inserted the row for the same employee, type and year first, the unique index refuses
    /// this one: it is detached and a <see cref="PeopleCore.Domain.Exceptions.DomainException"/>
    /// asks the employee to try again.
    /// </summary>
    Task AddNewAsync(LeaveBalance balance, CancellationToken ct = default);
}
