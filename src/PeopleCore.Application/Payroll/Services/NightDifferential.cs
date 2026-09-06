namespace PeopleCore.Application.Payroll.Services;

/// <summary>
/// Night shift differential window, Labor Code Article 86: hours worked between 10 p.m. and
/// 6 a.m. attract a premium. The window is measured against the punch interval rather than the
/// scheduled shift, so only hours genuinely worked at night are paid.
/// </summary>
public static class NightDifferential
{
    private const int WindowOpensHour = 22;
    private const int WindowClosesHour = 6;

    public static decimal Hours(DateTime timeIn, DateTime timeOut)
    {
        if (timeOut <= timeIn) return 0m;

        decimal total = 0m;

        // Start a day early: a punch beginning at 02:00 falls inside the window that opened at
        // 22:00 the previous day.
        for (var day = timeIn.Date.AddDays(-1); day <= timeOut.Date; day = day.AddDays(1))
        {
            var windowStart = day.AddHours(WindowOpensHour);
            var windowEnd = day.AddDays(1).AddHours(WindowClosesHour);

            var overlapStart = timeIn > windowStart ? timeIn : windowStart;
            var overlapEnd = timeOut < windowEnd ? timeOut : windowEnd;

            if (overlapEnd > overlapStart)
                total += (decimal)(overlapEnd - overlapStart).TotalHours;
        }

        return Math.Round(total, 2);
    }
}
