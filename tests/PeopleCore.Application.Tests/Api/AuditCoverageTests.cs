using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.API.Auditing;
using PeopleCore.API.Controllers.Auth;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// Keeps the audit trail complete as the API grows: the action filter records every
/// state-changing call except on the controllers allowed to opt out, and every sensitive read
/// says so.
/// </summary>
public class AuditCoverageTests
{
    private static readonly Assembly Api = typeof(AuthController).Assembly;

    private static IEnumerable<Type> Controllers =>
        Api.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

    [Fact]
    public void OnlyTheAuthController_OptsOutOfTheActionFilter()
    {
        // It records each sign-in, sign-out and password event itself, with the reason a sign-in
        // failed - which the filter cannot see.
        Controllers.Where(c => c.GetCustomAttribute<SkipAuditActionAttribute>() is not null)
            .Select(c => c.Name)
            .Should().Equal(nameof(AuthController));
    }

    [Theory]
    [InlineData("ReportsController", "GetPayslip")]
    [InlineData("ReportsController", "GetRunPayslips")]
    [InlineData("ReportsController", "GetMyPayslip")]
    [InlineData("ReportsController", "GetMyPayslips")]
    [InlineData("Bir2316Controller", "GetPreview")]
    [InlineData("Bir2316Controller", "Generate")]
    [InlineData("Bir2316Controller", "GenerateAll")]
    [InlineData("Bir2316Controller", "GetInputs")]
    [InlineData("GovernmentReportsController", "Get")]
    [InlineData("PayrollExportController", "GetEmployees")]
    [InlineData("PayrollExportController", "GetAttendanceSummary")]
    [InlineData("PayrollExportController", "GetApprovedLeaves")]
    [InlineData("PayrollExportController", "GetApprovedOvertime")]
    [InlineData("PayrollExportController", "GetStatusChanges")]
    [InlineData("EmployeeCompensationController", "GetByEmployee")]
    [InlineData("FinalPayController", "Get")]
    [InlineData("EmployeesController", "GetGovernmentIds")]
    [InlineData("EmployeesController", "GetDownloadUrl")]
    [InlineData("EmployeesController", "Coe")]
    [InlineData("LeaveController", "GetDocument")]
    [InlineData("AuditLogsController", "Export")]
    public void ASensitiveRead_IsRecordedAsAnAccess(string controller, string action)
    {
        var type = Controllers.SingleOrDefault(c => c.Name == controller);
        type.Should().NotBeNull($"{controller} is expected to exist");

        type!.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.Name == action)
            .Should().NotBeEmpty($"{controller}.{action} is expected to exist")
            .And.OnlyContain(m => m.GetCustomAttribute<AuditAccessAttribute>() != null,
                $"{controller}.{action} hands out sensitive data, so each call belongs in the audit trail");
    }
}
