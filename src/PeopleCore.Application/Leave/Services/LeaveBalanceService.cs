using PeopleCore.Application.Leave.DTOs;
using PeopleCore.Application.Leave.Interfaces;

namespace PeopleCore.Application.Leave.Services;

public class LeaveBalanceService : ILeaveBalanceService
{
    private readonly ILeaveBalanceRepository _repo;
    public LeaveBalanceService(ILeaveBalanceRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<LeaveBalanceDto>> GetByEmployeeAsync(
        Guid employeeId, int? year = null, CancellationToken ct = default)
    {
        var balances = await _repo.GetByEmployeeAsync(employeeId, year, ct);
        return balances.Select(ToDto).ToList();
    }

    public Task AccrueMonthlyAsync(int year, int month, CancellationToken ct = default)
        => Task.CompletedTask; // Handled by LeaveAccrualHostedService

    public Task CarryOverAsync(int fromYear, int toYear, CancellationToken ct = default)
        => Task.CompletedTask; // Handled by LeaveAccrualHostedService

    private static LeaveBalanceDto ToDto(Domain.Entities.Leave.LeaveBalance b) => new(
        b.Id, b.EmployeeId, b.Employee?.FullName ?? string.Empty,
        b.LeaveTypeId, b.LeaveType?.Name ?? string.Empty,
        b.Year, b.TotalDays, b.UsedDays, b.CarriedOverDays, b.RemainingDays);
}
