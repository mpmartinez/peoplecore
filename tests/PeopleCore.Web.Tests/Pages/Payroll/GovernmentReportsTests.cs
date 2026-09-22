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

    private static string Path1604c(int year) => $"/api/reports/government/1604c?year={year}";

    private static string Report(string report, string warnings = "[]", string rows = """[{"employeeId":"11111111-1111-1111-1111-111111111111","cells":["Cruz, Juan","34-1234567-8","3030.00"],"missingNumber":false}]""") =>
        $$"""
        {"report":"{{report}}","title":"SSS contributions","year":2026,"month":3,"basis":"Pay earned in March 2026",
         "employer":{"name":"Acme","address":null,"tin":"123","rdoCode":"050","agencyNumber":"03-9999999-1"},
         "columns":["Employee","SSS number","Total"],"rows":{{rows}},"totals":["Total","","3030.00"],
         "summary":[],"warnings":{{warnings}},"sections":[]}
        """;

    private static string Section(string title, string rows = """[{"employeeId":"11111111-1111-1111-1111-111111111111","cells":["Cruz, Juan","34-1234567-8","3030.00"],"missingNumber":false}]""") =>
        $$"""
        {"title":"{{title}}","columns":["Employee","TIN","Total"],"rows":{{rows}},"totals":["Total","","3030.00"],"emptyMessage":"No employees in this group."}
        """;

    private static string Report1604c(int year, string sections) =>
        $$"""
        {"report":"1604c","title":"BIR 1604-C alphalist","year":{{year}},"month":0,"basis":"Paid in {{year}}",
         "employer":{"name":"Acme","address":null,"tin":"123","rdoCode":"050","agencyNumber":"03-9999999-1"},
         "columns":[],"rows":[],"totals":[],"summary":[],"warnings":[],"sections":[{{sections}}]}
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
        cut.Find("[data-tab='sss']").GetAttribute("aria-selected").Should().Be("true");

        cut.Find("[data-tab='philhealth']").Click();

        cut.WaitForAssertion(() => _api.Requests.Should().Contain(r => r.RequestUri!.PathAndQuery == Path("philhealth", LastMonth)));
        cut.Find("[data-tab='philhealth']").GetAttribute("aria-selected").Should().Be("true");
        cut.Find("[data-tab='sss']").GetAttribute("aria-selected").Should().Be("false");
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

    [Fact]
    public void TheMonthInput_CannotBeSetPastTheCurrentMonth()
    {
        _api.On(HttpMethod.Get, Path("sss", LastMonth), HttpStatusCode.OK, Report("sss"));

        var cut = Render<GovernmentReports>();

        cut.WaitForElement("[data-report-table]");
        cut.Find("#report-month").GetAttribute("max").Should().Be(DateTime.Today.ToString("yyyy-MM"));
    }

    [Fact]
    public void ChangingTheMonth_RequestsThatMonthsReport()
    {
        var january2026 = new DateTime(2026, 1, 1);
        _api.On(HttpMethod.Get, Path("sss", LastMonth), HttpStatusCode.OK, Report("sss"))
            .On(HttpMethod.Get, Path("sss", january2026), HttpStatusCode.OK, Report("sss"));
        var cut = Render<GovernmentReports>();
        cut.WaitForElement("[data-report-table]");

        cut.Find("#report-month").Change("2026-01");

        cut.WaitForAssertion(() => _api.Requests.Should().Contain(r => r.RequestUri!.PathAndQuery == Path("sss", january2026)));
    }

    [Fact]
    public void FailedCsvDownload_KeepsTheReportVisible_AndShowsTheError()
    {
        _api.On(HttpMethod.Get, Path("sss", LastMonth), HttpStatusCode.OK, Report("sss"))
            .On(HttpMethod.Get, Path("sss", LastMonth) + "&format=csv", HttpStatusCode.BadRequest,
                """{"title":"x","detail":"The CSV could not be built.","status":400}""");
        var cut = Render<GovernmentReports>();
        cut.WaitForElement("[data-report-table]");

        cut.Find("[data-download-csv]").Click();

        cut.WaitForAssertion(() => cut.Find("[data-download-error]").TextContent.Should().Contain("The CSV could not be built."));
        cut.Find("[data-report-table]").TextContent.Should().Contain("Cruz, Juan");
        JSInterop.VerifyNotInvoke("downloadFileFromBytes");
    }

    [Fact]
    public void OpensTheAlphalistTab_ForLastYear_WithNoMonthParameter()
    {
        var lastYear = DateTime.Today.Year - 1;
        var sections = string.Join(",",
            Section("Terminated before December 31"),
            Section("Separated during the year"),
            Section("Still employed on December 31"));
        _api.On(HttpMethod.Get, Path("sss", LastMonth), HttpStatusCode.OK, Report("sss"))
            .On(HttpMethod.Get, Path1604c(lastYear), HttpStatusCode.OK, Report1604c(lastYear, sections));
        var cut = Render<GovernmentReports>();
        cut.WaitForElement("[data-report-table]");

        cut.Find("[data-tab='1604c']").Click();

        cut.WaitForAssertion(() => _api.Requests.Should().Contain(r => r.RequestUri!.PathAndQuery == Path1604c(lastYear)));
        cut.Find("#report-year").GetAttribute("value").Should().Be(lastYear.ToString());
        cut.FindAll("#report-month").Should().BeEmpty();
        var sectionElements = cut.FindAll("[data-report-section]");
        sectionElements.Should().HaveCount(3);
        sectionElements[0].TextContent.Should().Contain("Terminated before December 31");
        sectionElements[1].TextContent.Should().Contain("Separated during the year");
        sectionElements[2].TextContent.Should().Contain("Still employed on December 31");
    }

    [Fact]
    public void ASectionWithNoRows_ShowsItsEmptyMessage()
    {
        var lastYear = DateTime.Today.Year - 1;
        var sections = string.Join(",",
            Section("Terminated before December 31", rows: "[]"),
            Section("Separated during the year"),
            Section("Still employed on December 31"));
        _api.On(HttpMethod.Get, Path1604c(lastYear), HttpStatusCode.OK, Report1604c(lastYear, sections));
        var cut = Render<GovernmentReports>();

        cut.Find("[data-tab='1604c']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-section-empty]").TextContent.Should().Contain("No employees in this group."));
    }

    [Fact]
    public void EveryGroupEmpty_SaysNothingWasPaidInTheYear()
    {
        var lastYear = DateTime.Today.Year - 1;
        var sections = string.Join(",",
            Section("Terminated before December 31", rows: "[]"),
            Section("Separated during the year", rows: "[]"),
            Section("Still employed on December 31", rows: "[]"));
        _api.On(HttpMethod.Get, Path1604c(lastYear), HttpStatusCode.OK, Report1604c(lastYear, sections));
        var cut = Render<GovernmentReports>();

        cut.Find("[data-tab='1604c']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-report-empty]").TextContent.Should().Contain($"No payroll was paid in {lastYear}"));
    }

    [Fact]
    public void DownloadCsv_OnTheAlphalistTab_RequestsTheAnnualCsv()
    {
        var lastYear = DateTime.Today.Year - 1;
        var sections = string.Join(",", Section("Terminated before December 31"), Section("Separated during the year"), Section("Still employed on December 31"));
        _api.On(HttpMethod.Get, Path1604c(lastYear), HttpStatusCode.OK, Report1604c(lastYear, sections))
            .On(HttpMethod.Get, Path1604c(lastYear) + "&format=csv", HttpStatusCode.OK, "Acme\r\n");
        var cut = Render<GovernmentReports>();
        cut.Find("[data-tab='1604c']").Click();
        cut.WaitForElement("[data-report-section]");

        cut.Find("[data-download-csv]").Click();

        cut.WaitForAssertion(() => _api.Requests.Should().Contain(r => r.RequestUri!.PathAndQuery == Path1604c(lastYear) + "&format=csv"));
    }

    [Fact]
    public void ThePageDescription_MentionsTheAnnualAlphalistTooNotJustTheMonthlyReports()
    {
        _api.On(HttpMethod.Get, Path("sss", LastMonth), HttpStatusCode.OK, Report("sss"));

        var cut = Render<GovernmentReports>();

        cut.WaitForElement("[data-report-table]");
        // "BIR 1604-C" alone would also match the tab button's own label - pin the phrase that
        // only the page description would contain.
        cut.Markup.Should().Contain("1604-C alphalist");
    }

    [Fact]
    public void TheAlphalistTab_ExplainsToCollectOrRefund()
    {
        var lastYear = DateTime.Today.Year - 1;
        var sections = string.Join(",",
            Section("Terminated before December 31"), Section("Separated during the year"), Section("Still employed on December 31"));
        _api.On(HttpMethod.Get, Path1604c(lastYear), HttpStatusCode.OK, Report1604c(lastYear, sections));
        var cut = Render<GovernmentReports>();

        cut.Find("[data-tab='1604c']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-alphalist-note]").TextContent.Should().Contain("To collect / (refund)"));
    }

    [Fact]
    public void TheMonthlyTabs_DoNotShowTheAlphalistNote()
    {
        _api.On(HttpMethod.Get, Path("sss", LastMonth), HttpStatusCode.OK, Report("sss"));
        var cut = Render<GovernmentReports>();

        cut.WaitForElement("[data-report-table]");

        cut.FindAll("[data-alphalist-note]").Should().BeEmpty();
    }

    [Fact]
    public void ARequestFromAnOlderApiWithNoSectionsField_IsTreatedAsHavingNoSections()
    {
        // An older API still answering mid-deploy simply omits "sections" from the JSON rather than
        // sending an empty array - System.Text.Json then leaves the record's Sections null.
        var lastYear = DateTime.Today.Year - 1;
        var noSectionsField =
            $$"""
            {"report":"1604c","title":"BIR 1604-C alphalist","year":{{lastYear}},"month":0,"basis":"Paid in {{lastYear}}",
             "employer":{"name":"Acme","address":null,"tin":"123","rdoCode":"050","agencyNumber":"03-9999999-1"},
             "columns":[],"rows":[],"totals":[],"summary":[],"warnings":[]}
            """;
        _api.On(HttpMethod.Get, Path1604c(lastYear), HttpStatusCode.OK, noSectionsField);
        var cut = Render<GovernmentReports>();

        cut.Find("[data-tab='1604c']").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-alphalist-note]").Should().ContainSingle());
        cut.FindAll("[data-report-section]").Should().BeEmpty();
        cut.FindAll("[role=alert]").Should().BeEmpty();
    }
}
