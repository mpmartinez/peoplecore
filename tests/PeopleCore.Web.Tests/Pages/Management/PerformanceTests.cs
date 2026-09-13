using System.Net;
using System.Text;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Pages.Management;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Pages.Management;

public class PerformanceTests : BunitContext
{
    private const string CyclesPath = "/api/review-cycles?page=1&pageSize=20";
    private const string ReviewsPath = "/api/performance-reviews?page=1&pageSize=20";

    private static readonly Guid H1CycleId = Guid.Parse("1e7c3a90-4b2d-4f6e-8a15-c9d0b3e2f701");
    private static readonly Guid H2CycleId = Guid.Parse("2f8d4ba1-5c3e-4a7f-9b26-dae1c4f3a802");

    private readonly StubHttpHandler _api = new();

    private string _cyclesJson = Paged();
    private bool _cyclesFail;

    public PerformanceTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        _api.On(HttpMethod.Get, CyclesPath, () => _cyclesFail
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : Json(_cyclesJson));
    }

    private static string Paged(params string[] items) =>
        $$"""{"items":[{{string.Join(",", items)}}],"totalCount":{{items.Length}},"page":1,"pageSize":20,"totalPages":1}""";

    private static string Cycle(Guid id, string name, int? quarter, string status) =>
        $$"""
        {"id":"{{id}}","name":"{{name}}","year":2026,"quarter":{{(quarter?.ToString() ?? "null")}},
         "startDate":"2026-01-01","endDate":"2026-06-30","status":"{{status}}"}
        """;

    private static string Review(string employee, Guid cycleId, string? score, string status) =>
        $$"""
        {"id":"{{Guid.NewGuid()}}","employeeId":"{{Guid.NewGuid()}}","employeeName":"{{employee}}","reviewCycleId":"{{cycleId}}",
         "reviewCycleName":"cycle","finalScore":{{score ?? "null"}},"status":"{{status}}"}
        """;

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private IRenderedComponent<Performance> RenderPage()
    {
        var cut = Render<Performance>();
        cut.WaitForAssertion(() => cut.FindAll(".animate-spin").Should().BeEmpty());
        return cut;
    }

    private static List<string> RowsOf(IElement table) =>
        table.QuerySelectorAll("tbody tr")
            .Select(tr => string.Join("|", tr.QuerySelectorAll("td").Select(td => td.TextContent.Trim())))
            .ToList();

    private static IElement ButtonNamed(IRenderedComponent<Performance> cut, string text) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == text);

    private static IElement CycleRow(IRenderedComponent<Performance> cut, string name) =>
        cut.FindAll("tbody tr").First(tr => tr.TextContent.Contains(name));

    [Fact]
    public void NothingOnFile_ShowsBothEmptyStates()
    {
        _api.On(HttpMethod.Get, ReviewsPath, HttpStatusCode.OK, Paged());

        var cut = RenderPage();

        cut.Markup.Should().Contain("No review cycles.").And.Contain("No reviews found.");
    }

    [Fact]
    public void CyclesAndReviews_AreListed_WithUnscoredReviewsShownAsDashes()
    {
        _cyclesJson = Paged(Cycle(H1CycleId, "Mid-year 2026", quarter: 2, status: "Active"));
        _api.On(HttpMethod.Get, ReviewsPath, HttpStatusCode.OK,
            Paged(Review("Ana Reyes", H1CycleId, "4.25", "Completed"), Review("Juan Cruz", H1CycleId, null, "Draft")));

        var cut = RenderPage();

        var tables = cut.FindAll("table");
        RowsOf(tables[0]).Should().Equal("Mid-year 2026|2026 Q2|Active");
        // Formatted with the same culture the page renders in, so the test is not tied to the machine's locale.
        RowsOf(tables[1]).Should().Equal($"Ana Reyes|{4.25m.ToString("F1")}|Completed", "Juan Cruz|--|Draft");
    }

    [Fact]
    public void ClickingACycle_FiltersReviewsToIt_AndClickingItAgainClearsTheFilter()
    {
        _cyclesJson = Paged(Cycle(H1CycleId, "Mid-year 2026", 2, "Closed"), Cycle(H2CycleId, "Year-end 2026", 4, "Open"));
        _api.On(HttpMethod.Get, ReviewsPath, HttpStatusCode.OK,
                Paged(Review("Ana Reyes", H1CycleId, "4.0", "Completed"), Review("Juan Cruz", H2CycleId, null, "Draft")))
            .On(HttpMethod.Get, $"{ReviewsPath}&cycleId={H2CycleId}", HttpStatusCode.OK,
                Paged(Review("Juan Cruz", H2CycleId, null, "Draft")));
        var cut = RenderPage();

        CycleRow(cut, "Year-end 2026").Click();

        cut.WaitForAssertion(() => RowsOf(cut.FindAll("table")[1]).Should().Equal("Juan Cruz|--|Draft"));
        cut.Markup.Should().Contain("(filtered by cycle)");
        _api.Requests.Last().RequestUri!.PathAndQuery.Should().Be($"{ReviewsPath}&cycleId={H2CycleId}");

        CycleRow(cut, "Year-end 2026").Click();

        cut.WaitForAssertion(() => RowsOf(cut.FindAll("table")[1]).Should().HaveCount(2));
        cut.Markup.Should().NotContain("(filtered by cycle)");
        _api.Requests.Last().RequestUri!.PathAndQuery.Should().Be(ReviewsPath);
    }

    [Fact]
    public void SavingANewCycle_PostsTheForm_ClosesIt_AndReloadsTheCycles()
    {
        _api.On(HttpMethod.Get, ReviewsPath, HttpStatusCode.OK, Paged())
            .On(HttpMethod.Post, "/api/review-cycles", () =>
            {
                var created = Cycle(H2CycleId, "Year-end 2026", null, "Draft");
                _cyclesJson = Paged(created);
                return Json(created, HttpStatusCode.Created);
            });
        var cut = RenderPage();

        ButtonNamed(cut, "+ New Cycle").Click();
        cut.Find("input[placeholder='e.g. Q1 2026']").Input("Year-end 2026");
        cut.Find("input[type=number]").Input("2027");
        cut.Find("#cycle-start").Input("2026-07-01");
        cut.Find("#cycle-end").Input("2026-12-31");
        ButtonNamed(cut, "Save").Click();

        cut.WaitForAssertion(() => RowsOf(cut.FindAll("table")[0]).Should().Equal("Year-end 2026|2026|Draft"));
        var body = _api.RequestBodies[_api.Requests.FindIndex(r => r.Method == HttpMethod.Post)];
        body.Should().Be("""{"name":"Year-end 2026","year":2027,"quarter":null,"startDate":"2026-07-01","endDate":"2026-12-31"}""");
        cut.FindAll("#cycle-start").Should().BeEmpty("the form closes once the cycle exists");
        _api.Requests.Count(r => r.RequestUri!.PathAndQuery == CyclesPath).Should().Be(2);
    }

    [Fact]
    public void AFailedSave_ShowsTheError_AndKeepsWhatWasTyped()
    {
        _api.On(HttpMethod.Get, ReviewsPath, HttpStatusCode.OK, Paged())
            .On(HttpMethod.Post, "/api/review-cycles", HttpStatusCode.InternalServerError);
        var cut = RenderPage();

        ButtonNamed(cut, "+ New Cycle").Click();
        cut.Find("input[placeholder='e.g. Q1 2026']").Input("Year-end 2026");
        ButtonNamed(cut, "Save").Click();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain("Failed to create cycle."));
        cut.Find("input[placeholder='e.g. Q1 2026']").GetAttribute("value").Should().Be("Year-end 2026");
        ButtonNamed(cut, "Save").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void CyclesThatFailToLoad_AreReported_InsteadOfCrashingThePage()
    {
        _cyclesFail = true;
        _api.On(HttpMethod.Get, ReviewsPath, HttpStatusCode.OK, Paged());

        var cut = Render<Performance>();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain("Couldn't load review cycles"));
        cut.FindAll(".animate-spin").Should().BeEmpty();
        cut.Markup.Should().Contain("No reviews found.", "the reviews still loaded");
    }

    [Fact]
    public void AFilteredReloadThatFails_DoesNotLeaveTheOtherCyclesReviewsOnScreen()
    {
        _cyclesJson = Paged(Cycle(H2CycleId, "Year-end 2026", 4, "Open"));
        _api.On(HttpMethod.Get, ReviewsPath, HttpStatusCode.OK, Paged(Review("Ana Reyes", H1CycleId, "4.0", "Completed")))
            .On(HttpMethod.Get, $"{ReviewsPath}&cycleId={H2CycleId}", HttpStatusCode.InternalServerError);
        var cut = RenderPage();

        CycleRow(cut, "Year-end 2026").Click();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain("Couldn't load reviews"));
        cut.Markup.Should().NotContain("Ana Reyes");
    }

    [Theory]
    [InlineData("", "2026-07-01", "2026-12-31", "Please enter a name for the cycle.")]
    [InlineData("   ", "2026-07-01", "2026-12-31", "Please enter a name for the cycle.")]
    [InlineData("Year-end 2026", "2026-12-31", "2026-07-01", "The end date cannot be before the start date.")]
    public void AnInvalidCycle_IsRefusedWithoutCallingTheApi(string name, string start, string end, string expectedError)
    {
        _api.On(HttpMethod.Get, ReviewsPath, HttpStatusCode.OK, Paged());
        var cut = RenderPage();

        ButtonNamed(cut, "+ New Cycle").Click();
        cut.Find("#cycle-name").Input(name);
        cut.Find("#cycle-start").Input(start);
        cut.Find("#cycle-end").Input(end);
        ButtonNamed(cut, "Save").Click();

        cut.Find("[role=alert]").TextContent.Should().Contain(expectedError);
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public void TheNameAndYearLabels_PointAtTheirInputs()
    {
        // FormField renders <label for=Id>; an input without that id leaves the label unattached,
        // so clicking it does nothing and a screen reader announces an unnamed field.
        _api.On(HttpMethod.Get, ReviewsPath, HttpStatusCode.OK, Paged());
        var cut = RenderPage();

        ButtonNamed(cut, "+ New Cycle").Click();

        cut.FindAll("label[for=cycle-name]").Should().ContainSingle();
        cut.FindAll("input#cycle-name").Should().ContainSingle();
        cut.FindAll("label[for=cycle-year]").Should().ContainSingle();
        cut.FindAll("input#cycle-year").Should().ContainSingle();
    }
}
