using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PeopleCore.API.Authorization;
using PeopleCore.API.Controllers.Payroll;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Application.Payroll.GovernmentReports;
using PeopleCore.Domain.Exceptions;
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

    private static readonly GovernmentReportDto AnnualReport = new(
        "1604c", "BIR 1604-C alphalist", 2026, 0, "Paid in 2026",
        new GovernmentReportEmployerDto("Acme", null, "", null, ""), [], [], [], [], [], []);

    [Fact]
    public async Task Get_ReturnsTheReport()
    {
        _service.Setup(s => s.BuildAsync("sss", 2026, 3, It.IsAny<CancellationToken>())).ReturnsAsync(Report);

        var result = await new GovernmentReportsController(_service.Object).Get("sss", 2026, (int?)3, null, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(Report);
    }

    [Fact]
    public async Task Get_WithCsv_ReturnsTheFile()
    {
        _service.Setup(s => s.BuildAsync("sss", 2026, 3, It.IsAny<CancellationToken>())).ReturnsAsync(Report);

        var result = await new GovernmentReportsController(_service.Object).Get("sss", 2026, (int?)3, "csv", CancellationToken.None);

        var file = result.Should().BeOfType<FileContentResult>().Subject;
        file.ContentType.Should().Be("text/csv");
        file.FileDownloadName.Should().Be("sss-2026-03.csv");
    }

    [Fact]
    public async Task Get_1604C_BuildsTheAnnualReport_WithoutAMonth()
    {
        _service.Setup(s => s.BuildAnnualAsync("1604c", 2026, It.IsAny<CancellationToken>())).ReturnsAsync(AnnualReport);

        var result = await new GovernmentReportsController(_service.Object).Get("1604c", 2026, null, null, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(AnnualReport);
        _service.Verify(s => s.BuildAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Get_1604C_WithCsv_ReturnsTheFileNamedByYearOnly()
    {
        _service.Setup(s => s.BuildAnnualAsync("1604c", 2026, It.IsAny<CancellationToken>())).ReturnsAsync(AnnualReport);

        var result = await new GovernmentReportsController(_service.Object).Get("1604c", 2026, null, "csv", CancellationToken.None);

        var file = result.Should().BeOfType<FileContentResult>().Subject;
        file.FileDownloadName.Should().Be("1604c-2026.csv");
    }

    [Fact]
    public async Task Get_AMonthlyReportWithoutAMonth_IsRefused()
    {
        var act = () => new GovernmentReportsController(_service.Object).Get("sss", 2026, null, null, CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Be("Choose a month.");
    }
}
