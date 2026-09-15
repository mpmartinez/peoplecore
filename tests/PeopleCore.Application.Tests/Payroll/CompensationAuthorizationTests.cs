using System.Reflection;
using FluentAssertions;
using PeopleCore.API.Authorization;
using PeopleCore.API.Controllers.Payroll;
using PeopleCore.Application.Common.Authorization;

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
    public void PayrollController_RequiresPayrollManage(Type controller)
    {
        var attribute = controller.GetCustomAttribute<RequirePermissionAttribute>();

        attribute.Should().NotBeNull("every payroll controller must require a permission");
        attribute!.AnyOf.Should().BeEquivalentTo([Permissions.PayrollManage]);
    }
}
