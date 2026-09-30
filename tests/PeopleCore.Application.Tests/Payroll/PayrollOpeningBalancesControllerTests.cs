using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Moq;
using PeopleCore.API.Authorization;
using PeopleCore.API.Controllers.Payroll;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Application.Payroll.OpeningBalances;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class PayrollOpeningBalancesControllerTests
{
    private readonly Mock<IPayrollOpeningBalanceService> _service = new();
    private static readonly Guid BalanceId = Guid.NewGuid();

    private static readonly OpeningBalanceDto Balance = new(
        BalanceId, Guid.NewGuid(), "Maria Santos", "E-001", 2026, new DateOnly(2026, 3, 31),
        150_000m, 0m, 0m, 0m, 0m, 0m, 6_000m, 4_500m, 0m, []);

    private static readonly OpeningBalanceRequest Request = new(
        Balance.EmployeeId, 2026, new DateOnly(2026, 3, 31), 150_000m, 0m, 0m, 0m, 0m, 0m, 6_000m, 4_500m, 0m);

    private PayrollOpeningBalancesController Controller() => new(_service.Object);

    [Fact]
    public void RequiresPayrollManage()
    {
        typeof(PayrollOpeningBalancesController).GetCustomAttribute<RequirePermissionAttribute>()!
            .AnyOf.Should().BeEquivalentTo([Permissions.PayrollManage]);
    }

    [Fact]
    public void IsRoutedUnderPayrollOpeningBalances()
    {
        typeof(PayrollOpeningBalancesController).GetCustomAttribute<RouteAttribute>()!
            .Template.Should().Be("api/payroll-opening-balances");
    }

    [Theory]
    [InlineData(nameof(PayrollOpeningBalancesController.List), "GET", null)]
    [InlineData(nameof(PayrollOpeningBalancesController.Get), "GET", "{id:guid}")]
    [InlineData(nameof(PayrollOpeningBalancesController.Create), "POST", null)]
    [InlineData(nameof(PayrollOpeningBalancesController.Update), "PUT", "{id:guid}")]
    [InlineData(nameof(PayrollOpeningBalancesController.Delete), "DELETE", "{id:guid}")]
    public void EachActionHasItsRoute(string action, string verb, string? template)
    {
        var attribute = typeof(PayrollOpeningBalancesController).GetMethod(action)!.GetCustomAttribute<HttpMethodAttribute>()!;

        attribute.HttpMethods.Should().Equal(verb);
        attribute.Template.Should().Be(template);
    }

    [Fact]
    public void List_TakesTheYearFromTheQuery()
    {
        var parameter = typeof(PayrollOpeningBalancesController).GetMethod(nameof(PayrollOpeningBalancesController.List))!
            .GetParameters().Single(p => p.Name == "year");

        parameter.GetCustomAttribute<FromQueryAttribute>().Should().NotBeNull();
    }

    [Fact]
    public async Task List_ReturnsTheYearsBalances()
    {
        IReadOnlyList<OpeningBalanceDto> balances = [Balance];
        _service.Setup(s => s.ListAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync(balances);

        var result = await Controller().List(2026, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(balances);
    }

    [Fact]
    public async Task Get_ReturnsTheBalance()
    {
        _service.Setup(s => s.GetAsync(BalanceId, It.IsAny<CancellationToken>())).ReturnsAsync(Balance);

        var result = await Controller().Get(BalanceId, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(Balance);
    }

    [Fact]
    public async Task Create_ReturnsCreatedAtTheBalance()
    {
        _service.Setup(s => s.CreateAsync(Request, It.IsAny<CancellationToken>())).ReturnsAsync(Balance);

        var result = await Controller().Create(Request, CancellationToken.None);

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.ActionName.Should().Be(nameof(PayrollOpeningBalancesController.Get));
        created.RouteValues.Should().ContainKey("id").WhoseValue.Should().Be(BalanceId);
        created.Value.Should().Be(Balance);
    }

    [Fact]
    public async Task Update_ReturnsTheBalance()
    {
        _service.Setup(s => s.UpdateAsync(BalanceId, Request, It.IsAny<CancellationToken>())).ReturnsAsync(Balance);

        var result = await Controller().Update(BalanceId, Request, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(Balance);
    }

    [Fact]
    public async Task Delete_ReturnsNoContent()
    {
        var result = await Controller().Delete(BalanceId, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        _service.Verify(s => s.DeleteAsync(BalanceId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
