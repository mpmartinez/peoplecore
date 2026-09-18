using FluentAssertions;
using PeopleCore.DemoSeed.Plan;

namespace PeopleCore.DemoSeed.Tests;

/// <summary>Clock-ins that look like a real office: mostly on time, some late, now and then absent.</summary>
public class AttendancePlannerTests
{
    private static readonly DemoPlan Plan = DemoPlan.Build(20260918, new DateOnly(2026, 9, 18));

    [Fact]
    public void Rows_FallOnWorkDays_OnlyWhileEmployed_AndBeforeToday()
    {
        foreach (var month in Plan.Months)
        foreach (var row in Plan.AttendanceFor(month))
        {
            Calendar.IsWorkDay(row.Date).Should().BeTrue();
            row.Date.Month.Should().Be(month);
            row.Date.Should().BeBefore(Plan.Today);
            row.Date.Should().BeOnOrAfter(Plan.People.Single(p => p.EmployeeNumber == row.EmployeeNumber).ActiveFrom);
        }
    }

    [Fact]
    public void NobodyClocksInOnApprovedLeave()
    {
        var onLeave = Plan.Leave.Where(l => l.Decision == Decision.Approved)
            .SelectMany(l => Calendar.WorkDays(l.Start, l.End).Select(d => ($"DEMO-{l.PersonNumber:0000}", d)))
            .ToHashSet();

        Plan.Months.SelectMany(Plan.AttendanceFor)
            .Should().OnlyContain(r => !onLeave.Contains(new ValueTuple<string, DateOnly>(r.EmployeeNumber, r.Date)));
    }

    [Fact]
    public void OvertimeDays_ClockOutAtOrAfterTheOvertimeEnd()
    {
        var rows = Plan.Months.SelectMany(Plan.AttendanceFor).ToDictionary(r => (r.EmployeeNumber, r.Date));

        foreach (var o in Plan.Overtime)
            rows[($"DEMO-{o.PersonNumber:0000}", o.Date)].TimeOut.Should().BeOnOrAfter(o.End);
    }

    [Fact]
    public void AMonth_HasSomeLates_AndSomeAbsences()
    {
        var march = Plan.AttendanceFor(3);
        var expected = Plan.People.Sum(p => Calendar.WorkDays(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31))
            .Count(d => d >= p.ActiveFrom));

        march.Count(r => r.TimeIn > new TimeOnly(8, 0)).Should().BeGreaterThan(0);
        march.Count.Should().BeLessThan(expected, "someone is absent or on leave");
    }

    [Fact]
    public void TheCsv_HasTheImportHeader_AndOneLinePerRow()
    {
        var rows = Plan.AttendanceFor(1).Take(2).ToList();

        var csv = AttendancePlanner.ToCsv(rows).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        csv[0].Should().Be("employee_number,date,time_in,time_out");
        csv[1].Should().MatchRegex(@"^DEMO-\d{4},2026-01-\d{2},\d{2}:\d{2},\d{2}:\d{2}$");
        csv.Should().HaveCount(3);
    }

    [Fact]
    public void TheSameMonth_IsTheSameEveryTime()
    {
        DemoPlan.Build(20260918, Plan.Today).AttendanceFor(5).Should().Equal(Plan.AttendanceFor(5));
    }
}
