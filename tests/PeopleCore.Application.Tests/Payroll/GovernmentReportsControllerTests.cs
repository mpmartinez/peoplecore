using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PeopleCore.API.Authorization;
using PeopleCore.API.Controllers.Payroll;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Application.Payroll.GovernmentReports;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class GovernmentReportsControllerTests
{
    private readonly Mock<IGovernmentReportService> _service = new();

    private static readonly GovernmentReportDto Report = new(
        "sss", "SSS contributions", 2026, 3, "Pay earned in March 2026",
        new GovernmentReportEmployerDto("Acme", null, "", null, ""), ["Employee"], [], ["Total"], [], [], []);

    [Fact]
    public void RequiresPayrollManage()
    {
        typeof(GovernmentReportsController).GetCustomAttribute<RequirePermissionAttribute>()!
            .AnyOf.Should().BeEquivalentTo([Permissions.PayrollManage]);
    }

    [Fact]
    public async Task Get_ReturnsTheReport()
    {
        _service.Setup(s => s.BuildAsync("sss", 2026, 3, It.IsAny<CancellationToken>())).ReturnsAsync(Report);

        var result = await new GovernmentReportsController(_service.Object).Get("sss", 2026, 3, null, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(Report);
    }

    [Fact]
    public async Task Get_WithCsv_ReturnsTheFile()
    {
        _service.Setup(s => s.BuildAsync("sss", 2026, 3, It.IsAny<CancellationToken>())).ReturnsAsync(Report);

        var result = await new GovernmentReportsController(_service.Object).Get("sss", 2026, 3, "csv", CancellationToken.None);

        var file = result.Should().BeOfType<FileContentResult>().Subject;
        file.ContentType.Should().Be("text/csv");
        file.FileDownloadName.Should().Be("sss-2026-03.csv");
    }
}
