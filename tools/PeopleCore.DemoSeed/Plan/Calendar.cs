namespace PeopleCore.DemoSeed.Plan;

public record Holiday(DateOnly Date, string Name, bool IsRegular);

public record PayPeriod(DateOnly Start, DateOnly End, DateOnly PayDate);

/// <summary>
/// The demo year. Holidays are those of Proclamation No. 1006 (2026) that fall before October.
/// Eidul Fitr and Eidul Adha are proclaimed separately each year and are left out rather than guessed.
/// </summary>
public static class Calendar
{
    public static readonly DateOnly Start = new(2026, 1, 1);

    public static readonly IReadOnlyList<Holiday> Holidays =
    [
        new(new DateOnly(2026, 1, 1), "New Year's Day", true),
        new(new DateOnly(2026, 2, 17), "Chinese New Year", false),
        new(new DateOnly(2026, 4, 2), "Maundy Thursday", true),
        new(new DateOnly(2026, 4, 3), "Good Friday", true),
        new(new DateOnly(2026, 4, 4), "Black Saturday", false),
        new(new DateOnly(2026, 4, 9), "Araw ng Kagitingan", true),
        new(new DateOnly(2026, 5, 1), "Labor Day", true),
        new(new DateOnly(2026, 6, 12), "Independence Day", true),
        new(new DateOnly(2026, 8, 21), "Ninoy Aquino Day", false),
        new(new DateOnly(2026, 8, 31), "National Heroes Day", true),
    ];

    private static readonly HashSet<DateOnly> HolidayDates = Holidays.Select(h => h.Date).ToHashSet();

    public static bool IsWorkDay(DateOnly date) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !HolidayDates.Contains(date);

    public static IEnumerable<DateOnly> WorkDays(DateOnly from, DateOnly to)
    {
        for (var day = from; day <= to; day = day.AddDays(1))
            if (IsWorkDay(day)) yield return day;
    }

    /// <summary>Semi-monthly periods from 1 January whose last day is before <paramref name="today"/>.</summary>
    public static IReadOnlyList<PayPeriod> CompletedPayPeriods(DateOnly today)
    {
        var periods = new List<PayPeriod>();
        for (var month = Start; month < today; month = month.AddMonths(1))
        {
            var firstHalfEnd = new DateOnly(month.Year, month.Month, 15);
            var secondHalfEnd = new DateOnly(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month));

            if (firstHalfEnd < today) periods.Add(new PayPeriod(month, firstHalfEnd, firstHalfEnd));
            if (secondHalfEnd < today) periods.Add(new PayPeriod(firstHalfEnd.AddDays(1), secondHalfEnd, secondHalfEnd));
        }
        return periods;
    }
}
