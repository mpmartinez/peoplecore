using FluentAssertions;
using PeopleCore.Application.Analytics.Services;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Organization;
using PeopleCore.Domain.Entities.Performance;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Analytics;

/// <summary>
/// The executive performance overview grouped every review under "Unassigned" - and every cycle
/// under "Unknown" - because <c>PerformanceReviewRepository.GetAllAsync</c> (the base
/// <c>Repository&lt;T&gt;.GetAllAsync</c>, never overridden) loaded no navigations at all, so
/// <c>r.Employee?.Department?.Name</c> and <c>r.ReviewCycle?.Name</c> both fell back. Run against
/// real repositories and a real database.
/// </summary>
public class ExecutiveAnalyticsDepartmentTests : DatabaseTestBase
{
    public ExecutiveAnalyticsDepartmentTests(PostgresFixture fixture) : base(fixture) { }

    private ExecutiveAnalyticsService Service => new(
        new EmployeeRepository(NewContext()),
        new LeaveBalanceRepository(NewContext()),
        new PerformanceReviewRepository(NewContext()),
        new HRAnalyticsService(
            new EmployeeRepository(NewContext()),
            new AttendanceRepository(NewContext()),
            new OvertimeRepository(NewContext()),
            new LeaveBalanceRepository(NewContext()),
            new ApplicantRepository(NewContext()),
            new PerformanceReviewRepository(NewContext())));

    [Fact]
    public async Task GetPerformanceOverview_GroupsByTheEmployeesRealDepartmentAndTheReviewsRealCycle()
    {
        var company = ACompany();
        var engineering = new Department { Name = "Engineering", CompanyId = company.Id };
        Context.Companies.Add(company);
        Context.Departments.Add(engineering);

        var engineer = AnEmployee("Reyes", "Ana");
        engineer.DepartmentId = engineering.Id;
        var reviewer = AnEmployee("Santos", "Liza");
        Context.Employees.AddRange(engineer, reviewer);

        var cycle = new ReviewCycle
        {
            Name = "2026 Mid-Year", Year = 2026, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 6, 30)
        };
        Context.ReviewCycles.Add(cycle);
        await Context.SaveChangesAsync();

        Context.PerformanceReviews.Add(new PerformanceReview
        {
            EmployeeId = engineer.Id, ReviewCycleId = cycle.Id, ReviewerId = reviewer.Id,
            FinalScore = 4.2m, CompletedAt = DateTime.UtcNow
        });
        await Context.SaveChangesAsync();

        var overview = await Service.GetPerformanceOverviewAsync();

        overview.Should().ContainSingle().Which.Should().Match<PeopleCore.Application.Analytics.DTOs.PerformanceOverview>(
            o => o.Department == "Engineering" && o.Cycle == "2026 Mid-Year" && o.AverageScore == 4.2m);
    }
}
