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
    public static readonly DateOnly FirstDay = new(2026, 2, 1);
    public static readonly DateOnly LastDay = new(2026, 10, 31);

    /// <summary>
    /// A demo needs at least one completed month of history, and the calendar knows 2026's holidays
    /// only to the end of August (none fall in September or October). Pending leave is filed up to
    /// about two weeks ahead, and from mid-December that would reach 2027. So the plan can be built
    /// only from 1 February to 31 October 2026.
    /// </summary>
    public static DemoPlan Build(int seed, DateOnly today)
    {
        if (today < FirstDay || today > LastDay)
            throw new ArgumentOutOfRangeException(nameof(today), today,
                "The demo calendar covers 1 February to 31 October 2026.");

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
