using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Payroll.FinalPay;
using PeopleCore.Domain.Entities.Leave;

namespace PeopleCore.Application.Payroll.Services;

/// <summary>A leave balance converted to cash, and the days it paid out.</summary>
public sealed record LeavePaidOut(LeaveBalance Balance, decimal Days);

/// <summary>
/// Prices and records leave paid out in cash - shared by final pay and year-end leave
/// conversion, so both price and record the same way.
/// </summary>
public static class LeavePayout
{
    /// <summary>The (DeMinimis, OtherBenefits) of a day list at a daily rate.</summary>
    public static (decimal DeMinimis, decimal OtherBenefits) Price(IEnumerable<LeavePaidOut> days, decimal dailyRate)
        => FinalPayMath.LeaveConversion(
            days.Select(p => (p.Days, p.Balance.LeaveType.CountsAsVacationForDeMinimis)), dailyRate);

    /// <summary>
    /// Adds each day list to its balance's UsedDays and saves it - LeaveBalance has no field of
    /// its own for days converted to cash, so RemainingDays falls to what's left, and the
    /// year-end carry-over, which carries RemainingDays, doesn't carry the paid-out days forward.
    /// </summary>
    public static async Task RecordAsync(
        IEnumerable<LeavePaidOut> paidOut, ILeaveBalanceRepository balances, DateTime now, CancellationToken ct = default)
    {
        foreach (var (balance, days) in paidOut)
        {
            balance.UsedDays += days;
            balance.UpdatedAt = now;
            await balances.UpdateAsync(balance, ct);
        }
    }
}
