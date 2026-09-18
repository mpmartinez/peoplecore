namespace PeopleCore.DemoSeed.Plan;

public enum Decision { Approved, Rejected, Pending }

public record LeaveFiling(int PersonNumber, string TypeCode, DateOnly Start, DateOnly End, Decision Decision, int FiledInMonth)
{
    public int Days => Calendar.WorkDays(Start, End).Count();
}

/// <summary>
/// Vacation and sick leave that the API will accept. The balance check counts every day planned
/// for a type, whatever its date, against what has accrued by the filing's own month. That is
/// stricter than the real balance, so a filing can never be refused for lack of days.
/// </summary>
public static class LeavePlanner
{
    public const decimal DaysPerMonth = 1.25m;

    /// <summary>
    /// People who leave a request pending for the client. The client holds approvals.all, so any of
    /// these is theirs to decide.
    /// </summary>
    public static readonly IReadOnlyList<int> PendingFor = [3, 5, 10, 15];

    /// <summary>
    /// Days of one type accrued by the end of <paramref name="month"/>. People already on staff accrue
    /// from January. People hired in 2026 are assumed to start the month after they join.
    /// </summary>
    public static decimal Accrued(Person person, int month)
    {
        var firstMonth = person.HireDate.Year < 2026 ? 1 : person.HireDate.Month + 1;
        return month < firstMonth ? 0 : (month - firstMonth + 1) * DaysPerMonth;
    }

    public static IReadOnlyList<LeaveFiling> Plan(IReadOnlyList<Person> people, DateOnly today, Random rng)
    {
        var filings = new List<LeaveFiling>();

        foreach (var person in people)
        {
            var taken = new HashSet<DateOnly>();
            var used = new Dictionary<string, decimal> { ["VL"] = 0, ["SL"] = 0 };
            var wanted = person.HireDate.Year == 2026 ? rng.Next(1, 3) : rng.Next(2, 7);
            var mine = 0;
            // Someone who will leave a request pending keeps two vacation days back for it.
            var reserve = PendingFor.Contains(person.Number) ? 2 : 0;

            for (var attempt = 0; mine < wanted && attempt < 300; attempt++)
            {
                var type = rng.Next(3) == 0 ? "SL" : "VL";
                var start = Picking.RandomWorkDay(rng, person.ActiveFrom, today.AddDays(-1));
                if (start is null) break;

                var days = Picking.ConsecutiveWorkDays(start.Value, type == "SL" ? rng.Next(1, 3) : rng.Next(1, 4));
                if (days.Any(taken.Contains) || days[^1] >= today) continue;
                if (used[type] + days.Count + (type == "VL" ? reserve : 0) > Accrued(person, start.Value.Month)) continue;

                used[type] += days.Count;
                taken.UnionWith(days);
                filings.Add(new LeaveFiling(person.Number, type, days[0], days[^1], Decision.Approved, start.Value.Month));
                mine++;
            }

            if (!PendingFor.Contains(person.Number)) continue;

            for (var attempt = 0; attempt < 300; attempt++)
            {
                var start = Picking.RandomWorkDay(rng, today.AddDays(3), today.AddDays(14));
                if (start is null) break;

                var days = Picking.ConsecutiveWorkDays(start.Value, rng.Next(1, 3));
                if (days.Any(taken.Contains) || used["VL"] + days.Count > Accrued(person, today.Month)) continue;

                used["VL"] += days.Count;
                taken.UnionWith(days);
                filings.Add(new LeaveFiling(person.Number, "VL", days[0], days[^1], Decision.Pending, today.Month));
                break;
            }
        }

        // Two decided filings are turned down. They are picked from people who filed at least three
        // times, so nobody's whole year is refusals.
        var candidates = filings
            .Select((f, i) => (f, i))
            .Where(x => x.f.Decision == Decision.Approved && filings.Count(o => o.PersonNumber == x.f.PersonNumber) >= 3)
            .Select(x => x.i)
            .ToList();
        foreach (var index in candidates.OrderBy(_ => rng.Next()).Take(2))
            filings[index] = filings[index] with { Decision = Decision.Rejected };

        return filings.OrderBy(f => f.FiledInMonth).ThenBy(f => f.Start).ThenBy(f => f.PersonNumber).ToList();
    }
}
