using FluentAssertions;
using PeopleCore.DemoSeed.Plan;

namespace PeopleCore.DemoSeed.Tests;

/// <summary>Evening overtime in Operations and IT, plus two for the client to decide.</summary>
public class OvertimePlannerTests
{
    private static readonly DateOnly Today = new(2026, 9, 18);

    private static (IReadOnlyList<Person> People, IReadOnlyList<LeaveFiling> Leave, IReadOnlyList<OvertimeFiling> Overtime) Planned() =>
        Planned(Today);

    private static (IReadOnlyList<Person> People, IReadOnlyList<LeaveFiling> Leave, IReadOnlyList<OvertimeFiling> Overtime) Planned(DateOnly today)
    {
        var rng = new Random(20260918);
        var people = PeopleBuilder.Build(rng);
        var leave = LeavePlanner.Plan(people, today, rng);
        return (people, leave, OvertimePlanner.Plan(people, today, leave, rng));
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

    // 1 and 2 June 2026 are a Monday and a Tuesday; 1 September is a Tuesday. On those days this
    // month has fewer than two past work days, so the pending pair must reach back into last month.
    [Theory]
    [InlineData(2026, 9, 18)]
    [InlineData(2026, 9, 1)]
    [InlineData(2026, 6, 1)]
    [InlineData(2026, 6, 2)]
    public void TwoArePending_FromTheHrOfficer_OnTheirTwoMostRecentPastWorkDays(int year, int month, int day)
    {
        var today = new DateOnly(year, month, day);
        var (_, leave, overtime) = Planned(today);
        var pending = overtime.Where(o => o.Decision == Decision.Pending).ToList();

        pending.Should().HaveCount(2);
        pending.Should().OnlyContain(o => o.PersonNumber == OvertimePlanner.PendingPerson
            && o.Date < today && Calendar.IsWorkDay(o.Date));

        // Nothing free for the HR Officer sits between the pending pair and today.
        var onLeave = leave.Where(l => l.Decision == Decision.Approved && l.PersonNumber == OvertimePlanner.PendingPerson)
            .SelectMany(l => Calendar.WorkDays(l.Start, l.End)).ToHashSet();
        var approved = overtime.Where(o => o.PersonNumber == OvertimePlanner.PendingPerson && o.Decision == Decision.Approved)
            .Select(o => o.Date).ToHashSet();
        var skipped = Calendar.WorkDays(pending.Min(o => o.Date), today.AddDays(-1))
            .Where(d => !onLeave.Contains(d) && !approved.Contains(d));
        skipped.Should().BeEquivalentTo(pending.Select(o => o.Date));
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
