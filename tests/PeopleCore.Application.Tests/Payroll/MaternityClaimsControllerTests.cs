using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PeopleCore.API.Authorization;
using PeopleCore.API.Controllers.Payroll;
using PeopleCore.API.Middleware;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Application.Payroll.Maternity;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class MaternityClaimsControllerTests
{
    private readonly Mock<IMaternityClaimService> _service = new();
    private static readonly Guid ClaimId = Guid.NewGuid();

    private static readonly MaternityClaimDto Claim = new(
        ClaimId, Guid.NewGuid(), Guid.NewGuid(), "Maria Santos", new DateOnly(2026, 8, 10), new DateOnly(2026, 11, 22),
        105m, 800m, 84_000m, MaternityClaimStatus.Draft, null, null, null, null, null, null);

    private MaternityClaimsController Controller() => new(_service.Object);

    [Fact]
    public void RequiresPayrollManage()
    {
        typeof(MaternityClaimsController).GetCustomAttribute<RequirePermissionAttribute>()!
            .AnyOf.Should().BeEquivalentTo([Permissions.PayrollManage]);
    }

    [Fact]
    public void IsRoutedUnderMaternityClaims()
    {
        typeof(MaternityClaimsController).GetCustomAttribute<RouteAttribute>()!
            .Template.Should().Be("api/maternity-claims");
    }

    [Theory]
    [InlineData(nameof(MaternityClaimsController.List), "GET", null)]
    [InlineData(nameof(MaternityClaimsController.Eligible), "GET", "eligible")]
    [InlineData(nameof(MaternityClaimsController.Ready), "GET", "ready")]
    [InlineData(nameof(MaternityClaimsController.Create), "POST", "{leaveRequestId:guid}")]
    [InlineData(nameof(MaternityClaimsController.Suggestion), "GET", "{id:guid}/suggestion")]
    [InlineData(nameof(MaternityClaimsController.SetAllowance), "PUT", "{id:guid}/allowance")]
    [InlineData(nameof(MaternityClaimsController.Reimburse), "PUT", "{id:guid}/reimburse")]
    [InlineData(nameof(MaternityClaimsController.Deny), "PUT", "{id:guid}/deny")]
    public void EachActionHasItsRoute(string action, string verb, string? template)
    {
        var attribute = typeof(MaternityClaimsController).GetMethod(action)!.GetCustomAttribute<HttpMethodAttribute>()!;

        attribute.HttpMethods.Should().Equal(verb);
        attribute.Template.Should().Be(template);
    }

    [Fact]
    public async Task List_ReturnsTheSummary()
    {
        var summary = new MaternityClaimsSummaryDto([Claim], 0m);
        _service.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(summary);

        var result = await Controller().List(CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(summary);
    }

    [Fact]
    public async Task Eligible_ReturnsTheRequests()
    {
        IReadOnlyList<EligibleMaternityLeaveDto> eligible =
            [new(Guid.NewGuid(), Guid.NewGuid(), "Maria Santos", new DateOnly(2026, 8, 10), new DateOnly(2026, 11, 22), 105m)];
        _service.Setup(s => s.EligibleAsync(It.IsAny<CancellationToken>())).ReturnsAsync(eligible);

        var result = await Controller().Eligible(CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(eligible);
    }

    [Fact]
    public async Task Ready_ReturnsTheEmployeeIds()
    {
        IReadOnlyList<Guid> ids = [Guid.NewGuid()];
        _service.Setup(s => s.ReadyEmployeeIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(ids);

        var result = await Controller().Ready(CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(ids);
    }

    [Fact]
    public async Task Create_ReturnsCreatedAtTheList()
    {
        var leaveRequestId = Guid.NewGuid();
        _service.Setup(s => s.CreateAsync(leaveRequestId, It.IsAny<CancellationToken>())).ReturnsAsync(Claim);

        var result = await Controller().Create(leaveRequestId, CancellationToken.None);

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.ActionName.Should().Be(nameof(MaternityClaimsController.List));
        created.Value.Should().Be(Claim);
    }

    [Fact]
    public async Task Suggestion_ReturnsTheSuggestion()
    {
        var suggestion = new SuggestedAllowanceDto(861.11m, 8, new DateOnly(2025, 4, 1), new DateOnly(2026, 3, 31));
        _service.Setup(s => s.SuggestAsync(ClaimId, It.IsAny<CancellationToken>())).ReturnsAsync(suggestion);

        var result = await Controller().Suggestion(ClaimId, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(suggestion);
    }

    [Fact]
    public async Task SetAllowance_ReturnsTheClaim()
    {
        var request = new SetAllowanceRequest(800m);
        _service.Setup(s => s.SetAllowanceAsync(ClaimId, request, It.IsAny<CancellationToken>())).ReturnsAsync(Claim);

        var result = await Controller().SetAllowance(ClaimId, request, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(Claim);
    }

    [Fact]
    public async Task Reimburse_ReturnsTheClaim()
    {
        var request = new ReimburseRequest(new DateOnly(2026, 11, 3), 84_000m, null);
        _service.Setup(s => s.ReimburseAsync(ClaimId, request, It.IsAny<CancellationToken>())).ReturnsAsync(Claim);

        var result = await Controller().Reimburse(ClaimId, request, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(Claim);
    }

    [Fact]
    public async Task Deny_ReturnsTheClaim()
    {
        var request = new DenyRequest("Not enough contributions");
        _service.Setup(s => s.DenyAsync(ClaimId, request, It.IsAny<CancellationToken>())).ReturnsAsync(Claim);

        var result = await Controller().Deny(ClaimId, request, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(Claim);
    }

    [Fact]
    public void DenyRequest_NoteIsNullable_SoMvcDoesNotRequireItBeforeTheServiceCanExplain()
    {
        // MVC treats a non-nullable reference type as [Required] and answers a missing one with a
        // ValidationProblem that has no detail, so the service's message would never reach HR.
        var nullability = new NullabilityInfoContext();
        var property = typeof(DenyRequest).GetProperty(nameof(DenyRequest.Note))!;
        var parameter = typeof(DenyRequest).GetConstructors().Single().GetParameters().Single();

        nullability.Create(property).ReadState.Should().Be(NullabilityState.Nullable);
        nullability.Create(parameter).WriteState.Should().Be(NullabilityState.Nullable);
    }

    [Fact]
    public async Task Deny_WithoutANote_ReachesTheService_AndItsMessageComesBackAsThe400Detail()
    {
        _service.Setup(s => s.DenyAsync(ClaimId, It.Is<DenyRequest>(r => r.Note == null), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DomainException("Explain why SSS denied the claim."));
        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();
        var pipeline = new ExceptionHandlingMiddleware(
            async _ => await Controller().Deny(ClaimId, new DenyRequest(null), CancellationToken.None),
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        await pipeline.InvokeAsync(http);

        http.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        http.Response.Body.Position = 0;
        using var problem = await JsonDocument.ParseAsync(http.Response.Body);
        problem.RootElement.GetProperty("detail").GetString().Should().Be("Explain why SSS denied the claim.");
    }
}
