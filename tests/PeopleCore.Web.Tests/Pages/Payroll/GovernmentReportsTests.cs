using System.Net;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Pages.Payroll;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Pages.Payroll;

public class GovernmentReportsTests : BunitContext
{
    private readonly StubHttpHandler _api = new();
    private static readonly DateTime LastMonth = DateTime.Today.AddMonths(-1);

    public GovernmentReportsTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        JSInterop.SetupVoid("downloadFileFromBytes", _ => true);
    }

    private static string Path(string report, DateTime month) =>
        $"/api/reports/government/{report}?year={month.Year}&month={month.Month}";

    private static string Report(string report, string warnings = "[]", string rows = """[{"employeeId":"11111111-1111-1111-1111-111111111111","cells":["Cruz, Juan","34-1234567-8","3030.00"],"missingNumber":false}]""") =>
        $$"""
        {"report":"{{report}}","title":"SSS contributions","year":2026,"month":3,"basis":"Pay earned in March 2026",
         "employer":{"name":"Acme","address":null,"tin":"123","rdoCode":"050","agencyNumber":"03-9999999-1"},
         "columns":["Employee","SSS number","Total"],"rows":{{rows}},"totals":["Total","","3030.00"],
         "summary":[],"warnings":{{warnings}}}
        """;

    [Fact]
    public void OpensOnLastMonthsSssList()
    {
        _api.On(HttpMethod.Get, Path("sss", LastMonth), HttpStatusCode.OK, Report("sss"));

        var cut = Render<GovernmentReports>();

        cut.WaitForAssertion(() => cut.Find("[data-report-table]").TextContent.Should().Contain("Cruz, Juan"));
        cut.Find("[data-report-totals]").TextContent.Should().Contain("3030.00");
    }

    [Fact]
    public void SwitchingTab_LoadsThatAgencysReport()
    {
        _api.On(HttpMethod.Get, Path("sss", LastMonth), HttpStatusCode.OK, Report("sss"))
            .On(HttpMethod.Get, Path("philhealth", LastMonth), HttpStatusCode.OK, Report("philhealth"));
        var cut = Render<GovernmentReports>();
        cut.WaitForElement("[data-report-table]");

        cut.Find("[data-tab='philhealth']").Click();

        cut.WaitForAssertion(() => _api.Requests.Should().Contain(r => r.RequestUri!.PathAndQuery == Path("philhealth", LastMonth)));
    }

    [Fact]
    public void ShowsTheWarnings()
    {
        _api.On(HttpMethod.Get, Path("sss", LastMonth), HttpStatusCode.OK,
            Report("sss", warnings: """["1 employee has no SSS number."]"""));

        var cut = Render<GovernmentReports>();

        cut.WaitForAssertion(() => cut.Find("[data-report-warnings]").TextContent.Should().Contain("1 employee has no SSS number."));
    }

    [Fact]
    public void AMonthWithNothingPaid_SaysSo()
    {
        _api.On(HttpMethod.Get, Path("sss", LastMonth), HttpStatusCode.OK, Report("sss", rows: "[]"));

        var cut = Render<GovernmentReports>();

        cut.WaitForAssertion(() => cut.Find("[data-report-empty]").TextContent.Should().Contain("No payroll was paid"));
    }

    [Fact]
    public void DownloadCsv_RequestsTheCsv()
    {
        _api.On(HttpMethod.Get, Path("sss", LastMonth), HttpStatusCode.OK, Report("sss"))
            .On(HttpMethod.Get, Path("sss", LastMonth) + "&format=csv", HttpStatusCode.OK, "Acme\r\n");
        var cut = Render<GovernmentReports>();
        cut.WaitForElement("[data-report-table]");

        cut.Find("[data-download-csv]").Click();

        cut.WaitForAssertion(() => JSInterop.VerifyInvoke("downloadFileFromBytes"));
    }
}
