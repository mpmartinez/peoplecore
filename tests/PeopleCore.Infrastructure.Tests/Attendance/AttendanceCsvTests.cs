using System.Globalization;
using System.Text;
using FluentAssertions;
using PeopleCore.Infrastructure.Attendance;

namespace PeopleCore.Infrastructure.Tests.Attendance;

/// <summary>
/// The CSV import's parsing, with no database. A punch is the wall-clock time at the site labelled
/// UTC - the same convention <c>POST api/attendance/sync</c> uses - because
/// <c>timestamp with time zone</c> refuses any other kind, and lateness is read off the wall clock.
/// </summary>
public class AttendanceCsvTests
{
    private const string Header = "employee_number,date,time_in,time_out";

    private static AttendanceFileParseResult Parse(params string[] rows)
        => AttendanceFile.Parse(new MemoryStream(Encoding.UTF8.GetBytes(string.Join("\n", [Header, .. rows]))), "attendance.csv");

    [Fact]
    public void Parse_YieldsUtcPunchesWithTheSameWallClockTime()
    {
        var result = Parse("CHK-0001,2026-03-10,07:55,17:05");

        result.Errors.Should().BeEmpty();
        result.Punches.Should().HaveCount(2);
        result.Punches.Should().OnlyContain(p => p.EmployeeNumber == "CHK-0001");
        result.Punches.Should().OnlyContain(p => p.PunchTime.Kind == DateTimeKind.Utc);
        result.Punches[0].PunchTime.Should().Be(new DateTime(2026, 3, 10, 7, 55, 0, DateTimeKind.Utc));
        result.Punches[1].PunchTime.Should().Be(new DateTime(2026, 3, 10, 17, 5, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Parse_AcceptsSeconds()
    {
        var result = Parse("CHK-0001,2026-03-10,07:55:30,");

        result.Errors.Should().BeEmpty();
        result.Punches.Should().ContainSingle()
            .Which.PunchTime.Should().Be(new DateTime(2026, 3, 10, 7, 55, 30, DateTimeKind.Utc));
    }

    [Fact]
    public void Parse_SkipsBlankTimeFields()
    {
        var result = Parse(
            "CHK-0001,2026-03-10,08:00,",
            "CHK-0002,2026-03-10,,17:00",
            "CHK-0003,2026-03-10,,");

        result.Errors.Should().BeEmpty();
        result.Punches.Should().HaveCount(2);
        result.Punches[0].Should().Match<PeopleCore.Application.Attendance.DTOs.AttendancePunchDto>(
            p => p.EmployeeNumber == "CHK-0001" && p.PunchTime == new DateTime(2026, 3, 10, 8, 0, 0, DateTimeKind.Utc));
        result.Punches[1].Should().Match<PeopleCore.Application.Attendance.DTOs.AttendancePunchDto>(
            p => p.EmployeeNumber == "CHK-0002" && p.PunchTime == new DateTime(2026, 3, 10, 17, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Parse_SkipsRowsWithoutAnEmployeeNumberOrDate()
    {
        var result = Parse(
            ",2026-03-10,08:00,17:00",
            "CHK-0001,,08:00,17:00",
            "CHK-0002,2026-03-11,08:00,17:00");

        result.Errors.Should().BeEmpty();
        result.Punches.Should().HaveCount(2).And.OnlyContain(p => p.EmployeeNumber == "CHK-0002");
    }

    [Fact]
    public void Parse_DoesNotDependOnTheCurrentCulture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            // en-US reads 03/10 month-first, en-GB day-first; neither may change a yyyy-MM-dd date.
            CultureInfo.CurrentCulture = new CultureInfo("en-GB");
            var result = Parse("CHK-0001,2026-03-10,17:05,");

            result.Punches.Should().ContainSingle()
                .Which.PunchTime.Should().Be(new DateTime(2026, 3, 10, 17, 5, 0, DateTimeKind.Utc));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Parse_ReportsAnUnparseableDateAsARowError_AndKeepsTheOtherRows()
    {
        var result = Parse(
            "CHK-0001,2026/03/10,08:00,17:00",
            "CHK-0002,2026-03-10,08:00,17:00");

        result.Errors.Should().ContainSingle()
            .Which.Should().Be("Row 2: '2026/03/10' is not a date in yyyy-MM-dd form.");
        result.Punches.Should().HaveCount(2).And.OnlyContain(p => p.EmployeeNumber == "CHK-0002");
    }

    [Fact]
    public void Parse_ReportsAnUnparseableTimeAsARowError_AndTakesNoPunchFromThatRow()
    {
        // Keeping the good time_out while dropping the bad time_in would turn it into a time-in.
        var result = Parse(
            "CHK-0001,2026-03-10,8am,17:00",
            "CHK-0002,2026-03-10,08:00,25:00");

        result.Errors.Should().Equal(
            "Row 2: time_in '8am' is not a time in HH:mm or HH:mm:ss form.",
            "Row 3: time_out '25:00' is not a time in HH:mm or HH:mm:ss form.");
        result.Punches.Should().BeEmpty();
    }

    [Fact]
    public void Parse_NumbersRowErrorsByTheirLineInTheFile_CountingBlankLines()
    {
        var result = Parse(
            "CHK-0001,2026-03-10,08:00,17:00",
            "",
            "CHK-0002,10-03-2026,08:00,17:00");

        result.Errors.Should().ContainSingle()
            .Which.Should().StartWith("Row 4:");
    }

    [Fact]
    public void Parse_RefusesAFileWhoseColumnsAreNotRecognised_InsteadOfThrowing()
    {
        var result = AttendanceFile.Parse(new MemoryStream(Encoding.UTF8.GetBytes(
            "name,shift\nJuan,day\n")), "attendance.csv");

        result.Layout.Should().BeNull();
        result.Punches.Should().BeEmpty();
        result.Errors.Should().ContainSingle().Which.Should().Contain("employee_number,date,time_in,time_out");
    }

    [Fact]
    public void Parse_RecognisesPeopleCoresOwnLayout()
    {
        Parse("CHK-0001,2026-03-10,08:00,17:00").Layout.Should().Be(AttendanceFileLayout.DailyInOut);
    }
}
