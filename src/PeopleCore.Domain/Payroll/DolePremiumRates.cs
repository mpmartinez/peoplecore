namespace PeopleCore.Domain.Payroll;

/// <summary>
/// The kind of day work was performed on, as the DOLE handbook classifies it. The rest-day
/// variants are separate members rather than a flag because their rates are not a plain
/// multiple of the base day — a regular holiday is 200% and on a rest day 260%, not 260% from
/// applying a 1.30 rest-day factor to something.
/// </summary>
public enum WorkDayType
{
    Ordinary,
    RestDay,
    SpecialNonWorking,
    SpecialNonWorkingOnRestDay,
    DoubleSpecialNonWorking,
    DoubleSpecialNonWorkingOnRestDay,
    RegularHoliday,
    RegularHolidayOnRestDay,
    DoubleRegularHoliday,
    DoubleRegularHolidayOnRestDay
}

/// <summary>
/// Premium pay rates from the DOLE Handbook on Workers' Statutory Monetary Benefits, 2024
/// Edition (Bureau of Working Conditions), section A, "Guide Computations for Holiday Pay,
/// Premium Pay, Overtime Pay, and Night Shift Differential".
/// <para>
/// The handbook's forty published rates are not forty independent facts — they are one rule:
/// a base rate for the kind of day, multiplied by 1.10 for hours in the night window, and
/// multiplied again by the overtime factor for hours past the eighth. Expressing it as the
/// rule rather than a table of constants means a combination the attendance schema cannot yet
/// record still rates correctly the moment it can be.
/// </para>
/// </summary>
public static class DolePremiumRates
{
    /// <summary>Night shift differential: +10% per hour worked 10 p.m. to 6 a.m. (Article 86).</summary>
    public const decimal NightShiftFactor = 1.10m;

    /// <summary>Overtime factor on an ordinary day.</summary>
    public const decimal OrdinaryOvertimeFactor = 1.25m;

    /// <summary>
    /// Overtime factor on any premium day. The handbook states 25% only for ordinary days;
    /// every rest day, special day and holiday takes 30%, so a single flat overtime factor is
    /// always wrong somewhere.
    /// </summary>
    public const decimal PremiumOvertimeFactor = 1.30m;

    /// <summary>Pay rate for the first eight hours on the given kind of day.</summary>
    public static decimal BaseRate(WorkDayType day) => day switch
    {
        WorkDayType.Ordinary                         => 1.00m,
        WorkDayType.RestDay                          => 1.30m,
        WorkDayType.SpecialNonWorking                => 1.30m,
        WorkDayType.SpecialNonWorkingOnRestDay       => 1.50m,
        WorkDayType.DoubleSpecialNonWorking          => 1.50m,
        WorkDayType.DoubleSpecialNonWorkingOnRestDay => 1.95m,
        WorkDayType.RegularHoliday                   => 2.00m,
        WorkDayType.RegularHolidayOnRestDay          => 2.60m,
        WorkDayType.DoubleRegularHoliday             => 3.00m,
        WorkDayType.DoubleRegularHolidayOnRestDay    => 3.90m,
        _ => throw new ArgumentOutOfRangeException(nameof(day), day, "Unknown day type.")
    };

    /// <summary>
    /// The overtime factor for the given day. Ordinary days take 25%, everything else 30%.
    /// </summary>
    public static decimal OvertimeFactor(WorkDayType day) =>
        day == WorkDayType.Ordinary ? OrdinaryOvertimeFactor : PremiumOvertimeFactor;

    /// <summary>
    /// Full pay rate for an hour of work, as a multiple of the basic hourly rate. Unrounded:
    /// several published rates carry three or four decimals (rest-day night-shift overtime is
    /// 1.859), and rounding the rate rather than the money loses centavos.
    /// </summary>
    public static decimal Rate(WorkDayType day, bool nightShift = false, bool overtime = false)
    {
        decimal rate = BaseRate(day);
        if (nightShift) rate *= NightShiftFactor;
        if (overtime) rate *= OvertimeFactor(day);
        return rate;
    }

    /// <summary>
    /// The portion of <see cref="Rate"/> payable on top of hours already paid as regular time.
    /// <para>
    /// A monthly-paid employee's salary already covers the first 100% of a day it treats as
    /// paid, so working a regular holiday adds 100%, not 200%; and a night-shift hour inside
    /// the normal workday adds only its 10% premium. Paying the full rate on top of regular
    /// pay would pay a worked regular holiday at 300%.
    /// </para>
    /// </summary>
    public static decimal Premium(WorkDayType day, bool nightShift = false, bool overtime = false) =>
        Rate(day, nightShift, overtime) - 1.00m;
}
