using FluentAssertions;
using PeopleCore.DemoSeed.Plan;

namespace PeopleCore.DemoSeed.Tests;

/// <summary>
/// Leave the API will accept: work days only, never overlapping, never more than has accrued. A few
/// requests are left pending for the client to decide.
/// </summary>
public class LeavePlannerTests
{
    private static readonly DateOnly Today = new(2026, 9, 18);

    private static (IReadOnlyList<Person> People, IReadOnlyList<LeaveFiling> Leave) Planned(int seed = 20260918)
    {
        var rng = new Random(seed);
        var people = PeopleBuilder.Build(rng);
        return (people, LeavePlanner.Plan(people, Today, rng));
    }

    [Fact]
    public void Filings_CoverOnlyWorkDays_AndDoNotOverlapForOnePerson()
    {
        var (_, leave) = Planned();

        foreach (var filing in leave)
        {
            var calendarDays = filing.End.DayNumber - filing.Start.DayNumber + 1;
            filing.Days.Should().Be(calendarDays, "a filing never spans a weekend or holiday");
        }

        foreach (var person in leave.GroupBy(f => f.PersonNumber))
        {
            var days = person.SelectMany(f => Calendar.WorkDays(f.Start, f.End)).ToList();
            days.Should().OnlyHaveUniqueItems();
        }
    }

    [Fact]
    public void NobodyTakesLeaveBeforeTheyStarted()
    {
        var (people, leave) = Planned();

        leave.Should().OnlyContain(f => f.Start >= people[f.PersonNumber - 1].ActiveFrom);
    }

    [Fact]
    public void InDateOrder_NoFilingEverExceedsWhatHasAccrued()
    {
        var (people, leave) = Planned();

        foreach (var group in leave.Where(f => f.Decision != Decision.Rejected).GroupBy(f => (f.PersonNumber, f.TypeCode)))
        {
            var person = people[group.Key.PersonNumber - 1];
            decimal used = 0;
            foreach (var filing in group.OrderBy(f => f.FiledInMonth).ThenBy(f => f.Start))
            {
                used += filing.Days;
                used.Should().BeLessThanOrEqualTo(LeavePlanner.Accrued(person, filing.FiledInMonth));
            }
        }
    }

    [Fact]
    public void DecidedFilings_AreInThePast_AndFiledInTheirOwnMonth()
    {
        var (_, leave) = Planned();

        foreach (var filing in leave.Where(f => f.Decision != Decision.Pending))
        {
            filing.End.Should().BeBefore(Today);
            filing.FiledInMonth.Should().Be(filing.Start.Month);
        }
    }

    [Fact]
    public void PendingFilings_AreFiledThisMonth_ForLaterDates_ByTheChosenPeople()
    {
        var (_, leave) = Planned();
        var pending = leave.Where(f => f.Decision == Decision.Pending).ToList();

        pending.Select(f => f.PersonNumber).Should().BeEquivalentTo(LeavePlanner.PendingFor);
        pending.Should().OnlyContain(f => f.FiledInMonth == Today.Month && f.Start > Today);
    }

    [Fact]
    public void TwoFilings_AreRejected()
    {
        Planned().Leave.Count(f => f.Decision == Decision.Rejected).Should().Be(2);
    }

    [Fact]
    public void EachPerson_FilesAFewTimes()
    {
        var (people, leave) = Planned();

        foreach (var person in people.Where(p => p.HireDate.Year < 2026))
            leave.Count(f => f.PersonNumber == person.Number && f.Decision != Decision.Pending).Should().BeInRange(2, 6);
    }

    [Fact]
    public void Accrual_StartsTheMonthAfterA2026Hire()
    {
        var (people, _) = Planned();
        var driver = people.Single(p => p.EmployeeNumber == "DEMO-0013"); // hired 2 February 2026

        LeavePlanner.Accrued(driver, 2).Should().Be(0);
        LeavePlanner.Accrued(driver, 3).Should().Be(1.25m);
        LeavePlanner.Accrued(people[0], 1).Should().Be(1.25m);
        LeavePlanner.Accrued(people[0], 9).Should().Be(11.25m);
    }
}
