using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PeopleCore.API.Authorization;
using PeopleCore.API.Controllers.Payroll;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Application.Payroll.FinalPay;
using PeopleCore.Domain.Enums;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class FinalPayControllerTests
{
    private readonly Mock<IFinalPayService> _service = new();
    private static readonly Guid SeparationId = Guid.NewGuid();

    private static readonly FinalPayRequest Request = new(
        new DateOnly(2026, 10, 15), null, null, null, null, [new FinalPayDeductionDto("Unreturned laptop", 5_000m)]);

    private static readonly FinalPaySummaryDto Summary = new(
        Guid.NewGuid(), "PR-2026-0042", PayrollRunStatus.Draft,
        new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 15),
        7m, false, true, 1_600m, 8_000m, 8_000m, [new FinalPayLeaveLineDto("Vacation Leave", 5m, true)], true,
        0m, 0m, null, null, null, null, 3,
        Request.Deductions, [new FinalPayDeductionLineDto("Unreturned laptop", 5_000m, 5_000m, 0m)],
        [], 1_200m, 20_000m, 13_800m, false, ["Return laptop"]);

    private FinalPayController Controller() => new(_service.Object);

    [Fact]
    public void RequiresPayrollManage()
    {
        typeof(FinalPayController).GetCustomAttribute<RequirePermissionAttribute>()!
            .AnyOf.Should().BeEquivalentTo([Permissions.PayrollManage]);
    }

    [Fact]
    public void IsRoutedUnderTheSeparation()
    {
        typeof(FinalPayController).GetCustomAttribute<RouteAttribute>()!
            .Template.Should().Be("api/separations/{separationId:guid}/final-pay");
    }

    [Fact]
    public async Task Get_Found_ReturnsTheSummary()
    {
        _service.Setup(s => s.GetAsync(SeparationId, It.IsAny<CancellationToken>())).ReturnsAsync(Summary);

        var result = await Controller().Get(SeparationId, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(Summary);
    }

    [Fact]
    public async Task Get_NoFinalPayYet_ReturnsNotFound()
    {
        _service.Setup(s => s.GetAsync(SeparationId, It.IsAny<CancellationToken>())).ReturnsAsync((FinalPaySummaryDto?)null);

        var result = await Controller().Get(SeparationId, CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Create_ReturnsCreatedAtGet()
    {
        _service.Setup(s => s.CreateAsync(SeparationId, Request, It.IsAny<CancellationToken>())).ReturnsAsync(Summary);

        var result = await Controller().Create(SeparationId, Request, CancellationToken.None);

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.ActionName.Should().Be(nameof(FinalPayController.Get));
        created.RouteValues!["separationId"].Should().Be(SeparationId);
        created.Value.Should().Be(Summary);
    }

    [Fact]
    public async Task Update_ReturnsTheRecomputedSummary()
    {
        _service.Setup(s => s.UpdateAsync(SeparationId, Request, It.IsAny<CancellationToken>())).ReturnsAsync(Summary);

        var result = await Controller().Update(SeparationId, Request, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(Summary);
    }
}
