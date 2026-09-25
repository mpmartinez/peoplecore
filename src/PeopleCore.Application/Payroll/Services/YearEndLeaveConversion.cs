using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Leave.Services;
using PeopleCore.Domain.Enums;

namespace PeopleCore.Application.Payroll.Services;

/// <summary>
/// An employee's year-end convertible leave: each active, paid, Accrued <c>ConvertsAtYearEnd</c> type's unused
/// days for the year. Shared by the December payroll's computation, its approval and its Mark
/// Paid, which records the days as used through <see cref="LeavePayout.Apply"/>, as final pay does.
/// </summary>
public interface IYearEndLeaveConversion
{
    /// <summary>Each year-end type's convertible days for the employee in the year.</summary>
    Task<IReadOnlyList<LeavePaidOut>> DaysAsync(Guid employeeId, int year, CancellationToken ct = default);
}

/// <inheritdoc cref="IYearEndLeaveConversion"/>
public sealed class YearEndLeaveConversion : IYearEndLeaveConversion
{
    private readonly ILeaveBalanceRepository _balances;
    private readonly ILeaveRequestRepository _requests;
    private readonly ILeaveTypeRepository _leaveTypes;

    public YearEndLeaveConversion(
        ILeaveBalanceRepository balances,
        ILeaveRequestRepository requests,
        ILeaveTypeRepository leaveTypes)
    {
        _balances = balances;
        _requests = requests;
        _leaveTypes = leaveTypes;
    }

    /// <summary>
    /// Each active year-end type's balance for the year, less the days the employee's Pending
    /// requests of that type hold against that year (charged the same way approval charges a
    /// balance - <see cref="LeaveCharging.DaysChargedByYear"/> - so a request spanning New Year
    /// only holds its own year's part), floored at 0. A type with no balance row, or whose
    /// computed days come to 0, isn't returned. Only paid Accrued types count: leave-type
    /// validation already refuses the setting on any other kind, and this is a second guard.
    /// </summary>
    public async Task<IReadOnlyList<LeavePaidOut>> DaysAsync(Guid employeeId, int year, CancellationToken ct = default)
    {
        var yearEndTypeIds = (await _leaveTypes.GetAllAsync(ct))
            .Where(t => t.IsActive && t.ConvertsAtYearEnd
                        && t.EntitlementKind == LeaveEntitlementKind.Accrued && t.IsPaid)
            .Select(t => t.Id)
            .ToHashSet();
        if (yearEndTypeIds.Count == 0)
            return [];

        var balances = await _balances.GetByEmployeeAsync(employeeId, year, ct);

        var result = new List<LeavePaidOut>();
        foreach (var balance in balances)
        {
            if (!yearEndTypeIds.Contains(balance.LeaveTypeId))
                continue;

            var pending = await _requests.GetPendingAsync(employeeId, balance.LeaveTypeId, excludeId: null, ct);
            var held = pending
                .SelectMany(LeaveCharging.DaysChargedByYear)
                .Where(x => x.Year == year)
                .Sum(x => x.Days);

            var days = Math.Max(0m, balance.RemainingDays - held);
            if (days > 0m)
                result.Add(new LeavePaidOut(balance, days));
        }

        return result;
    }
}
