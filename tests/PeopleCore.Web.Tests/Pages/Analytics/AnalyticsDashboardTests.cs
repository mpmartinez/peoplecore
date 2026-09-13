using System.Net;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Pages.Analytics;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Pages.Analytics;

public class AnalyticsDashboardTests : BunitContext
{
    private static readonly string[] HrEndpoints =
    [
        "hr/headcount", "hr/turnover", "hr/attendance", "hr/leave-utilization",
        "hr/overtime", "hr/recruitment-funnel", "hr/performance-distribution"
    ];

    private static readonly string[] ExecutiveEndpoints =
    [
        "executive/workforce-summary", "executive/hiring-trend", "executive/attrition-rate",
        "executive/leave-summary", "executive/performance-overview"
    ];

    private const string Empty = """{"period":{"from":"2025-01-01","to":"2025-12-31"},"data":[],"generatedAt":"2026-01-01T00:00:00Z"}""";

    private readonly StubHttpHandler _api = new();

    // Answers per endpoint, keyed on the endpoint name alone so a test can say what one section
    // returns without restating the date range; anything a test leaves out answers with no data.
    private readonly Dictionary<string, Func<HttpResponseMessage>> _responses = [];

    public AnalyticsDashboardTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
    }

    private static string Data(string rows) =>
        $$"""{"period":{"from":"2025-01-01","to":"2025-12-31"},"data":[{{rows}}],"generatedAt":"2026-01-01T00:00:00Z"}""";

    private void Returns(string endpoint, string json) =>
        _responses[endpoint] = () => Json(HttpStatusCode.OK, json);

    private void Fails(string endpoint) =>
        _responses[endpoint] = () => new HttpResponseMessage(HttpStatusCode.InternalServerError);

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    /// <summary>The URL the client builds for an endpoint over a range, formatted as the API expects.</summary>
    private static string UrlFor(string endpoint, DateOnly from, DateOnly to) => endpoint switch
    {
        // The overview is per review cycle rather than per date range, so it carries no dates.
        "executive/performance-overview" => "/api/analytics/executive/performance-overview",
        "hr/turnover" or "executive/attrition-rate" =>
            $"/api/analytics/{endpoint}?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&groupBy=month",
        _ => $"/api/analytics/{endpoint}?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}"
    };

    private void StubRange(DateOnly from, DateOnly to)
    {
        foreach (var endpoint in HrEndpoints.Concat(ExecutiveEndpoints))
        {
            _api.On(HttpMethod.Get, UrlFor(endpoint, from, to),
                () => _responses.TryGetValue(endpoint, out var respond) ? respond() : Json(HttpStatusCode.OK, Empty));
        }
    }

    private IRenderedComponent<AnalyticsDashboard> RenderAs(string role)
    {
        // The page defaults to the twelve months up to today, so the expected URLs come from the
        // same clock rather than from dates that would stop matching tomorrow.
        StubRange(DefaultFrom, DefaultTo);

        var auth = AddAuthorization();
        auth.SetAuthorized("someone@company.test");
        auth.SetRoles(role);

        var cut = Render<AnalyticsDashboard>();
        WaitForLoad(cut);
        return cut;
    }

    private static DateOnly DefaultFrom => DateOnly.FromDateTime(DateTime.Today.AddMonths(-12));

    private static DateOnly DefaultTo => DateOnly.FromDateTime(DateTime.Today);

    private static void WaitForLoad(IRenderedComponent<AnalyticsDashboard> cut) =>
        cut.WaitForAssertion(() => ApplyButton(cut).HasAttribute("disabled").Should().BeFalse());

    private static IElement ApplyButton(IRenderedComponent<AnalyticsDashboard> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Apply");

    private List<string> RequestedPaths => _api.Requests.Select(r => r.RequestUri!.PathAndQuery).ToList();

    /// <summary>The body rows of the card with the given title, as lists of trimmed cell texts.</summary>
    private static List<List<string>> Rows(IRenderedComponent<AnalyticsDashboard> cut, string cardTitle) =>
        Card(cut, cardTitle).QuerySelectorAll("tbody tr")
            .Select(tr => tr.QuerySelectorAll("td").Select(td => td.TextContent.Trim()).ToList())
            .ToList();

    /// <summary>The card whose title is given: CardTitle's h3 sits in the header, inside the card.</summary>
    private static IElement Card(IRenderedComponent<AnalyticsDashboard> cut, string title) =>
        cut.FindAll("h3").Single(h => h.TextContent.Trim() == title).ParentElement!.ParentElement!;

    [Fact]
    public void AnHrManager_LoadsTheHrSectionsForTheLastTwelveMonths_ButNotTheExecutiveOnes()
    {
        var cut = RenderAs("HRManager");

        RequestedPaths.Should().BeEquivalentTo(HrEndpoints.Select(e => UrlFor(e, DefaultFrom, DefaultTo)));
        cut.Markup.Should().NotContain("Executive Analytics");
        cut.Find("#analytics-from").GetAttribute("value").Should().Be(DefaultFrom.ToString("yyyy-MM-dd"));
        cut.Find("#analytics-to").GetAttribute("value").Should().Be(DefaultTo.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public void AnAdmin_AlsoLoadsAndSeesTheExecutiveSections()
    {
        Returns("executive/workforce-summary",
            Data("""{"totalActive":120,"totalInactive":7,"byDepartment":[{"department":"Finance","active":20,"inactive":2,"total":22}]}"""));

        var cut = RenderAs("Admin");

        RequestedPaths.Should().BeEquivalentTo(
            HrEndpoints.Concat(ExecutiveEndpoints).Select(e => UrlFor(e, DefaultFrom, DefaultTo)));
        cut.Markup.Should().Contain("Executive Analytics");

        // The total is worked out on the page, so it is the figure most likely to drift from the data.
        var kpis = cut.FindAll("div.text-3xl").Select(e => e.TextContent.Trim());
        kpis.Should().Equal("120", "7", "127");
        Rows(cut, "Workforce Summary").Should().ContainSingle().Which.Should().Equal("Finance", "20", "2", "22");
    }

    [Fact]
    public void SectionsShowTheFiguresTheApiReturned()
    {
        Returns("hr/headcount", Data(
            """{"department":"Finance","active":18,"inactive":2,"total":20},{"department":"IT","active":30,"inactive":0,"total":30}"""));
        Returns("hr/attendance", Data("""{"department":"Finance","onTimeRate":92.46,"lateRate":5.5,"absentRate":2.04}"""));
        Returns("hr/turnover", Data("""{"period":"2026-08","newHires":4,"separations":1,"turnoverRate":2.5}"""));

        var cut = RenderAs("HRManager");

        Rows(cut, "Headcount by Department").Should().BeEquivalentTo(
            new[] { new[] { "Finance", "18", "2", "20" }, new[] { "IT", "30", "0", "30" } },
            o => o.WithStrictOrdering());
        Rows(cut, "Attendance Rates").Should().ContainSingle().Which.Should().Equal(
            "Finance", $"{92.5m:F1}%", $"{5.5m:F1}%", $"{2.0m:F1}%");
        Rows(cut, "Turnover").Should().ContainSingle().Which.Should().Equal("2026-08", "4", "1", $"{2.5m:F1}%");
        Card(cut, "Overtime by Department").TextContent.Should().Contain("No data available.");
    }

    [Fact]
    public void ApplyingANewRange_RefetchesEverySectionForThatRange()
    {
        var from = new DateOnly(2025, 1, 1);
        var to = new DateOnly(2025, 6, 30);
        StubRange(from, to);
        var cut = RenderAs("Admin");
        _api.Requests.Clear();

        cut.Find("#analytics-from").Input("2025-01-01");
        cut.Find("#analytics-to").Input("2025-06-30");
        ApplyButton(cut).Click();

        cut.WaitForAssertion(() => _api.Requests.Should().HaveCount(12));
        WaitForLoad(cut);
        RequestedPaths.Should().BeEquivalentTo(
            HrEndpoints.Concat(ExecutiveEndpoints).Select(e => UrlFor(e, from, to)));
    }

    [Fact]
    public void OneFailingSection_DoesNotBlankTheRestOfTheDashboard()
    {
        Fails("hr/headcount");
        Returns("hr/attendance", Data("""{"department":"Finance","onTimeRate":90,"lateRate":6,"absentRate":4}"""));
        Returns("hr/performance-distribution", Data("""{"scoreRange":"4.0-5.0","count":12,"percentage":40}"""));

        var cut = RenderAs("HRManager");

        // Every section is still asked for, and the ones that answered are shown.
        _api.Requests.Should().HaveCount(HrEndpoints.Length);
        Rows(cut, "Attendance Rates").Should().ContainSingle();
        Rows(cut, "Performance Distribution").Should().ContainSingle()
            .Which.Should().Equal("4.0-5.0", "12", $"{40m:F1}%");
        Rows(cut, "Headcount by Department").Should().BeEmpty();
    }

    [Fact]
    public void AFailingSection_SaysItFailed_RatherThanLoadingForever()
    {
        Fails("hr/headcount");
        Fails("hr/turnover");

        var cut = RenderAs("HRManager");

        Card(cut, "Headcount by Department").TextContent.Should().Contain("Couldn't load this section.").And.NotContain("Loading...");
        cut.Find("[role=alert]").TextContent.Should()
            .Contain("Some sections couldn't be loaded").And.Contain("Headcount by Department").And.Contain("Turnover")
            .And.NotContain("Attendance Rates");
    }

    [Fact]
    public void ASectionThatFailsForANewRange_DoesNotKeepShowingTheOldRangesFigures()
    {
        var from = new DateOnly(2025, 1, 1);
        var to = new DateOnly(2025, 6, 30);
        Returns("hr/headcount", Data("""{"department":"Finance","active":18,"inactive":2,"total":20}"""));
        StubRange(from, to);
        var cut = RenderAs("HRManager");
        Rows(cut, "Headcount by Department").Should().ContainSingle();

        Fails("hr/headcount");
        cut.Find("#analytics-from").Input("2025-01-01");
        cut.Find("#analytics-to").Input("2025-06-30");
        ApplyButton(cut).Click();

        cut.WaitForAssertion(() => Card(cut, "Headcount by Department").TextContent.Should().Contain("Couldn't load this section."));
        Rows(cut, "Headcount by Department").Should().BeEmpty("those figures were for the previous range");
    }

    [Fact]
    public void ARangeThatEndsBeforeItStarts_IsRefusedWithoutRefetching()
    {
        var cut = RenderAs("HRManager");
        _api.Requests.Clear();

        cut.Find("#analytics-from").Input("2025-06-30");
        cut.Find("#analytics-to").Input("2025-01-01");
        ApplyButton(cut).Click();

        cut.Find("[role=alert]").TextContent.Should().Contain("The From date must be on or before the To date.");
        _api.Requests.Should().BeEmpty();
    }
}
