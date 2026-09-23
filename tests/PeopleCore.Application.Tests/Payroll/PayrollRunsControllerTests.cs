using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PeopleCore.API.Controllers.Payroll;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Domain.Enums;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class PayrollRunsControllerTests
{
    private readonly Mock<IPayrollRunService> _service = new();
    private static readonly Guid RunId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    private static readonly PayrollRunDto Run = new(
        RunId, "PR-2026-0041", "Oct 1-15, 2026", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 15),
        new DateOnly(2026, 10, 20), PayFrequency.SemiMonthly, PayrollRunStatus.Draft, 1,
        20_000m, 2_000m, 18_000m, DateTime.UtcNow, null, 0, []);

    [Fact]
    public void RemoveEmployee_IsADeleteOnTheRunsEmployee()
    {
        typeof(PayrollRunsController).GetMethod(nameof(PayrollRunsController.RemoveEmployee))!
            .GetCustomAttribute<HttpDeleteAttribute>()!
            .Template.Should().Be("{id:guid}/employees/{employeeId:guid}");
    }

    [Fact]
    public async Task RemoveEmployee_ReturnsTheUpdatedRun()
    {
        _service.Setup(s => s.RemoveEmployeeAsync(RunId, EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(Run);

        var result = await new PayrollRunsController(_service.Object).RemoveEmployee(RunId, EmployeeId, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(Run);
    }
}
