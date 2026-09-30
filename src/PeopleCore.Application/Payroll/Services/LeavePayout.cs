using PeopleCore.Application.Payroll.FinalPay;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Entities.Payroll;

namespace PeopleCore.Application.Payroll.Services;

/// <summary>A leave balance converted to cash, and the days it paid out.</summary>
public sealed record LeavePaidOut(LeaveBalance Balance, decimal Days);

/// <summary>
/// Prices and records leave paid out in cash - shared by final pay and year-end leave
/// conversion, so both price and record the same way.
/// </summary>
public static class LeavePayout
{
    /// <summary>
    /// The (DeMinimis, OtherBenefits) of a day list at a daily rate. Only
    /// <paramref name="deMinimisDaysLeft"/> vacation-type days can be de minimis: the ten days are
    /// a tax year's (see <see cref="DeMinimisDaysLeft"/>), and a conversion paid earlier in the
    /// same year has used some of them.
    /// </summary>
    public static (decimal DeMinimis, decimal OtherBenefits) Price(IEnumerable<LeavePaidOut> days, decimal dailyRate,
        decimal deMinimisDaysLeft = FinalPayMath.DeMinimisVacationDays)
        => FinalPayMath.LeaveConversion(
            days.Select(p => (p.Days, p.Balance.LeaveType.CountsAsVacationForDeMinimis)), dailyRate, deMinimisDaysLeft);

    /// <summary>
    /// The tax year's de minimis leave days <paramref name="earlierPaidEntries"/> - the employee's
    /// entries on the pay year's earlier Paid runs - used. Each paid LeaveConversionNonTaxable /
    /// DailyRate days as de minimis (rounded to the centi-day, since the amount was rounded to the
    /// centavo). <see cref="PayrollYearToDate"/> adds her opening balance's days to these.
    /// </summary>
    public static decimal DeMinimisDaysUsed(IEnumerable<PayrollRunEmployee> earlierPaidEntries)
        => earlierPaidEntries
            .Where(e => e.LeaveConversionNonTaxable > 0m && e.DailyRate > 0m)
            .Sum(e => Math.Round(e.LeaveConversionNonTaxable / e.DailyRate, 2));

    /// <summary>
    /// What's left of the tax year's ten de minimis leave days after <paramref name="daysUsed"/> -
    /// those the year's earlier Paid runs and her opening balance used
    /// (<see cref="YearToDate.DeMinimisLeaveDaysUsed"/>). Never below zero.
    /// </summary>
    public static decimal DeMinimisDaysLeft(decimal daysUsed)
        => Math.Max(0m, FinalPayMath.DeMinimisVacationDays - daysUsed);

    /// <summary>
    /// What's left of the tax year's ten de minimis leave days after
    /// <paramref name="earlierPaidEntries"/> alone (<see cref="DeMinimisDaysUsed"/>). The pay
    /// sites read <see cref="YearToDate.DeMinimisLeaveDaysLeft"/>, which counts the opening balance too.
    /// </summary>
    public static decimal DeMinimisDaysLeft(IEnumerable<PayrollRunEmployee> earlierPaidEntries)
        => DeMinimisDaysLeft(DeMinimisDaysUsed(earlierPaidEntries));

    /// <summary>
    /// Whether the days still price, at the entry's own daily rate, to what the entry pays - its
    /// de minimis part as well as its total, since the two are taxed differently.
    /// </summary>
    public static bool StillPrices(IEnumerable<LeavePaidOut> days, PayrollRunEmployee entry, decimal deMinimisDaysLeft)
    {
        var (deMinimis, otherBenefits) = Price(days, entry.DailyRate, deMinimisDaysLeft);
        return deMinimis == entry.LeaveConversionNonTaxable && deMinimis + otherBenefits == entry.LeaveConversionPay;
    }

    /// <summary>
    /// Adds each day list to its balance's UsedDays, without saving - the caller saves the
    /// balances with the rest of what paying the run changes, in one go. LeaveBalance has no
    /// field of its own for days converted to cash, so RemainingDays falls to what's left, and the
    /// year-end carry-over, which carries RemainingDays, doesn't carry the paid-out days forward.
    /// Returns each balance changed, once.
    /// </summary>
    public static IReadOnlyList<LeaveBalance> Apply(IEnumerable<LeavePaidOut> paidOut, DateTime now)
    {
        var changed = new List<LeaveBalance>();
        foreach (var (balance, days) in paidOut)
        {
            balance.UsedDays += days;
            balance.UpdatedAt = now;
            if (!changed.Any(b => ReferenceEquals(b, balance)))
                changed.Add(balance);
        }
        return changed;
    }
}
