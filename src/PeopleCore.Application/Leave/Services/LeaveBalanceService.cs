using PeopleCore.Application.Leave.DTOs;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Domain.Enums;

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

    // Accrual is handled by LeaveAccrualHostedService; this method exists for manual triggers via admin API
    public async Task AccrueMonthlyAsync(int year, int month, CancellationToken ct = default)
        => await Task.CompletedTask;

    public async Task CarryOverAsync(int fromYear, int toYear, CancellationToken ct = default)
    {
        var balances = await _repo.GetByYearAsync(fromYear, ct);
        // Only an accrued type carries over; a flag left on another kind from before validation
        // refused it is ignored.
        foreach (var balance in balances.Where(b =>
                     b.LeaveType is { IsCarryOver: true, EntitlementKind: LeaveEntitlementKind.Accrued }))
        {
            var remaining = balance.RemainingDays;
            if (remaining <= 0) continue;

            var carryAmount = balance.LeaveType!.CarryOverMaxDays.HasValue
                ? Math.Min(remaining, balance.LeaveType.CarryOverMaxDays.Value)
                : remaining;

            var nextYear = await _repo.GetByEmployeeAndTypeAsync(balance.EmployeeId, balance.LeaveTypeId, toYear, ct);
            if (nextYear is not null)
            {
                nextYear.CarriedOverDays += carryAmount;
                nextYear.UpdatedAt = DateTime.UtcNow;
                await _repo.UpdateAsync(nextYear, ct);
            }
            else
            {
                var newBalance = new Domain.Entities.Leave.LeaveBalance
                {
                    EmployeeId = balance.EmployeeId,
                    LeaveTypeId = balance.LeaveTypeId,
                    Year = toYear,
                    TotalDays = 0,
                    UsedDays = 0,
                    CarriedOverDays = carryAmount
                };
                await _repo.AddAsync(newBalance, ct);
            }
        }
    }

    private static LeaveBalanceDto ToDto(Domain.Entities.Leave.LeaveBalance b) => new(
        b.Id, b.EmployeeId, b.Employee?.FullName ?? string.Empty,
        b.LeaveTypeId, b.LeaveType?.Name ?? string.Empty,
        b.Year, b.TotalDays, b.UsedDays, b.CarriedOverDays, b.RemainingDays,
        // Fail closed: a balance whose type did not load is treated as confidential.
        b.LeaveType?.IsConfidential ?? true);
}
