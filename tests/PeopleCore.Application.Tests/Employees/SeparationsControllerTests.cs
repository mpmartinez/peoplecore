using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PeopleCore.API.Authorization;
using PeopleCore.API.Controllers.Employees;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Application.Employees.DTOs;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Domain.Enums;
using Xunit;

namespace PeopleCore.Application.Tests.Employees;

public class SeparationsControllerTests
{
    private readonly Mock<ISeparationService> _service = new();
    private static readonly Guid SeparationId = Guid.NewGuid();
    private static readonly Guid ItemId = Guid.NewGuid();

    private static readonly SeparationDto Dto = new(
        SeparationId, Guid.NewGuid(), "Juan Dela Cruz", "EMP-001", "Clerk",
        SeparationType.Resignation, null, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1),
        "Personal reasons", SeparationStatus.NoticeGiven, "hr@acme.com", null, null,
        new DateOnly(2026, 10, 31), false, 0, 0, [], null, null, null);

    private SeparationsController Controller() => new(_service.Object);

    [Fact]
    public void RequiresEmployeesManage()
    {
        typeof(SeparationsController).GetCustomAttribute<RequirePermissionAttribute>()!
            .AnyOf.Should().BeEquivalentTo([Permissions.EmployeesManage]);
    }

    [Fact]
    public async Task List_ReturnsEverySeparation()
    {
        _service.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([Dto]);

        var result = await Controller().List(CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeEquivalentTo(new[] { Dto });
    }

    [Fact]
    public async Task Get_Found_ReturnsIt()
    {
        _service.Setup(s => s.GetAsync(SeparationId, It.IsAny<CancellationToken>())).ReturnsAsync(Dto);

        var result = await Controller().Get(SeparationId, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(Dto);
    }

    [Fact]
    public async Task Get_Missing_ReturnsNotFound()
    {
        _service.Setup(s => s.GetAsync(SeparationId, It.IsAny<CancellationToken>())).ReturnsAsync((SeparationDto?)null);

        var result = await Controller().Get(SeparationId, CancellationToken.None);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Record_ReturnsCreatedAtGet()
    {
        var request = new RecordSeparationRequest(
            Dto.EmployeeId, SeparationType.Resignation, null, Dto.NoticeDate, Dto.LastWorkingDay, "Personal reasons");
        _service.Setup(s => s.RecordAsync(request, It.IsAny<CancellationToken>())).ReturnsAsync(Dto);

        var result = await Controller().Record(request, CancellationToken.None);

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.ActionName.Should().Be(nameof(SeparationsController.Get));
        created.RouteValues!["id"].Should().Be(Dto.Id);
        created.Value.Should().Be(Dto);
    }

    [Fact]
    public async Task MarkSeparated_ReturnsTheUpdatedDto()
    {
        _service.Setup(s => s.MarkSeparatedAsync(SeparationId, It.IsAny<CancellationToken>())).ReturnsAsync(Dto);

        var result = await Controller().MarkSeparated(SeparationId, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(Dto);
    }

    [Fact]
    public async Task Cancel_ReturnsNoContent()
    {
        _service.Setup(s => s.CancelAsync(SeparationId, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await Controller().Cancel(SeparationId, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        _service.Verify(s => s.CancelAsync(SeparationId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddClearanceItem_ReturnsTheUpdatedDto()
    {
        var request = new AddClearanceItemRequest("Return laptop");
        _service.Setup(s => s.AddClearanceItemAsync(SeparationId, "Return laptop", It.IsAny<CancellationToken>())).ReturnsAsync(Dto);

        var result = await Controller().AddClearanceItem(SeparationId, request, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(Dto);
    }

    [Fact]
    public async Task ClearItem_ReturnsTheUpdatedDto()
    {
        var request = new ClearItemRequest("All good");
        _service.Setup(s => s.ClearItemAsync(SeparationId, ItemId, "All good", It.IsAny<CancellationToken>())).ReturnsAsync(Dto);

        var result = await Controller().ClearItem(SeparationId, ItemId, request, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(Dto);
    }

    [Fact]
    public async Task UndoClearItem_ReturnsTheUpdatedDto()
    {
        _service.Setup(s => s.UndoClearItemAsync(SeparationId, ItemId, It.IsAny<CancellationToken>())).ReturnsAsync(Dto);

        var result = await Controller().UndoClearItem(SeparationId, ItemId, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(Dto);
    }

    [Fact]
    public async Task DeleteClearanceItem_ReturnsTheUpdatedDto()
    {
        _service.Setup(s => s.DeleteClearanceItemAsync(SeparationId, ItemId, It.IsAny<CancellationToken>())).ReturnsAsync(Dto);

        var result = await Controller().DeleteClearanceItem(SeparationId, ItemId, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(Dto);
    }
}
