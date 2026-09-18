namespace PeopleCore.DemoSeed.Plan;

public record KpiPlan(string Description, string Target, decimal Weight, string Actual, decimal SelfScore, decimal ManagerScore);

public record ReviewPlan(
    int PersonNumber, int ReviewerNumber, IReadOnlyList<KpiPlan> Kpis,
    decimal SelfScore, string SelfComment, decimal? ManagerScore, string? ManagerComment)
{
    public bool AwaitsManager => ManagerScore is null;
}

/// <summary>
/// The first-half review, scored 1 to 5. Each person is reviewed by their manager. The General
/// Manager has none, so the HR Manager reviews them. Reviews the HR Manager owns are left awaiting
/// the manager's part, because the HR Manager is the client, who can then finish them in the demo.
/// </summary>
public static class PerformancePlanner
{
    public const string CycleName = "2026 Mid-Year Review";
    public static readonly DateOnly CycleStart = new(2026, 1, 1);
    public static readonly DateOnly CycleEnd = new(2026, 6, 30);
    private static readonly DateOnly OnStaffBy = new(2026, 3, 31);

    private static readonly Dictionary<string, (string Description, string Target, string Actual)[]> Kpis = new()
    {
        ["Executive"] = [("Revenue growth", "12% year on year", "10.5%"), ("Operating margin", "18%", "17.2%"), ("Key hires filled", "4", "3")],
        ["Human Resources"] = [("Time to fill", "30 days", "34 days"), ("Payroll accuracy", "100%", "99.8%"), ("Training hours per employee", "16", "14")],
        ["Finance"] = [("Month-end close", "5 working days", "5 days"), ("Receivables over 60 days", "Under 10%", "8%"), ("Audit findings", "0 major", "0 major")],
        ["Operations"] = [("On-time deliveries", "95%", "93%"), ("Inventory accuracy", "99%", "98.6%"), ("Safety incidents", "0", "0")],
        ["Sales"] = [("Sales against quota", "100%", "96%"), ("New accounts", "12", "11"), ("Collection rate", "95%", "94%")],
        ["IT"] = [("System uptime", "99.5%", "99.7%"), ("Tickets resolved within SLA", "90%", "92%"), ("Projects delivered", "3", "3")],
    };

    private static readonly decimal[] Weights = [40, 30, 30];

    private static readonly string[] SelfComments =
    [
        "Met most targets; want to improve on turnaround time.", "A strong half. I took on extra work during peak season.",
        "Learned a lot this half and ready for more responsibility.", "Some targets slipped in March; recovered by June.",
    ];

    private static readonly string[] ManagerComments =
    [
        "Reliable and consistent. Keep it up.", "Good progress; focus on the targets that slipped.",
        "Exceeded expectations on key deliverables.", "Solid half. Ready for a stretch assignment.",
    ];

    public static IReadOnlyList<ReviewPlan> Plan(IReadOnlyList<Person> people, Random rng)
    {
        var reviews = new List<ReviewPlan>();
        foreach (var person in people.Where(p => p.HireDate <= OnStaffBy))
        {
            var reviewer = person.ManagerNumber ?? 2;
            var kpis = Kpis[person.Department]
                .Select((k, i) => new KpiPlan(k.Description, k.Target, Weights[i], k.Actual, Score(rng), Score(rng)))
                .ToList();
            var self = Weighted(kpis, k => k.SelfScore);
            var awaits = reviewer == 2;

            reviews.Add(new ReviewPlan(
                person.Number, reviewer, kpis, self, SelfComments[rng.Next(SelfComments.Length)],
                awaits ? null : Weighted(kpis, k => k.ManagerScore),
                awaits ? null : ManagerComments[rng.Next(ManagerComments.Length)]));
        }
        return reviews;
    }

    private static decimal Score(Random rng) => Math.Round(3.0m + (decimal)rng.NextDouble() * 1.8m, 1);

    private static decimal Weighted(IReadOnlyList<KpiPlan> kpis, Func<KpiPlan, decimal> score) =>
        Math.Round(kpis.Sum(k => score(k) * k.Weight) / 100m, 1);
}
