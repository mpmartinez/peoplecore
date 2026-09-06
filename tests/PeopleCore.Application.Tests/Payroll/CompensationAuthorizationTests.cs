using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using PeopleCore.API.Controllers.Payroll;

namespace PeopleCore.Application.Tests.Payroll;

public class CompensationAuthorizationTests
{
    public static TheoryData<Type> PayrollControllers => new()
    {
        typeof(EmployeeCompensationController),
        typeof(PayrollRunsController),
        typeof(PayrollSettingsController)
    };

    [Theory]
    [MemberData(nameof(PayrollControllers))]
    public void PayrollController_AdmitsOnlyPayrollRoles(Type controller)
    {
        var attribute = controller.GetCustomAttribute<AuthorizeAttribute>();

        attribute.Should().NotBeNull("every payroll controller must be role-restricted");

        var roles = attribute!.Roles!.Split(',', StringSplitOptions.TrimEntries);
        roles.Should().BeEquivalentTo(["Admin", "HRManager", "PayrollService"]);
        roles.Should().NotContain("Manager");
        roles.Should().NotContain("Employee");
    }
}
