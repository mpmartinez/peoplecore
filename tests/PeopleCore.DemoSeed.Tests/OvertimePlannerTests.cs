using FluentAssertions;
using PeopleCore.DemoSeed.Plan;

namespace PeopleCore.DemoSeed.Tests;

/// <summary>Evening overtime in Operations and IT, plus two for the client to decide.</summary>
public class OvertimePlannerTests
{
    private static readonly DateOnly Today = new(2026, 9, 18);

    private static (IReadOnlyList<Person> People, IReadOnlyList<LeaveFiling> Leave, IReadOnlyList<OvertimeFiling> Overtime) Planned()
    {
        var rng = new Random(20260918);
        var people = PeopleBuilder.Build(rng);
        var leave = LeavePlanner.Plan(people, Today, rng);
        return (people, leave, OvertimePlanner.Plan(people, Today, leave, rng));
    }

    [Fact]
    public void TwentyFourAreApproved_FromOperationsAndItStaffWithAManager()
    {
        var (people, _, overtime) = Planned();
        var approved = overtime.Where(o => o.Decision == Decision.Approved).ToList();

        approved.Should().HaveCount(OvertimePlanner.ApprovedCount);
        approved.Should().OnlyContain(o =>
            (people[o.PersonNumber - 1].Department == "Operations" || people[o.PersonNumber - 1].Department == "IT")
            && people[o.PersonNumber - 1].ManagerNumber != null);
    }

    [Fact]
    public void TwoArePending_FromTheHrOfficer_OnPastWorkDaysThisMonth()
    {
        var (_, _, overtime) = Planned();
        var pending = overtime.Where(o => o.Decision == Decision.Pending).ToList();

        pending.Should().HaveCount(2);
        pending.Should().OnlyContain(o => o.PersonNumber == OvertimePlanner.PendingPerson
            && o.Date < Today && o.Date.Month == Today.Month && Calendar.IsWorkDay(o.Date));
    }

    [Fact]
    public void Overtime_IsOnWorkDays_InTheEvening_NeverOnLeave_AndOncePerPersonPerDay()
    {
        var (people, leave, overtime) = Planned();
        var onLeave = leave.Where(l => l.Decision == Decision.Approved)
            .SelectMany(l => Calendar.WorkDays(l.Start, l.End).Select(d => (l.PersonNumber, d))).ToHashSet();

        foreach (var o in overtime)
        {
            Calendar.IsWorkDay(o.Date).Should().BeTrue();
            o.Date.Should().BeOnOrAfter(people[o.PersonNumber - 1].ActiveFrom);
            o.Start.Should().Be(new TimeOnly(17, 0));
            (o.End.ToTimeSpan() - o.Start.ToTimeSpan()).TotalHours.Should().BeInRange(2, 4);
            onLeave.Should().NotContain((o.PersonNumber, o.Date));
        }

        overtime.Select(o => (o.PersonNumber, o.Date)).Should().OnlyHaveUniqueItems();
    }
}
