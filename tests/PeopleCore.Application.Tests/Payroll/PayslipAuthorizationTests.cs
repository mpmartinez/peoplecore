using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using PeopleCore.API.Controllers.Payroll;

namespace PeopleCore.Application.Tests.Payroll;

public class PayslipAuthorizationTests
{
    private static MethodInfo Action(string name) =>
        typeof(ReportsController).GetMethod(name, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"ReportsController has no public action '{name}'.");

    [Theory]
    [InlineData("GetMyPayslip")]
    [InlineData("GetMyPayslips")]
    public void SelfServiceActions_AcceptNoEmployeeId(string actionName)
    {
        // The route cannot be made to serve someone else's payslip because it has nowhere to
        // put an employee id. The caller is resolved from the employee_id claim instead.
        var parameters = Action(actionName).GetParameters().Select(p => p.Name!.ToLowerInvariant());

        parameters.Should().NotContain(p => p.Contains("employee"));
    }

    [Theory]
    [InlineData("GetPayslip")]
    [InlineData("GetRunPayslips")]
    public void HrActions_AdmitOnlyPayrollRoles(string actionName)
    {
        var attribute = Action(actionName).GetCustomAttribute<AuthorizeAttribute>();

        attribute.Should().NotBeNull("HR payslip actions must be role-restricted");
        attribute!.Roles!.Split(',', StringSplitOptions.TrimEntries)
            .Should().BeEquivalentTo(["Admin", "HRManager", "PayrollService"]);
    }
}
