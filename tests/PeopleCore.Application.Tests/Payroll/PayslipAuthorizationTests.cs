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

    [Fact]
    public void GetMyPayslip_TakesExactlyRunIdAndCancellationToken()
    {
        // Asserting no parameter NAME contains "employee" is defeatable by a future
        // [FromQuery] Guid targetId (or similar) that adds a second identifier under a
        // different name without ever saying "employee". Pinning the exact parameter SET closes
        // that gap: nothing beyond {runId, ct} can be added without this test failing.
        var parameters = Action("GetMyPayslip").GetParameters().Select(p => p.Name).ToList();

        parameters.Should().BeEquivalentTo(["runId", "ct"]);
    }

    [Fact]
    public void GetMyPayslips_TakesOnlyCancellationToken()
    {
        var parameters = Action("GetMyPayslips").GetParameters().Select(p => p.Name).ToList();

        parameters.Should().BeEquivalentTo(["ct"]);
    }

    [Theory]
    [InlineData("GetMyPayslip")]
    [InlineData("GetMyPayslips")]
    public void SelfServiceActions_CarryAuthorize(string actionName)
    {
        // ReportsController has no class-level [Authorize] and there is no fallback policy
        // configured, so an action with no attribute of its own is anonymous. Nothing else
        // guards that - dropping [Authorize] from one of these would make it reachable by
        // anyone, and no other test would notice.
        var attribute = Action(actionName).GetCustomAttribute<AuthorizeAttribute>();

        attribute.Should().NotBeNull($"{actionName} must require an authenticated caller");
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
