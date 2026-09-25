using FluentAssertions;
using PeopleCore.Domain.Entities.Attendance;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Enums;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests;

/// <summary>
/// A Manager's unfiltered lists are narrowed to their direct reports in SQL. Run against Postgres so
/// the filter through the Employee navigation is proven to translate, and so a reports-of-reports
/// row proves the scope is direct reports only.
/// </summary>
public class TeamScopingRepositoryTests : DatabaseTestBase
{
    public TeamScopingRepositoryTests(PostgresFixture fixture) : base(fixture) { }

    private Employee _manager = null!;
    private Employee _report = null!;
    private Employee _reportOfReport = null!;
    private Employee _outsider = null!;

    private async Task SeedTeamAsync()
    {
        _manager = AnEmployee("Manager");
        Context.Employees.Add(_manager);
        await Context.SaveChangesAsync();

        _report = AnEmployee("Report");
        _report.ReportingManagerId = _manager.Id;
        _outsider = AnEmployee("Outsider");
        Context.Employees.AddRange(_report, _outsider);
        await Context.SaveChangesAsync();

        _reportOfReport = AnEmployee("ReportOfReport");
        _reportOfReport.ReportingManagerId = _report.Id;
        Context.Employees.Add(_reportOfReport);
        await Context.SaveChangesAsync();
    }

    private IEnumerable<Employee> Everyone => [_manager, _report, _reportOfReport, _outsider];

    [Fact]
    public async Task IsDirectReport_IsTrueOnlyForAnEmployeeWhoseManagerIsTheGivenOne()
    {
        await SeedTeamAsync();
        var sut = new EmployeeRepository(NewContext());

        (await sut.IsDirectReportAsync(_report.Id, _manager.Id)).Should().BeTrue();
        (await sut.IsDirectReportAsync(_reportOfReport.Id, _manager.Id)).Should().BeFalse("only direct reports count");
        (await sut.IsDirectReportAsync(_outsider.Id, _manager.Id)).Should().BeFalse();
        (await sut.IsDirectReportAsync(_manager.Id, _manager.Id)).Should().BeFalse("a manager does not report to themselves");
    }

    [Fact]
    public async Task LeaveRequests_NarrowedToAManager_AreOnlyTheirDirectReports()
    {
        await SeedTeamAsync();
        var leaveType = new LeaveType { Name = "Vacation Leave", Code = "VL", MaxDaysPerYear = 15 };
        Context.LeaveTypes.Add(leaveType);
        Context.LeaveRequests.AddRange(Everyone.Select(e => new LeaveRequest
        {
            EmployeeId = e.Id, LeaveTypeId = leaveType.Id, TotalDays = 1,
            StartDate = new DateOnly(2026, 10, 5), EndDate = new DateOnly(2026, 10, 5)
        }));
        await Context.SaveChangesAsync();
        var sut = new LeaveRequestRepository(NewContext());

        var (team, teamTotal) = await sut.GetPagedAsync(null, _manager.Id, null, 1, 20, excludeConfidential: false);
        var (all, allTotal) = await sut.GetPagedAsync(null, null, null, 1, 20, excludeConfidential: false);

        team.Select(r => r.EmployeeId).Should().Equal(_report.Id);
        teamTotal.Should().Be(1, "the count drives the pager, so it must be narrowed too");
        allTotal.Should().Be(4);
        all.Should().HaveCount(4);
    }

    [Fact]
    public async Task AttendanceRecords_NarrowedToAManager_AreOnlyTheirDirectReports()
    {
        await SeedTeamAsync();
        Context.AttendanceRecords.AddRange(Everyone.Select(e => new AttendanceRecord
        {
            EmployeeId = e.Id, AttendanceDate = new DateOnly(2026, 9, 14), IsPresent = true
        }));
        await Context.SaveChangesAsync();
        var sut = new AttendanceRepository(NewContext());

        var (team, teamTotal) = await sut.GetPagedAsync(null, _manager.Id, null, null, 1, 20);

        team.Select(r => r.EmployeeId).Should().Equal(_report.Id);
        teamTotal.Should().Be(1);
        (await sut.GetPagedAsync(null, null, null, null, 1, 20)).TotalCount.Should().Be(4);
    }

    [Fact]
    public async Task OvertimeRequests_NarrowedToAManager_AreOnlyTheirDirectReports()
    {
        await SeedTeamAsync();
        Context.OvertimeRequests.AddRange(Everyone.Select(e => new OvertimeRequest
        {
            EmployeeId = e.Id, OvertimeDate = new DateOnly(2026, 9, 10), Reason = "Month-end close",
            StartTime = new DateTime(2026, 9, 10, 10, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc), TotalMinutes = 120,
            Status = OvertimeStatus.Pending
        }));
        await Context.SaveChangesAsync();
        var sut = new OvertimeRepository(NewContext());

        var (team, teamTotal) = await sut.GetPagedAsync(null, _manager.Id, "Pending", 1, 20);

        team.Select(r => r.EmployeeId).Should().Equal(_report.Id);
        teamTotal.Should().Be(1);
        (await sut.GetPagedAsync(null, null, "Pending", 1, 20)).TotalCount.Should().Be(4);
    }
}
