using System.Net;
using System.Text;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Pages.ESS;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;
using static PeopleCore.Web.Tests.Pages.Payroll.PayrollTestData;

namespace PeopleCore.Web.Tests.Pages.Payroll;

public class MyPayslipsTests : BunitContext
{
    private const string ListPath = "/api/reports/my-payslips";

    private static readonly Guid SeptemberRunId = Guid.Parse("5d2c8e61-9f4a-4b37-8c15-a7e3b0d92f48");

    private static readonly string TwoPayslips =
        $$"""
        [{"runId":"{{SeptemberRunId}}","runNumber":"PR-2026-0017","periodLabel":"Sep 1-15, 2026","payDate":"2026-09-20","netPay":0},
         {"runId":"{{Guid.NewGuid()}}","runNumber":"PR-2026-0016","periodLabel":"Aug 16-31, 2026","payDate":"2026-09-05","netPay":0}]
        """;

    private readonly StubHttpHandler _api = new();

    public MyPayslipsTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        JSInterop.SetupVoid("downloadFileFromBytes", _ => true);
    }

    private IRenderedComponent<MyPayslips> RenderPage()
    {
        var cut = Render<MyPayslips>();
        cut.WaitForAssertion(() => cut.FindAll(".animate-spin").Should().BeEmpty());
        return cut;
    }

    [Fact]
    public void Payslips_AreListedWithTheirRunAndPeriod()
    {
        _api.On(HttpMethod.Get, ListPath, HttpStatusCode.OK, TwoPayslips);

        var cut = RenderPage();

        var rows = cut.FindAll("tbody tr");
        rows.Should().HaveCount(2);
        rows[0].TextContent.Should().Contain("PR-2026-0017").And.Contain("Sep 1-15, 2026").And.Contain("2026-09-20");
        rows[1].TextContent.Should().Contain("PR-2026-0016");
    }

    [Fact]
    public void NoPayslips_SaysSo()
    {
        _api.On(HttpMethod.Get, ListPath, HttpStatusCode.OK, "[]");

        var cut = RenderPage();

        cut.Markup.Should().Contain("No payslips found.");
        cut.FindAll("[role=alert]").Should().BeEmpty();
    }

    [Fact]
    public void AnAccountWithNoEmployeeRecord_IsToldWhy_WithoutAPointlessRetry()
    {
        // With an explanation in the body, so the page cannot get away with spotting "403" in the
        // message text: it has to go by the status code.
        _api.On(HttpMethod.Get, ListPath, HttpStatusCode.Forbidden, """{"detail":"No employee record for this user."}""");

        var cut = RenderPage();

        var alert = cut.Find("[role=alert]");
        alert.TextContent.Should().Contain("Account not linked")
            .And.Contain("Your account is not linked to an employee record.");
        cut.FindAll("button").Should().NotContain(b => b.TextContent.Trim() == "Retry");
    }

    [Fact]
    public void ATransientFailure_OffersARetry_ThatLoadsThePayslips()
    {
        var attempts = 0;
        _api.On(HttpMethod.Get, ListPath, () => ++attempts == 1
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(TwoPayslips, Encoding.UTF8, "application/json") });
        var cut = RenderPage();

        cut.Find("[role=alert]").TextContent.Should().Contain("Couldn't load payslips").And.NotContain("not linked");

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Retry").Click();

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Should().HaveCount(2));
        cut.FindAll("[role=alert]").Should().BeEmpty();
    }

    [Fact]
    public void Download_AsksForTheRunOnly_AndSavesThePayslip()
    {
        // The API works out whose payslip it is from the caller's token; a URL carrying an
        // employee id would be an invitation to fetch someone else's.
        _api.On(HttpMethod.Get, ListPath, HttpStatusCode.OK, TwoPayslips)
            .On(HttpMethod.Get, $"/api/reports/my-payslip/{SeptemberRunId}", Pdf);
        var cut = RenderPage();

        cut.FindAll("tbody tr")[0].QuerySelector("button")!.Click();

        cut.WaitForAssertion(() => JSInterop.VerifyInvoke("downloadFileFromBytes").Arguments
            .Should().Equal(PdfBase64, "Payslip-PR-2026-0017.pdf", "application/pdf"));
    }

    [Fact]
    public void AFailedDownload_ShowsAnInlineError_AndKeepsTheList()
    {
        _api.On(HttpMethod.Get, ListPath, HttpStatusCode.OK, TwoPayslips)
            .On(HttpMethod.Get, $"/api/reports/my-payslip/{SeptemberRunId}", HttpStatusCode.InternalServerError);
        var cut = RenderPage();

        cut.FindAll("tbody tr")[0].QuerySelector("button")!.Click();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should()
            .Contain("Failed to download payslip for PR-2026-0017. Please try again."));
        cut.FindAll("tbody tr").Should().HaveCount(2);
        JSInterop.VerifyNotInvoke("downloadFileFromBytes");
    }
}
