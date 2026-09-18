namespace PeopleCore.DemoSeed.Plan;

public static class Picking
{
    /// <summary>A random work day in [from, to], or null when there is none.</summary>
    public static DateOnly? RandomWorkDay(Random rng, DateOnly from, DateOnly to)
    {
        var days = Calendar.WorkDays(from, to).ToList();
        return days.Count == 0 ? null : days[rng.Next(days.Count)];
    }

    /// <summary>Up to <paramref name="count"/> work days from <paramref name="start"/>, stopping at the first day off.</summary>
    public static IReadOnlyList<DateOnly> ConsecutiveWorkDays(DateOnly start, int count)
    {
        var days = new List<DateOnly>();
        for (var day = start; days.Count < count && Calendar.IsWorkDay(day); day = day.AddDays(1))
            days.Add(day);
        return days;
    }
}
