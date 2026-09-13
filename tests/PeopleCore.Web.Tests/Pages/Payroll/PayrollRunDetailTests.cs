using System.Net;
using System.Text;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Pages.Payroll;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;
using static PeopleCore.Web.Tests.Pages.Payroll.PayrollTestData;

namespace PeopleCore.Web.Tests.Pages.Payroll;

public class PayrollRunDetailTests : BunitContext
{
    private static readonly Guid RunId = Guid.Parse("5d2c8e61-9f4a-4b37-8c15-a7e3b0d92f48");
    private static readonly Guid MariaId = Guid.Parse("7b0e3c52-5d1f-4a44-9b5e-2f7c1d9a6e10");

    private readonly StubHttpHandler _api = new();

    public PayrollRunDetailTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        JSInterop.SetupVoid("downloadFileFromBytes", _ => true);
    }

    private string RunPath => $"/api/payroll-runs/{RunId}";

    private string CurrentUri => Services.GetRequiredService<NavigationManager>().Uri;

    private static string RunJson(string status, bool withEmployee = true, int missingAttendance = 0) =>
        $$"""
        {"id":"{{RunId}}","runNumber":"PR-2026-0017","periodLabel":"Sep 1-15, 2026","periodStart":"2026-09-01",
         "periodEnd":"2026-09-15","payDate":"2026-09-20","frequency":"SemiMonthly","status":"{{status}}",
         "employeeCount":1,"totalGrossPay":0,"totalDeductions":0,"totalNetPay":0,"createdAt":"2026-09-01T00:00:00Z",
         "employeesMissingAttendance":{{missingAttendance}},
         "employees":[{{(withEmployee ? EmployeeLine : "")}}]}
        """;

    private static string EmployeeLine =>
        $$"""{"id":"{{Guid.NewGuid()}}","employeeId":"{{MariaId}}","employeeName":"Maria Santos","employeeNumber":"EMP-0042"}""";

    private IRenderedComponent<PayrollRunDetail> RenderPage()
    {
        var cut = Render<PayrollRunDetail>(p => p.Add(x => x.Id, RunId));
        cut.WaitForAssertion(() => cut.FindAll(".animate-spin").Should().BeEmpty());
        return cut;
    }

    private static IEnumerable<string> ActionButtons(IRenderedComponent<PayrollRunDetail> cut) =>
        cut.FindAll("button").Select(b => b.TextContent.Trim()).Where(t => t is "Compute" or "Approve" or "Mark Paid");

    private static IElement Button(IRenderedComponent<PayrollRunDetail> cut, string text) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == text);

    [Fact]
    public void TheRun_IsLoadedWithItsEmployees_AndWarnsAboutMissingSchedules()
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("ForApproval", missingAttendance: 2));

        var cut = RenderPage();

        cut.Find("h1").TextContent.Should().Be("PR-2026-0017");
        cut.FindAll("tbody tr").Should().ContainSingle().Which.TextContent.Should().Contain("Maria Santos");
        // Without the warning, those employees' pay silently has no absences deducted.
        cut.Find("[role=alert]").TextContent.Should()
            .Contain("2 employees had no shift schedule for this period, so no absences were derived for those employees.");
    }

    [Theory]
    [InlineData("Draft", new[] { "Compute", "Approve" })]
    [InlineData("Processing", new[] { "Compute", "Approve" })]
    [InlineData("ForApproval", new[] { "Compute", "Approve" })]
    [InlineData("Approved", new[] { "Mark Paid" })]
    [InlineData("Paid", new string[0])]
    public void OnlyTheActionsTheServerWillAcceptForTheStatus_AreOffered(string status, string[] expected)
    {
        // Every button outside these sets is a guaranteed DomainException from the API - and
        // recomputing a Paid run would be touching figures that have already retired loans.
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson(status));

        var cut = RenderPage();

        ActionButtons(cut).Should().Equal(expected);
    }

    [Fact]
    public void AFailedLoad_ShowsTheError_AndOffersNoActions()
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.InternalServerError);

        var cut = Render<PayrollRunDetail>(p => p.Add(x => x.Id, RunId));

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain("500"));
        ActionButtons(cut).Should().BeEmpty();
        cut.FindAll("button").Should().NotContain(b => b.TextContent.Contains("Download all"));
    }

    [Theory]
    [InlineData("Draft", "Compute", "compute", "ForApproval", new[] { "Compute", "Approve" })]
    [InlineData("ForApproval", "Approve", "approve", "Approved", new[] { "Mark Paid" })]
    [InlineData("Approved", "Mark Paid", "mark-paid", "Paid", new string[0])]
    public void AnAction_PutsToItsEndpoint_AndReloadsTheRunIntoItsNextStatus(
        string status, string button, string endpoint, string nextStatus, string[] nextActions)
    {
        _api.On(HttpMethod.Get, RunPath, () => Json(RunJson(status)))
            .On(HttpMethod.Put, $"{RunPath}/{endpoint}", () =>
            {
                status = nextStatus;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            });
        var cut = RenderPage();

        Button(cut, button).Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain($">{nextStatus}<"));
        ActionButtons(cut).Should().Equal(nextActions);
        _api.Requests.Where(r => r.Method == HttpMethod.Put).Should().ContainSingle();
        cut.FindAll("[role=alert]").Should().BeEmpty();
    }

    [Fact]
    public void ARejectedAction_ShowsTheServersReason_AndLetsTheUserTryAgain()
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("ForApproval"))
            .On(HttpMethod.Put, $"{RunPath}/approve", HttpStatusCode.BadRequest,
                """{"detail":"Run must be computed before approval."}""");
        var cut = RenderPage();
        var before = CurrentUri;

        Button(cut, "Approve").Click();

        cut.WaitForAssertion(() =>
            cut.Find("[role=alert]").TextContent.Should().Contain("Run must be computed before approval."));
        CurrentUri.Should().Be(before);
        Button(cut, "Approve").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void DownloadingOneEmployeesPayslip_SavesItUnderTheRunAndEmployeeNumber()
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("Paid"))
            .On(HttpMethod.Get, $"/api/reports/payslip/{RunId}/{MariaId}", Pdf);
        var cut = RenderPage();

        cut.Find("button[title='Download payslip for Maria Santos']").Click();

        cut.WaitForAssertion(() => JSInterop.VerifyInvoke("downloadFileFromBytes").Arguments
            .Should().Equal(PdfBase64, "Payslip-PR-2026-0017-EMP-0042.pdf", "application/pdf"));
    }

    [Fact]
    public void DownloadingEveryPayslip_SavesOneFileForTheRun()
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("Paid"))
            .On(HttpMethod.Get, $"/api/reports/payslips/{RunId}", Pdf);
        var cut = RenderPage();

        cut.FindAll("button").Single(b => b.TextContent.Contains("Download all")).Click();

        cut.WaitForAssertion(() => JSInterop.VerifyInvoke("downloadFileFromBytes").Arguments
            .Should().Equal(PdfBase64, "Payslips-PR-2026-0017.pdf", "application/pdf"));
    }

    [Fact]
    public void AFailedPayslipDownload_ShowsAnInlineError_AndSavesNothing()
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("Paid"))
            .On(HttpMethod.Get, $"/api/reports/payslip/{RunId}/{MariaId}", HttpStatusCode.InternalServerError);
        var cut = RenderPage();

        cut.Find("button[title='Download payslip for Maria Santos']").Click();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should()
            .Contain("Failed to download payslip for Maria Santos. Please try again."));
        JSInterop.VerifyNotInvoke("downloadFileFromBytes");
    }

    [Fact]
    public void ARunWithNoEmployees_SaysSo_AndOffersNothingToDownload()
    {
        _api.On(HttpMethod.Get, RunPath, HttpStatusCode.OK, RunJson("Draft", withEmployee: false));

        var cut = RenderPage();

        cut.Markup.Should().Contain("No employees on this run.");
        cut.FindAll("button").Should().NotContain(b => b.TextContent.Contains("Download all"));
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
