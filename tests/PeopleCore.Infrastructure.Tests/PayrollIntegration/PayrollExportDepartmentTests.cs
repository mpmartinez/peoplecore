using FluentAssertions;
using PeopleCore.Application.PayrollIntegration.Services;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Organization;
using PeopleCore.Domain.Enums;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.PayrollIntegration;

/// <summary>
/// The payroll master data export showed a blank department for every employee: it reads
/// employees through <c>IEmployeeRepository.GetAllAsync</c>, the same unfiltered
/// <c>Repository&lt;T&gt;.GetAllAsync</c> the HR analytics headcount used, which never loaded
/// Employee.Department. Fixing that Include (see <see cref="Analytics.HRAnalyticsDepartmentTests"/>)
/// fixes this export too - proven here against a real database.
///
/// The same export also showed a blank position title and null SSS/PhilHealth/Pag-IBIG/TIN for
/// every employee, for the same reason: <c>GetAllAsync</c> loaded neither Employee.Position nor
/// Employee.GovernmentIds. That is covered by
/// <see cref="GetEmployeeMasterData_IncludesTheEmployeesPositionAndGovernmentIds"/> below, which
/// the export now satisfies through a dedicated <c>GetAllForExportAsync</c> that loads all three.
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

    [Fact]
    public async Task GetEmployeeMasterData_IncludesTheEmployeesPositionAndGovernmentIds()
    {
        var company = ACompany();
        var engineering = new Department { Name = "Engineering", CompanyId = company.Id };
        Context.Companies.Add(company);
        Context.Departments.Add(engineering);
        await Context.SaveChangesAsync();

        var seniorEngineer = new Position { DepartmentId = engineering.Id, Title = "Senior Engineer" };
        Context.Positions.Add(seniorEngineer);
        await Context.SaveChangesAsync();

        var engineer = AnEmployee("Reyes", "Ana");
        engineer.DepartmentId = engineering.Id;
        engineer.PositionId = seniorEngineer.Id;
        engineer.IsActive = true;
        Context.Employees.Add(engineer);
        await Context.SaveChangesAsync();

        Context.EmployeeGovernmentIds.AddRange(
            new EmployeeGovernmentId { EmployeeId = engineer.Id, IdType = GovernmentIdType.SSS, IdNumber = "SSS-001" },
            new EmployeeGovernmentId { EmployeeId = engineer.Id, IdType = GovernmentIdType.PhilHealth, IdNumber = "PH-002" },
            new EmployeeGovernmentId { EmployeeId = engineer.Id, IdType = GovernmentIdType.PagIbig, IdNumber = "PI-003" },
            new EmployeeGovernmentId { EmployeeId = engineer.Id, IdType = GovernmentIdType.TIN, IdNumber = "TIN-004" });
        await Context.SaveChangesAsync();

        var data = await Service.GetEmployeeMasterDataAsync();

        var dto = data.Should().ContainSingle().Subject;
        dto.PositionTitle.Should().Be("Senior Engineer");
        dto.SssNumber.Should().Be("SSS-001");
        dto.PhilHealthNumber.Should().Be("PH-002");
        dto.PagIbigNumber.Should().Be("PI-003");
        dto.TinNumber.Should().Be("TIN-004");
    }

    [Fact]
    public async Task GetEmployeeMasterData_EmployeeWithNoDepartment_ShowsEmptyDepartmentAndPosition()
    {
        var employee = AnEmployee("Santos", "Carlo");
        employee.IsActive = true;
        Context.Employees.Add(employee);
        await Context.SaveChangesAsync();

        var data = await Service.GetEmployeeMasterDataAsync();

        var dto = data.Should().ContainSingle().Subject;
        dto.DepartmentName.Should().BeEmpty();
        dto.PositionTitle.Should().BeEmpty();
    }
}
