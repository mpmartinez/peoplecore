using System.Net;
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

public class PayrollRunsTests : BunitContext
{
    private const string RunsPath = "/api/payroll-runs?page=1&pageSize=50";

    private static readonly Guid MariaId = Guid.Parse("7b0e3c52-5d1f-4a44-9b5e-2f7c1d9a6e10");
    private static readonly Guid JoseId = Guid.Parse("0c6f9a3e-8b2d-4f71-a5c4-3e9d1b7f2a60");
    private static readonly Guid FormerId = Guid.Parse("e4a1d7b9-2c3f-4e58-9a06-5b8c7d1f3e20");

    private readonly StubHttpHandler _api = new();

    public PayrollRunsTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
    }

    private string CurrentUri => Services.GetRequiredService<NavigationManager>().Uri;

    private static string RunSummary(Guid id, string runNumber, string status, int employees, string runType = "Regular",
        bool includesLeaveConversion = false, bool includesThirteenthMonth = false) =>
        $$"""
        {"id":"{{id}}","runNumber":"{{runNumber}}","periodLabel":"Sep 1-15, 2026","periodStart":"2026-09-01",
         "periodEnd":"2026-09-15","payDate":"2026-09-20","frequency":"SemiMonthly","status":"{{status}}",
         "employeeCount":{{employees}},"totalGrossPay":0,"totalNetPay":0,"employeesMissingAttendance":0,
         "createdAt":"2026-09-01T00:00:00Z","runType":"{{runType}}",
         "includesLeaveConversion":{{(includesLeaveConversion ? "true" : "false")}},
         "includesThirteenthMonth":{{(includesThirteenthMonth ? "true" : "false")}}}
        """;

    private static string Runs(params string[] runs) => RunsPage(1, 1, runs);

    private static string RunsPage(int page, int totalPages, params string[] runs) =>
        $$"""{"items":[{{string.Join(",", runs)}}],"totalCount":{{runs.Length}},"page":{{page}},"pageSize":50,"totalPages":{{totalPages}}}""";

    /// <summary>The Pagination bar - the page header has a nav of its own (the breadcrumb).</summary>
    private static IEnumerable<IElement> PaginationBars(IRenderedComponent<PayrollRuns> cut) =>
        cut.FindAll("nav").Where(n => n.TextContent.Contains("Page "));

    private static IElement PageButton(IRenderedComponent<PayrollRuns> cut, int page) =>
        PaginationBars(cut).Single().QuerySelectorAll("button").Single(b => b.TextContent.Trim() == page.ToString());

    private IRenderedComponent<PayrollRuns> RenderPage()
    {
        var cut = Render<PayrollRuns>();
        cut.WaitForAssertion(() => cut.FindAll(".animate-spin").Should().BeEmpty());
        return cut;
    }

    private IRenderedComponent<PayrollRuns> RenderWithCreateDialogOpen()
    {
        _api.On(HttpMethod.Get, RunsPath, HttpStatusCode.OK, Runs());
        var cut = RenderPage();
        Button(cut, "Create Run").Click();
        cut.WaitForAssertion(() => cut.FindAll(".animate-spin").Should().BeEmpty());
        return cut;
    }

    private static IElement Button(IRenderedComponent<PayrollRuns> cut, string text) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == text);

    private static IElement EmployeeCheckbox(IRenderedComponent<PayrollRuns> cut, string name) =>
        cut.FindAll("[role=checkbox]").Single(c => c.ParentElement!.TextContent.Contains(name));

    private void StubTwoActiveEmployees() =>
        _api.On(HttpMethod.Get, "/api/employees?page=1&pageSize=100", HttpStatusCode.OK,
            EmployeePage(1, 1, Employee(MariaId, "EMP-0042", "Maria", "Santos"), Employee(JoseId, "EMP-0043", "Jose", "Reyes")));

    [Fact]
    public void Runs_AreListed_AndARowOpensThatRun()
    {
        var runId = Guid.NewGuid();
        _api.On(HttpMethod.Get, RunsPath, HttpStatusCode.OK,
            Runs(RunSummary(runId, "PR-2026-0017", "ForApproval", 12), RunSummary(Guid.NewGuid(), "PR-2026-0016", "Paid", 11)));

        var cut = RenderPage();

        var rows = cut.FindAll("tbody tr");
        rows.Should().HaveCount(2);
        rows[0].TextContent.Should().Contain("PR-2026-0017").And.Contain("For approval").And.NotContain("ForApproval").And.Contain("12");
        rows[1].TextContent.Should().Contain("PR-2026-0016").And.Contain("Paid");

        rows[0].Click();

        CurrentUri.Should().Be($"http://localhost/payroll-runs/{runId}");
    }

    [Fact]
    public void AFinalPayRun_CarriesAFinalPayBadge_AndARegularOneDoesNot()
    {
        _api.On(HttpMethod.Get, RunsPath, HttpStatusCode.OK,
            Runs(RunSummary(Guid.NewGuid(), "FP-2026-001", "Draft", 1, runType: "FinalPay"),
                 RunSummary(Guid.NewGuid(), "PR-2026-0016", "Paid", 11)));

        var cut = RenderPage();

        var rows = cut.FindAll("tbody tr");
        rows[0].QuerySelector("[data-final-pay-badge]")!.TextContent.Should().Be("Final pay");
        rows[1].QuerySelector("[data-final-pay-badge]").Should().BeNull();
        cut.Markup.Should().NotContain("FinalPay");
    }

    [Fact]
    public void NoRuns_SaysSo()
    {
        _api.On(HttpMethod.Get, RunsPath, HttpStatusCode.OK, Runs());

        var cut = RenderPage();

        cut.Markup.Should().Contain("No payroll runs yet.");
        cut.FindAll("[role=alert]").Should().BeEmpty();
    }

    [Fact]
    public void AFailedLoad_ShowsTheErrorInPlaceOfTheList_NotAnEndlessSpinner()
    {
        _api.On(HttpMethod.Get, RunsPath, HttpStatusCode.InternalServerError);

        var cut = RenderPage();

        cut.Find("[role=alert]").TextContent.Should().Contain("Couldn't load payroll runs")
            .And.Contain("The server ran into a problem (500). Please try again.");
        cut.Markup.Should().NotContain("No payroll runs yet.");
    }

    [Fact]
    public void AFailedLoad_OffersARetry_ThatLoadsTheRuns()
    {
        var attempts = 0;
        _api.On(HttpMethod.Get, RunsPath, () => ++attempts == 1
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : Json(Runs(RunSummary(Guid.NewGuid(), "PR-2026-0017", "Draft", 12))));
        var cut = RenderPage();

        Button(cut, "Retry").Click();

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Should().ContainSingle());
        cut.FindAll("[role=alert]").Should().BeEmpty();
    }

    [Fact]
    public void OlderRuns_AreReachableThroughThePages()
    {
        // Only the newest page comes back at a time; without paging, every run past it could
        // never be opened again from this list.
        _api.On(HttpMethod.Get, RunsPath, HttpStatusCode.OK,
                RunsPage(1, 2, RunSummary(Guid.NewGuid(), "PR-2026-0017", "Draft", 12)))
            .On(HttpMethod.Get, "/api/payroll-runs?page=2&pageSize=50", HttpStatusCode.OK,
                RunsPage(2, 2, RunSummary(Guid.NewGuid(), "PR-2025-0001", "Paid", 9)));
        var cut = RenderPage();

        PageButton(cut, 2).Click();

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Should().ContainSingle()
            .Which.TextContent.Should().Contain("PR-2025-0001"));
        cut.Markup.Should().NotContain("PR-2026-0017").And.Contain("Page 2 of 2");
    }

    [Fact]
    public void ASinglePageOfRuns_OffersNoPaging()
    {
        _api.On(HttpMethod.Get, RunsPath, HttpStatusCode.OK, Runs(RunSummary(Guid.NewGuid(), "PR-2026-0017", "Draft", 12)));

        var cut = RenderPage();

        PaginationBars(cut).Should().BeEmpty();
    }

    [Fact]
    public void AFailedPageChange_ShowsTheError_RatherThanThePreviousPagesRuns_AndRetriesThatPage()
    {
        var page2Attempts = 0;
        _api.On(HttpMethod.Get, RunsPath, HttpStatusCode.OK,
                RunsPage(1, 2, RunSummary(Guid.NewGuid(), "PR-2026-0017", "Draft", 12)))
            .On(HttpMethod.Get, "/api/payroll-runs?page=2&pageSize=50", () => ++page2Attempts == 1
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : Json(RunsPage(2, 2, RunSummary(Guid.NewGuid(), "PR-2025-0001", "Paid", 9))));
        var cut = RenderPage();

        PageButton(cut, 2).Click();

        // Page one's runs left under a failed move to page two would pass for page two's.
        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain("Couldn't load payroll runs"));
        cut.FindAll("tbody tr").Should().BeEmpty();

        Button(cut, "Retry").Click();

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Should().ContainSingle()
            .Which.TextContent.Should().Contain("PR-2025-0001"));
        cut.FindAll("[role=alert]").Should().BeEmpty();
    }

    [Fact]
    public void TheCreateDialog_PagesThroughEveryEmployee_AndPreselectsOnlyTheActiveOnes()
    {
        // Taking page one alone would create a run that silently leaves out everyone past it.
        _api.On(HttpMethod.Get, "/api/employees?page=1&pageSize=100", HttpStatusCode.OK,
                EmployeePage(1, 2, Employee(MariaId, "EMP-0042", "Maria", "Santos"), Employee(FormerId, "EMP-0007", "Former", "Staff", isActive: false)))
            .On(HttpMethod.Get, "/api/employees?page=2&pageSize=100", HttpStatusCode.OK,
                EmployeePage(2, 2, Employee(JoseId, "EMP-0043", "Jose", "Reyes")));

        var cut = RenderWithCreateDialogOpen();

        cut.Markup.Should().Contain("Maria Santos").And.Contain("Jose Reyes").And.NotContain("Former Staff");
        // Blazor renders a true bool attribute as a bare aria-checked and drops a false one entirely.
        // The employee checklist only: the year-end pay boxes are checkboxes too.
        cut.FindAll("[role=checkbox]:not([data-leave-conversion]):not([data-thirteenth-month])").Should().HaveCount(2).And.OnlyContain(c => c.HasAttribute("aria-checked"));
        cut.Markup.Should().Contain("2 selected of 2 active employees");
    }

    [Fact]
    public void AFailedEmployeeLoad_ShowsTheError_WithoutClaimingThereAreNoActiveEmployees()
    {
        _api.On(HttpMethod.Get, "/api/employees?page=1&pageSize=100", HttpStatusCode.InternalServerError);

        var cut = RenderWithCreateDialogOpen();

        cut.Find("[role=alert]").TextContent.Should().Contain("The server ran into a problem (500). Please try again.");
        // Neither "No active employees found." nor "0 selected of 0 active employees": after a
        // failure nobody knows how many there are.
        cut.Markup.Should().NotContain("active employees");
    }

    [Fact]
    public void APeriodEndingBeforeItStarts_IsRefusedWithoutCallingTheApi()
    {
        StubTwoActiveEmployees();
        var cut = RenderWithCreateDialogOpen();

        cut.Find("#periodStart").Input("2026-09-15");
        cut.Find("#periodEnd").Input("2026-09-01");
        Button(cut, "Create").Click();

        cut.Find("[role=alert]").TextContent.Should().Contain("Period end must be on or after period start.");
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public void ARunWithNoEmployees_IsRefusedWithoutCallingTheApi()
    {
        StubTwoActiveEmployees();
        var cut = RenderWithCreateDialogOpen();

        Button(cut, "Clear all").Click();
        Button(cut, "Create").Click();

        cut.Find("[role=alert]").TextContent.Should().Contain("Select at least one employee.");
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public void AValidRun_SendsOnlyTheSelectedEmployees_AndOpensTheNewRun()
    {
        var newRunId = Guid.NewGuid();
        StubTwoActiveEmployees();
        _api.On(HttpMethod.Post, "/api/payroll-runs", HttpStatusCode.Created, $$"""{"id":"{{newRunId}}","runNumber":"PR-2026-0018"}""");
        var cut = RenderWithCreateDialogOpen();

        cut.Find("#periodStart").Input("2026-09-01");
        cut.Find("#periodEnd").Input("2026-09-15");
        cut.Find("#payDate").Input("2026-09-20");
        cut.Find("#frequency").Change("SemiMonthly");
        EmployeeCheckbox(cut, "Jose Reyes").Click();
        Button(cut, "Create").Click();

        cut.WaitForAssertion(() => CurrentUri.Should().Be($"http://localhost/payroll-runs/{newRunId}"));
        // No daysWorked/overtimeHours/holidayDays: a zero sent here would beat the figures the
        // server derives from attendance and unpay overtime and holiday premiums.
        var body = _api.RequestBodies[_api.Requests.FindIndex(r => r.Method == HttpMethod.Post)];
        body.Should().Be(
            $$"""{"periodStart":"2026-09-01","periodEnd":"2026-09-15","payDate":"2026-09-20","frequency":"SemiMonthly","employees":[{"employeeId":"{{MariaId}}","includeThirteenthMonth":false}],"includeLeaveConversion":false}""");
    }

    [Fact]
    public void ARejectedRun_ShowsTheServersReason_AndKeepsTheDialogOpen()
    {
        StubTwoActiveEmployees();
        _api.On(HttpMethod.Post, "/api/payroll-runs", HttpStatusCode.BadRequest,
            """{"detail":"A payroll run already exists for this period."}""");
        var cut = RenderWithCreateDialogOpen();
        var before = CurrentUri;

        Button(cut, "Create").Click();

        cut.WaitForAssertion(() =>
            cut.Find("[role=alert]").TextContent.Should().Contain("A payroll run already exists for this period."));
        CurrentUri.Should().Be(before);
        Button(cut, "Create").HasAttribute("disabled").Should().BeFalse("the user has to be able to fix and retry");
    }

    // ---------- Year-end leave conversion ----------

    [Fact]
    public void AFlaggedRun_CarriesALeaveConversionBadge_AndOthersDoNot()
    {
        _api.On(HttpMethod.Get, RunsPath, HttpStatusCode.OK,
            Runs(RunSummary(Guid.NewGuid(), "PR-2026-0024", "Draft", 12, includesLeaveConversion: true),
                 RunSummary(Guid.NewGuid(), "PR-2026-0023", "Paid", 12)));

        var cut = RenderPage();

        var rows = cut.FindAll("tbody tr");
        rows[0].QuerySelector("[data-leave-conversion-badge]")!.TextContent.Trim().Should().Be("Leave conversion");
        rows[1].QuerySelector("[data-leave-conversion-badge]").Should().BeNull();
    }

    [Fact]
    public void TheConvertUnusedLeaveBox_IsOfferedOnlyWhileThePeriodEndsInDecember()
    {
        StubTwoActiveEmployees();
        var cut = RenderWithCreateDialogOpen();

        cut.Find("#periodStart").Input("2026-09-01");
        cut.Find("#periodEnd").Input("2026-09-15");
        cut.FindAll("[data-leave-conversion]").Should().BeEmpty();

        cut.Find("#periodStart").Input("2026-12-16");
        cut.Find("#periodEnd").Input("2026-12-31");
        var box = cut.Find("[data-leave-conversion]");
        box.ParentElement!.TextContent.Should().Contain("Convert unused leave");
        box.HasAttribute("aria-checked").Should().BeFalse("it starts unticked");

        cut.Find("#periodEnd").Input("2027-01-15");
        cut.FindAll("[data-leave-conversion]").Should().BeEmpty();
    }

    [Fact]
    public void ADecemberRunWithTheBoxTicked_AsksForTheConversion()
    {
        var newRunId = Guid.NewGuid();
        StubTwoActiveEmployees();
        _api.On(HttpMethod.Post, "/api/payroll-runs", HttpStatusCode.Created, $$"""{"id":"{{newRunId}}","runNumber":"PR-2026-0024"}""");
        var cut = RenderWithCreateDialogOpen();

        cut.Find("#periodStart").Input("2026-12-16");
        cut.Find("#periodEnd").Input("2026-12-31");
        cut.Find("#payDate").Input("2027-01-05");
        cut.Find("[data-leave-conversion]").Click();
        Button(cut, "Create").Click();

        cut.WaitForAssertion(() => CurrentUri.Should().Be($"http://localhost/payroll-runs/{newRunId}"));
        var body = System.Text.Json.JsonDocument.Parse(_api.RequestBodies[_api.Requests.FindIndex(r => r.Method == HttpMethod.Post)]!).RootElement;
        body.GetProperty("periodEnd").GetString().Should().Be("2026-12-31");
        body.GetProperty("includeLeaveConversion").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void ATickedBox_IsNotSent_OnceThePeriodEndMovesOutOfDecember()
    {
        // The box is hidden then, so nothing on screen says the run would convert - and the API
        // would refuse it anyway.
        StubTwoActiveEmployees();
        _api.On(HttpMethod.Post, "/api/payroll-runs", HttpStatusCode.Created, $$"""{"id":"{{Guid.NewGuid()}}","runNumber":"PR-2026-0022"}""");
        var cut = RenderWithCreateDialogOpen();

        cut.Find("#periodStart").Input("2026-11-16");
        cut.Find("#periodEnd").Input("2026-12-31");
        cut.Find("[data-leave-conversion]").Click();
        cut.Find("#periodEnd").Input("2026-11-30");
        Button(cut, "Create").Click();

        cut.WaitForAssertion(() => _api.Requests.Should().Contain(r => r.Method == HttpMethod.Post));
        var body = System.Text.Json.JsonDocument.Parse(_api.RequestBodies[_api.Requests.FindIndex(r => r.Method == HttpMethod.Post)]!).RootElement;
        body.GetProperty("includeLeaveConversion").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void ARefusedConversion_ShowsTheApisReason_AndKeepsTheDialogOpen()
    {
        StubTwoActiveEmployees();
        _api.On(HttpMethod.Post, "/api/payroll-runs", HttpStatusCode.BadRequest,
            """{"title":"Business rule violation","status":400,"detail":"Maria Santos's leave for 2026 was already converted in PR-2026-0023."}""");
        var cut = RenderWithCreateDialogOpen();
        var before = CurrentUri;

        cut.Find("#periodStart").Input("2026-12-16");
        cut.Find("#periodEnd").Input("2026-12-31");
        cut.Find("[data-leave-conversion]").Click();
        Button(cut, "Create").Click();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should()
            .Contain("Maria Santos's leave for 2026 was already converted in PR-2026-0023."));
        CurrentUri.Should().Be(before);
        cut.Find("[data-leave-conversion]").HasAttribute("aria-checked").Should().BeTrue("the tick stays for a retry");
    }

    // ---------- The 13th month ----------

    [Fact]
    public void ARunWithThe13thMonth_CarriesA13thMonthBadge_AndOthersDoNot()
    {
        _api.On(HttpMethod.Get, RunsPath, HttpStatusCode.OK,
            Runs(RunSummary(Guid.NewGuid(), "PR-2026-0024", "Draft", 12, includesThirteenthMonth: true),
                 RunSummary(Guid.NewGuid(), "PR-2026-0023", "Paid", 12)));

        var cut = RenderPage();

        var rows = cut.FindAll("tbody tr");
        rows[0].QuerySelector("[data-thirteenth-month-badge]")!.TextContent.Trim().Should().Be("13th month");
        rows[1].QuerySelector("[data-thirteenth-month-badge]").Should().BeNull();
    }

    [Fact]
    public void AFinalPay_CarriesNo13thMonthBadge_SinceItAlwaysIncludesIt()
    {
        _api.On(HttpMethod.Get, RunsPath, HttpStatusCode.OK,
            Runs(RunSummary(Guid.NewGuid(), "FP-2026-001", "Draft", 1, runType: "FinalPay", includesThirteenthMonth: true)));

        var cut = RenderPage();

        cut.FindAll("[data-thirteenth-month-badge]").Should().BeEmpty();
        cut.FindAll("[data-final-pay-badge]").Should().ContainSingle();
    }

    [Theory]
    [InlineData("2026-09-01", "2026-09-15")]
    [InlineData("2026-12-16", "2026-12-31")]
    public void TheInclude13thMonthBox_IsOfferedForEveryPeriod_UnderYearEndPay(string start, string end)
    {
        StubTwoActiveEmployees();
        var cut = RenderWithCreateDialogOpen();

        cut.Find("#periodStart").Input(start);
        cut.Find("#periodEnd").Input(end);

        var group = cut.Find("[data-year-end-pay]");
        group.TextContent.Should().Contain("Year-end pay");
        var box = group.QuerySelector("[data-thirteenth-month]")!;
        box.ParentElement!.TextContent.Should().Contain("Include 13th month");
        box.HasAttribute("aria-checked").Should().BeFalse("it starts unticked");
        // In December, Convert unused leave sits in the same group.
        (group.QuerySelector("[data-leave-conversion]") is not null).Should().Be(end.StartsWith("2026-12"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheInclude13thMonthBox_IsSentOnEveryEmployee(bool ticked)
    {
        StubTwoActiveEmployees();
        _api.On(HttpMethod.Post, "/api/payroll-runs", HttpStatusCode.Created, $$"""{"id":"{{Guid.NewGuid()}}","runNumber":"PR-2026-0021"}""");
        var cut = RenderWithCreateDialogOpen();

        cut.Find("#periodStart").Input("2026-06-01");
        cut.Find("#periodEnd").Input("2026-06-15");
        if (ticked) cut.Find("[data-thirteenth-month]").Click();
        Button(cut, "Create").Click();

        cut.WaitForAssertion(() => _api.Requests.Should().Contain(r => r.Method == HttpMethod.Post));
        var body = System.Text.Json.JsonDocument.Parse(_api.RequestBodies[_api.Requests.FindIndex(r => r.Method == HttpMethod.Post)]!).RootElement;
        var employees = body.GetProperty("employees").EnumerateArray().ToList();
        employees.Select(e => e.GetProperty("employeeId").GetGuid()).Should().BeEquivalentTo([MariaId, JoseId]);
        employees.Should().OnlyContain(e => e.GetProperty("includeThirteenthMonth").GetBoolean() == ticked);
    }

    [Fact]
    public void ADecemberRun_CanIncludeThe13thMonthAndConvertLeaveTogether()
    {
        StubTwoActiveEmployees();
        _api.On(HttpMethod.Post, "/api/payroll-runs", HttpStatusCode.Created, $$"""{"id":"{{Guid.NewGuid()}}","runNumber":"PR-2026-0024"}""");
        var cut = RenderWithCreateDialogOpen();

        cut.Find("#periodStart").Input("2026-12-01");
        cut.Find("#periodEnd").Input("2026-12-15");
        cut.Find("[data-thirteenth-month]").Click();
        cut.Find("[data-leave-conversion]").Click();
        Button(cut, "Create").Click();

        cut.WaitForAssertion(() => _api.Requests.Should().Contain(r => r.Method == HttpMethod.Post));
        var body = System.Text.Json.JsonDocument.Parse(_api.RequestBodies[_api.Requests.FindIndex(r => r.Method == HttpMethod.Post)]!).RootElement;
        body.GetProperty("includeLeaveConversion").GetBoolean().Should().BeTrue();
        body.GetProperty("employees").EnumerateArray().Should().OnlyContain(e => e.GetProperty("includeThirteenthMonth").GetBoolean());
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
}
