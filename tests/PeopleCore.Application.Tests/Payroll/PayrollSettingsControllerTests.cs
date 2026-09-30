using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Moq;
using PeopleCore.API.Controllers.Payroll;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Interfaces;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

/// <summary>
/// api/payroll-settings/default reads and changes the settings row payroll actually computes from,
/// so a caller needn't know which company it belongs to.
/// </summary>
public class PayrollSettingsControllerTests
{
    private readonly Mock<IPayrollSettingsService> _service = new();

    private PayrollSettingsController Controller() => new(_service.Object);

    private static readonly PayrollSettingsDto Settings = new(Guid.NewGuid(), 0.05m, 250m, 2_500m, 0.02m, 0.01m, 1_500m,
        0.02m, 10_000m, 365m, null, null, true);

    [Theory]
    [InlineData(nameof(PayrollSettingsController.GetDefault), "GET", "default")]
    [InlineData(nameof(PayrollSettingsController.UpdateDefault), "PUT", "default")]
    [InlineData(nameof(PayrollSettingsController.GetByCompany), "GET", "{companyId:guid}")]
    [InlineData(nameof(PayrollSettingsController.Update), "PUT", "{companyId:guid}")]
    public void EachActionHasItsRoute(string action, string verb, string template)
    {
        var attribute = typeof(PayrollSettingsController).GetMethod(action)!.GetCustomAttribute<HttpMethodAttribute>()!;

        attribute.HttpMethods.Should().Equal(verb);
        attribute.Template.Should().Be(template);
    }

    [Fact]
    public async Task GetDefault_ReturnsTheRowPayrollUses()
    {
        _service.Setup(s => s.GetDefaultAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Settings);

        var result = await Controller().GetDefault(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(Settings);
    }

    [Fact]
    public async Task UpdateDefault_SavesIt_AndAnswersNoContent()
    {
        var result = await Controller().UpdateDefault(Settings, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        _service.Verify(s => s.UpdateDefaultAsync(Settings, It.IsAny<CancellationToken>()), Times.Once);
    }
}
