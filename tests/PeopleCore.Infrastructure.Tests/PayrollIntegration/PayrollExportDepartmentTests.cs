using FluentAssertions;
using PeopleCore.Application.PayrollIntegration.Services;
using PeopleCore.Domain.Entities.Organization;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.PayrollIntegration;

/// <summary>
/// The payroll master data export showed a blank department for every employee: it reads
/// employees through <c>IEmployeeRepository.GetAllAsync</c>, the same unfiltered
/// <c>Repository&lt;T&gt;.GetAllAsync</c> the HR analytics headcount used, which never loaded
/// Employee.Department. Fixing that Include (see <see cref="Analytics.HRAnalyticsDepartmentTests"/>)
/// fixes this export too - proven here against a real database.
/// </summary>
public class PayrollExportDepartmentTests : DatabaseTestBase
{
    public PayrollExportDepartmentTests(PostgresFixture fixture) : base(fixture) { }

    private PayrollExportService Service => new(
        new EmployeeRepository(NewContext()),
        new AttendanceRepository(NewContext()),
        new LeaveRequestRepository(NewContext()),
        new OvertimeRepository(NewContext()));

    [Fact]
    public async Task GetEmployeeMasterData_IncludesTheEmployeesRealDepartmentName()
    {
        var company = ACompany();
        var engineering = new Department { Name = "Engineering", CompanyId = company.Id };
        Context.Companies.Add(company);
        Context.Departments.Add(engineering);

        var engineer = AnEmployee("Reyes", "Ana");
        engineer.DepartmentId = engineering.Id;
        engineer.IsActive = true;
        Context.Employees.Add(engineer);
        await Context.SaveChangesAsync();

        var data = await Service.GetEmployeeMasterDataAsync();

        data.Should().ContainSingle().Which.DepartmentName.Should().Be("Engineering");
    }
}
