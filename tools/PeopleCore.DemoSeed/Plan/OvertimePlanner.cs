namespace PeopleCore.DemoSeed.Plan;

public record OvertimeFiling(int PersonNumber, DateOnly Date, TimeOnly Start, TimeOnly End, string Reason, Decision Decision);

/// <summary>
/// Evening overtime from 17:00. Approved requests come from Operations and IT staff, since only
/// someone with a manager can have overtime approved. Two pending requests come from the HR Officer,
/// whose manager is the client: the API lets only the direct manager decide overtime.
/// </summary>
public static class OvertimePlanner
{
    public const int ApprovedCount = 24;
    public const int PendingPerson = 3;

    // Deviation from the brief: the sampling loop below is rejection-based, so it needs a bound to
    // provably terminate. Twenty-four approved filings come from roughly ten eligible people across
    // about 180 work days each (well over a thousand candidate person/day pairs), so a handful of
    // rejections per success is the expected case; this cap is two orders of magnitude above that.
    private const int MaxAttempts = 50_000;

    private static readonly string[] Reasons =
    [
        "Month-end inventory count", "Urgent delivery schedule", "System maintenance window",
        "Client deliverable deadline", "Payroll cut-off support", "Warehouse restocking",
    ];

    public static IReadOnlyList<OvertimeFiling> Plan(
        IReadOnlyList<Person> people, DateOnly today, IReadOnlyList<LeaveFiling> leave, Random rng)
    {
        var onLeave = leave.Where(l => l.Decision == Decision.Approved)
            .SelectMany(l => Calendar.WorkDays(l.Start, l.End).Select(d => (l.PersonNumber, d)))
            .ToHashSet();
        var eligible = people
            .Where(p => p.Department is "Operations" or "IT" && p.ManagerNumber is not null)
            .ToList();
        var filings = new List<OvertimeFiling>();
        var used = new HashSet<(int, DateOnly)>();

        var attempt = 0;
        while (filings.Count < ApprovedCount)
        {
            if (attempt++ >= MaxAttempts)
                throw new InvalidOperationException(
                    $"Could not find {ApprovedCount} approvable overtime filings within {MaxAttempts} attempts.");

            var person = eligible[rng.Next(eligible.Count)];
            var date = Picking.RandomWorkDay(rng, person.ActiveFrom, today.AddDays(-1));
            if (date is null || onLeave.Contains((person.Number, date.Value)) || !used.Add((person.Number, date.Value))) continue;

            filings.Add(Evening(person.Number, date.Value, rng, Decision.Approved));
        }

        // The HR Officer's two most recent work days before today, from whichever month they fall in,
        // worked late and awaiting the client. Reaching back past the 1st keeps a run early in a
        // month from leaving the client with nothing to decide.
        var hrOfficer = people[PendingPerson - 1];
        var recent = Calendar.WorkDays(hrOfficer.ActiveFrom, today.AddDays(-1))
            .Where(d => !onLeave.Contains((PendingPerson, d)) && !used.Contains((PendingPerson, d)))
            .TakeLast(2);
        foreach (var date in recent)
            filings.Add(Evening(PendingPerson, date, rng, Decision.Pending));

        return filings.OrderBy(f => f.Date).ThenBy(f => f.PersonNumber).ToList();
    }

    private static OvertimeFiling Evening(int person, DateOnly date, Random rng, Decision decision)
    {
        var start = new TimeOnly(17, 0);
        return new OvertimeFiling(person, date, start, start.AddHours(rng.Next(2, 5)), Reasons[rng.Next(Reasons.Length)], decision);
    }
}
