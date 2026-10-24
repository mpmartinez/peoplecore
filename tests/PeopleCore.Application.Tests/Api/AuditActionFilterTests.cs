using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using PeopleCore.API.Auditing;
using PeopleCore.Application.Common.Auditing;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

public class AuditActionFilterTests
{
    private readonly RecordingAuditTrail _audit = new();

    private sealed record SecretBody(string Password);

    private async Task RunAsync(
        string method, IEnumerable<object> metadata, Dictionary<string, object?> arguments,
        Func<ActionExecutedContext, ActionExecutedContext> outcome, ClaimsPrincipal? user = null)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        if (user is not null) http.User = user;
        var descriptor = new ControllerActionDescriptor
        {
            ControllerName = "PayrollRuns",
            ActionName = "MarkPaid",
            EndpointMetadata = metadata.ToList(),
        };
        var actionContext = new ActionContext(http, new RouteData(), descriptor);
        var filters = new List<IFilterMetadata>();
        var executing = new ActionExecutingContext(actionContext, filters, arguments, controller: new object());

        await new AuditActionFilter(_audit).OnActionExecutionAsync(executing,
            () => Task.FromResult(outcome(new ActionExecutedContext(actionContext, filters, new object()))));
    }

    [Fact]
    public async Task AStateChangingCall_IsRecordedByDefaultName_WithItsSimpleArguments_AndNoBody()
    {
        var id = Guid.NewGuid();
        await RunAsync("PUT", [], new() { ["id"] = id, ["request"] = new SecretBody("hunter2"), ["newPassword"] = "x" },
            done => { done.Result = new OkResult(); return done; });

        var entry = _audit.Events.Should().ContainSingle().Subject;
        entry.Category.Should().Be(AuditCategory.Action);
        entry.Action.Should().Be("PayrollRuns.MarkPaid");
        entry.Outcome.Should().Be(AuditOutcome.Success);
        entry.StatusCode.Should().Be(200);
        entry.EntityType.Should().Be("PayrollRuns");
        entry.EntityId.Should().Be(id.ToString());
        entry.Metadata.Should().BeEquivalentTo(new Dictionary<string, object?> { ["id"] = id });
    }

    [Fact]
    public async Task ANamedAction_UsesItsName_AndADomainRefusalIsRecordedAsAFailure()
    {
        await RunAsync("PUT",
            [new AuditAsAttribute("payroll-run.marked-paid", "Marked a payroll run paid") { EntityType = "PayrollRun" }],
            new() { ["id"] = Guid.NewGuid() },
            done => { done.Exception = new DomainException("The run is already paid."); return done; });

        var entry = _audit.Events.Should().ContainSingle().Subject;
        entry.Action.Should().Be("payroll-run.marked-paid");
        entry.Summary.Should().Be("Marked a payroll run paid");
        entry.EntityType.Should().Be("PayrollRun");
        entry.Outcome.Should().Be(AuditOutcome.Failure);
        entry.StatusCode.Should().Be(400);
        entry.FailureReason.Should().Be("The run is already paid.");
    }

    [Fact]
    public async Task AProblemResponse_IsRecordedWithItsDetail()
    {
        await RunAsync("POST", [], new(),
            done =>
            {
                done.Result = new ObjectResult(new ProblemDetails { Title = "Not allowed", Detail = "Only an Admin may." }) { StatusCode = 403 };
                return done;
            });

        var entry = _audit.Events.Should().ContainSingle().Subject;
        entry.Outcome.Should().Be(AuditOutcome.Failure);
        entry.StatusCode.Should().Be(403);
        entry.FailureReason.Should().Be("Only an Admin may.");
    }

    [Fact]
    public async Task ARead_IsNotRecorded_UnlessItIsASensitiveAccess()
    {
        await RunAsync("GET", [], new(), done => { done.Result = new OkResult(); return done; });
        _audit.Events.Should().BeEmpty();

        var employee = Guid.NewGuid();
        await RunAsync("GET",
            [new AuditAccessAttribute("payslip.downloaded", "Downloaded a payslip") { IdRoute = "runId", EmployeeRoute = "employeeId" }],
            new() { ["runId"] = Guid.NewGuid(), ["employeeId"] = employee },
            done => { done.Result = new FileContentResult([1], "application/pdf"); return done; });

        var entry = _audit.Events.Should().ContainSingle().Subject;
        entry.Category.Should().Be(AuditCategory.Access);
        entry.Action.Should().Be("payslip.downloaded");
        entry.SubjectEmployeeId.Should().Be(employee);
    }

    [Fact]
    public async Task AFailedAccess_IsNotRecordedAsAnAccess()
    {
        await RunAsync("GET", [new AuditAccessAttribute("payslip.downloaded", "Downloaded a payslip")], new(),
            done => { done.Result = new NotFoundResult(); return done; });

        _audit.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task AnAccessToTheCallersOwnRecord_NamesTheCallerAsTheEmployee()
    {
        var employee = Guid.NewGuid();
        var caller = new ClaimsPrincipal(new ClaimsIdentity([new Claim("employee_id", employee.ToString())], "Bearer"));

        await RunAsync("GET", [new AuditAccessAttribute("payslips.listed-own", "Viewed own payslips") { SubjectIsCaller = true }],
            new(), done => { done.Result = new OkResult(); return done; }, caller);

        _audit.Events.Should().ContainSingle().Which.SubjectEmployeeId.Should().Be(employee);
    }

    [Fact]
    public async Task AControllerThatRecordsItsOwnEvents_IsSkipped()
    {
        await RunAsync("POST", [new SkipAuditActionAttribute()], new(), done => { done.Result = new OkResult(); return done; });

        _audit.Events.Should().BeEmpty();
    }
}
