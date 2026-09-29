namespace PeopleCore.Application.Payroll.Maternity;

/// <summary>
/// The SSS maternity benefit arithmetic under RA 11210 (105-Day Expanded Maternity Leave Law),
/// with no data access.
/// </summary>
public static class MaternityMath
{
    /// <summary>The semester of contingency: the two calendar quarters ending with the quarter of the contingency.</summary>
    public const int SemesterMonths = 6;

    /// <summary>The contribution window: the 12 calendar months immediately before the semester of contingency.</summary>
    public const int WindowMonths = 12;

    /// <summary>The SSS formula takes the 6 highest monthly salary credits in the window.</summary>
    public const int HighestMonths = 6;

    /// <summary>The SSS formula divides the sum of the highest credits by 180 to get the daily maternity allowance.</summary>
    public const int Divisor = 180;

    /// <summary>
    /// The 12-month window before the semester of contingency, from the first day of its first month
    /// to the last day of its last month. The semester is the two calendar quarters ending with the
    /// quarter of <paramref name="contingency"/>.
    /// </summary>
    public static (DateOnly From, DateOnly To) ContributionWindow(DateOnly contingency)
    {
        int quarterEndMonth = ((contingency.Month - 1) / 3 + 1) * 3;
        var semesterStart = new DateOnly(contingency.Year, quarterEndMonth, 1).AddMonths(-(SemesterMonths - 1));
        var from = semesterStart.AddMonths(-WindowMonths);
        var to = semesterStart.AddDays(-1);
        return (from, to);
    }

    /// <summary>
    /// The SSS daily maternity allowance formula: the sum of the 6 highest monthly salary credits
    /// divided by 180, rounded to 2 dp. Fewer than 6 credits are all used; non-positive credits are
    /// ignored; null when there is no credit.
    /// </summary>
    public static decimal? SuggestedDailyAllowance(IEnumerable<decimal> monthlySalaryCredits)
    {
        var highest = monthlySalaryCredits
            .Where(c => c > 0m)
            .OrderByDescending(c => c)
            .Take(HighestMonths)
            .ToList();
        if (highest.Count == 0) return null;
        return Math.Round(highest.Sum() / Divisor, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>The maternity benefit: the daily allowance for the leave days, rounded to 2 dp.</summary>
    public static decimal Benefit(decimal dailyAllowance, decimal days)
        => Math.Round(dailyAllowance * days, 2, MidpointRounding.AwayFromZero);

    /// <summary>The calendar days of [start, end] that fall inside [periodStart, periodEnd].</summary>
    public static int DaysInPeriod(DateOnly start, DateOnly end, DateOnly periodStart, DateOnly periodEnd)
    {
        var from = start > periodStart ? start : periodStart;
        var to = end < periodEnd ? end : periodEnd;
        return to < from ? 0 : to.DayNumber - from.DayNumber + 1;
    }

    /// <summary>
    /// The part of regular pay SSS covers for the maternity days (the daily allowance times the days),
    /// never more than the regular pay. The employer pays only the salary differential.
    /// </summary>
    public static decimal Offset(decimal regularPay, decimal dailyAllowance, int maternityDays)
    {
        if (maternityDays <= 0) return 0m;
        return Math.Min(regularPay, Math.Round(dailyAllowance * maternityDays, 2, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// For an employer exempt from the salary differential (RA 11210 IRR): the share of regular pay
    /// for the maternity days out of the period's calendar days, rounded to 2 dp, never more than the regular pay.
    /// </summary>
    public static decimal ExemptOffset(decimal regularPay, int maternityDays, int periodDays)
    {
        if (maternityDays <= 0 || periodDays <= 0) return 0m;
        return Math.Min(regularPay, Math.Round(regularPay * maternityDays / periodDays, 2, MidpointRounding.AwayFromZero));
    }
}
