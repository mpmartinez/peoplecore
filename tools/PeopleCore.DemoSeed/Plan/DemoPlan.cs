namespace PeopleCore.DemoSeed.Plan;

/// <summary>
/// Everything the seeder will create, decided before the first request is sent. The same seed and
/// the same day always give the same plan.
/// </summary>
public record DemoPlan(
    int Seed, DateOnly Today, IReadOnlyList<Person> People, IReadOnlyList<PayPeriod> PayPeriods,
    IReadOnlyList<LeaveFiling> Leave, IReadOnlyList<OvertimeFiling> Overtime,
    IReadOnlyList<ReviewPlan> Reviews, IReadOnlyList<ApplicantPlan> Applicants)
{
    /// <summary>
    /// The calendar knows only 2026's holidays, and a demo needs at least one completed month of
    /// history, so the plan can be built only between February and December 2026.
    /// </summary>
    public static DemoPlan Build(int seed, DateOnly today)
    {
        if (today < new DateOnly(2026, 2, 1) || today.Year != 2026)
            throw new ArgumentOutOfRangeException(nameof(today), today,
                "The demo calendar covers 2026 only, and needs at least one full month of history.");

        var rng = new Random(seed);
        var people = PeopleBuilder.Build(rng);
        var leave = LeavePlanner.Plan(people, today, rng);
        var overtime = OvertimePlanner.Plan(people, today, leave, rng);
        var reviews = PerformancePlanner.Plan(people, rng);
        var applicants = RecruitmentPlanner.Applicants(people, today, rng);

        return new DemoPlan(seed, today, people, Calendar.CompletedPayPeriods(today), leave, overtime, reviews, applicants);
    }

    public IReadOnlyList<int> Months => Enumerable.Range(1, Today.Month).ToList();

    public IReadOnlyList<AttendanceRow> AttendanceFor(int month) =>
        AttendancePlanner.ForMonth(People, month, Today, Leave, Overtime, Seed);

    public Person PersonNumber(int number) => People[number - 1];
}
