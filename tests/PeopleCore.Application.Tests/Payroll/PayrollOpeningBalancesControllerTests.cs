using System.Reflection;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Moq;
using PeopleCore.API.Authorization;
using PeopleCore.API.Controllers.Payroll;
using PeopleCore.API.Filters;
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
    [InlineData(nameof(PayrollOpeningBalancesController.Template), "GET", "template")]
    [InlineData(nameof(PayrollOpeningBalancesController.Import), "POST", "import")]
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

    // ── The CSV template and import ──────────────────────────────────────────

    private static IFormFile AFile(string text = "EmployeeNumber", long? length = null)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return new FormFile(new MemoryStream(bytes), 0, length ?? bytes.Length, "file", "balances.csv");
    }

    [Fact]
    public void Template_IsTheTemplatesCsv()
    {
        var result = Controller().Template();

        var file = result.Should().BeOfType<FileContentResult>().Subject;
        file.ContentType.Should().Be("text/csv");
        file.FileDownloadName.Should().Be("opening-balances-template.csv");
        file.FileContents.Should().Equal(OpeningBalanceCsv.Template());
    }

    [Fact]
    public void Import_CapsTheRequestBody_AtTwoMegabytesAndFraming_AndRefusesMoreReadably()
    {
        var action = typeof(PayrollOpeningBalancesController).GetMethod(nameof(PayrollOpeningBalancesController.Import))!;
        const long cap = 2 * 1024 * 1024 + 64 * 1024;

        action.GetCustomAttributesData()
              .Single(a => a.AttributeType == typeof(RequestSizeLimitAttribute))
              .ConstructorArguments[0].Value.Should().Be(cap);
        action.GetCustomAttribute<RequestFormLimitsAttribute>()!.MultipartBodyLengthLimit.Should().Be(cap);
        action.GetCustomAttribute<RefuseOversizedFormAttribute>()!.Message.Should().Be("Choose a CSV file of at most 2 MB.");
    }

    [Fact]
    public void Import_TakesTheFileFromTheForm()
    {
        var parameter = typeof(PayrollOpeningBalancesController).GetMethod(nameof(PayrollOpeningBalancesController.Import))!
            .GetParameters().Single(p => p.Name == "file");

        parameter.GetCustomAttribute<FromFormAttribute>().Should().NotBeNull();
    }

    [Fact]
    public async Task Import_ReturnsTheCounts_AndTheWarnings()
    {
        IReadOnlyList<string> warnings = ["E-001 Maria Santos: PAY-2026-007 used these figures; ..."];
        _service.Setup(s => s.ImportAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OpeningBalanceImportResult(3, 2, [], warnings));

        var result = await Controller().Import(AFile(), CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>()
              .Which.Value.Should().Be(new OpeningBalanceImportDto(3, 2, warnings));
    }

    [Fact]
    public async Task Import_PassesTheFilesContentToTheService()
    {
        string? read = null;
        _service.Setup(s => s.ImportAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .Callback((Stream stream, CancellationToken _) => read = new StreamReader(stream).ReadToEnd())
                .ReturnsAsync(new OpeningBalanceImportResult(0, 0, [], []));

        await Controller().Import(AFile("hello,world"), CancellationToken.None);

        read.Should().Be("hello,world");
    }

    [Fact]
    public async Task Import_WithProblems_IsABadRequest_ListingThem()
    {
        _service.Setup(s => s.ImportAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OpeningBalanceImportResult(0, 0,
                    ["Row 2: Unknown employee number E-9.", "Row 3: Enter a year."], []));

        var result = await Controller().Import(AFile(), CancellationToken.None);

        var problem = result.Result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Status.Should().Be(400);
        problem.Title.Should().Be("File not imported");
        problem.Detail.Should().Be("Row 2: Unknown employee number E-9.\nRow 3: Enter a year.");
        problem.Extensions.Should().ContainKey("errors")
               .WhoseValue.Should().BeEquivalentTo(new[] { "Row 2: Unknown employee number E-9.", "Row 3: Enter a year." });
    }

    [Fact]
    public async Task Import_WithoutAFile_IsRefused()
    {
        var result = await Controller().Import(null, CancellationToken.None);

        var problem = result.Result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Detail.Should().Be("Choose a CSV file of at most 2 MB.");
        problem.Extensions["errors"].Should().BeEquivalentTo(new[] { "Choose a CSV file of at most 2 MB." });
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Import_OfAFileOverTwoMegabytes_IsRefused()
    {
        var result = await Controller().Import(AFile(length: 2 * 1024 * 1024 + 1), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>()
              .Which.Value.Should().BeOfType<ProblemDetails>()
              .Which.Detail.Should().Be("Choose a CSV file of at most 2 MB.");
        _service.VerifyNoOtherCalls();
    }
}
