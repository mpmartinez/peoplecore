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

    private static string RunSummary(Guid id, string runNumber, string status, int employees) =>
        $$"""
        {"id":"{{id}}","runNumber":"{{runNumber}}","periodLabel":"Sep 1-15, 2026","periodStart":"2026-09-01",
         "periodEnd":"2026-09-15","payDate":"2026-09-20","frequency":"SemiMonthly","status":"{{status}}",
         "employeeCount":{{employees}},"totalGrossPay":0,"totalNetPay":0,"employeesMissingAttendance":0,
         "createdAt":"2026-09-01T00:00:00Z"}
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
        rows[0].TextContent.Should().Contain("PR-2026-0017").And.Contain("ForApproval").And.Contain("12");
        rows[1].TextContent.Should().Contain("PR-2026-0016").And.Contain("Paid");

        rows[0].Click();

        CurrentUri.Should().Be($"http://localhost/payroll-runs/{runId}");
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
        cut.FindAll("[role=checkbox]").Should().HaveCount(2).And.OnlyContain(c => c.HasAttribute("aria-checked"));
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
            $$"""{"periodStart":"2026-09-01","periodEnd":"2026-09-15","payDate":"2026-09-20","frequency":"SemiMonthly","employees":[{"employeeId":"{{MariaId}}"}]}""");
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

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
}
