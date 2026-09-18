using FluentAssertions;
using PeopleCore.Application.Analytics.Services;
using PeopleCore.Domain.Entities.Attendance;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Entities.Organization;
using PeopleCore.Domain.Enums;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Analytics;

/// <summary>
/// The HR analytics page showed every employee under "Unassigned" even though every employee row
/// had a real department: the repositories the service calls loaded Employee but never
/// Employee.Department, so the service's <c>GroupBy(e => e.Department?.Name ?? "Unassigned")</c>
/// always took the fallback. Run against the real repositories and a real database - the
/// Application-layer analytics tests mock the repositories, so they could never see a missing
/// Include.
/// </summary>
public class HRAnalyticsDepartmentTests : DatabaseTestBase
{
    public HRAnalyticsDepartmentTests(PostgresFixture fixture) : base(fixture) { }

    private HRAnalyticsService Service => new(
        new EmployeeRepository(NewContext()),
        new AttendanceRepository(NewContext()),
        new OvertimeRepository(NewContext()),
        new LeaveBalanceRepository(NewContext()),
        new ApplicantRepository(NewContext()),
        new PerformanceReviewRepository(NewContext()));

    private async Task<(Department Engineering, Department Sales)> SeedTwoDepartmentsAsync()
    {
        var company = ACompany();
        var engineering = new Department { Name = "Engineering", CompanyId = company.Id };
        var sales = new Department { Name = "Sales", CompanyId = company.Id };
        Context.Companies.Add(company);
        Context.Departments.AddRange(engineering, sales);
        await Context.SaveChangesAsync();
        return (engineering, sales);
    }

    [Fact]
    public async Task GetHeadcount_GroupsByTheEmployeesRealDepartment()
    {
        var (engineering, sales) = await SeedTwoDepartmentsAsync();
        var engineer = AnEmployee("Reyes", "Ana");
        engineer.DepartmentId = engineering.Id;
        var salesRep = AnEmployee("Cruz", "Ben");
        salesRep.DepartmentId = sales.Id;
        Context.Employees.AddRange(engineer, salesRep);
        await Context.SaveChangesAsync();

        var response = await Service.GetHeadcountAsync(new DateOnly(2019, 1, 1), new DateOnly(2030, 1, 1));

        response.Data.Should().Contain(d => d.Department == "Engineering" && d.Total == 1);
        response.Data.Should().Contain(d => d.Department == "Sales" && d.Total == 1);
        response.Data.Should().NotContain(d => d.Department == "Unassigned");
    }

    [Fact]
    public async Task GetAttendanceRate_GroupsByTheEmployeesRealDepartment()
    {
        var (engineering, sales) = await SeedTwoDepartmentsAsync();
        var engineer = AnEmployee("Reyes", "Ana");
        engineer.DepartmentId = engineering.Id;
        var salesRep = AnEmployee("Cruz", "Ben");
        salesRep.DepartmentId = sales.Id;
        Context.Employees.AddRange(engineer, salesRep);
        await Context.SaveChangesAsync();

        var date = new DateOnly(2026, 9, 1);
        Context.AttendanceRecords.AddRange(
            new AttendanceRecord { EmployeeId = engineer.Id, AttendanceDate = date, IsPresent = true, LateMinutes = 0 },
            new AttendanceRecord { EmployeeId = salesRep.Id, AttendanceDate = date, IsPresent = true, LateMinutes = 0 });
        await Context.SaveChangesAsync();

        var response = await Service.GetAttendanceRateAsync(date, date);

        response.Data.Select(a => a.Department).Should().BeEquivalentTo(["Engineering", "Sales"]);
        response.Data.Should().NotContain(a => a.Department == "Unassigned");
    }

    [Fact]
    public async Task GetOvertime_GroupsByTheEmployeesRealDepartment()
    {
        var (engineering, sales) = await SeedTwoDepartmentsAsync();
        var engineer = AnEmployee("Reyes", "Ana");
        engineer.DepartmentId = engineering.Id;
        var salesRep = AnEmployee("Cruz", "Ben");
        salesRep.DepartmentId = sales.Id;
        Context.Employees.AddRange(engineer, salesRep);
        await Context.SaveChangesAsync();

        var date = new DateOnly(2026, 9, 10);
        Context.OvertimeRequests.AddRange(
            new OvertimeRequest
            {
                EmployeeId = engineer.Id, OvertimeDate = date, Reason = "Release",
                StartTime = new DateTime(2026, 9, 10, 18, 0, 0, DateTimeKind.Utc),
                EndTime = new DateTime(2026, 9, 10, 20, 0, 0, DateTimeKind.Utc),
                TotalMinutes = 120, Status = OvertimeStatus.Approved
            },
            new OvertimeRequest
            {
                EmployeeId = salesRep.Id, OvertimeDate = date, Reason = "Month-end close",
                StartTime = new DateTime(2026, 9, 10, 18, 0, 0, DateTimeKind.Utc),
                EndTime = new DateTime(2026, 9, 10, 19, 0, 0, DateTimeKind.Utc),
                TotalMinutes = 60, Status = OvertimeStatus.Approved
            });
        await Context.SaveChangesAsync();

        var response = await Service.GetOvertimeAsync(date, date);

        response.Data.Select(o => o.Department).Should().BeEquivalentTo(["Engineering", "Sales"]);
        response.Data.Should().NotContain(o => o.Department == "Unassigned");
    }

    [Fact]
    public async Task GetLeaveUtilization_FilteredByDepartment_MatchesTheEmployeesInThatDepartment()
    {
        // GetByYearAsync loaded LeaveType but never Employee, so
        // `b.Employee?.DepartmentId == departmentId.Value` compared null to a Guid and was always
        // false - a department filter on leave utilization silently returned nothing, for every
        // department, forever.
        var (engineering, sales) = await SeedTwoDepartmentsAsync();
        var engineer = AnEmployee("Reyes", "Ana");
        engineer.DepartmentId = engineering.Id;
        var salesRep = AnEmployee("Cruz", "Ben");
        salesRep.DepartmentId = sales.Id;
        Context.Employees.AddRange(engineer, salesRep);
        var leaveType = new LeaveType { Name = "Vacation Leave", Code = "VL", MaxDaysPerYear = 15 };
        Context.LeaveTypes.Add(leaveType);
        await Context.SaveChangesAsync();

        Context.LeaveBalances.AddRange(
            new LeaveBalance { EmployeeId = engineer.Id, LeaveTypeId = leaveType.Id, Year = 2026, TotalDays = 15, UsedDays = 5 },
            new LeaveBalance { EmployeeId = salesRep.Id, LeaveTypeId = leaveType.Id, Year = 2026, TotalDays = 15, UsedDays = 3 });
        await Context.SaveChangesAsync();

        var response = await Service.GetLeaveUtilizationAsync(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), departmentId: engineering.Id);

        response.Data.Should().ContainSingle().Which.TotalUsed.Should().Be(5m,
            "only the engineering employee's balance should survive the department filter");
    }
}
