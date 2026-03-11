using PeopleCore.Application.Leave.DTOs;

namespace PeopleCore.Application.Leave.Interfaces;

public interface ILeaveBalanceService
{
    Task<IReadOnlyList<LeaveBalanceDto>> GetByEmployeeAsync(Guid employeeId, int? year = null, CancellationToken ct = default);
    Task AccrueMonthlyAsync(int year, int month, CancellationToken ct = default);
    Task CarryOverAsync(int fromYear, int toYear, CancellationToken ct = default);
}
