using System.Net;
using System.Text;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Pages.Organization;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Pages.Organization;

public class PositionsTests : BunitContext
{
    private const string PositionsPath = "/api/positions?page=1&pageSize=100";
    private const string DepartmentsPath = "/api/departments?page=1&pageSize=100";
    private static readonly Guid FinanceId = Guid.Parse("3f7a9c1e-2b4d-4e6f-8a0b-1c2d3e4f5a6b");
    private static readonly Guid LegalId = Guid.Parse("9e8d7c6b-5a49-4382-a716-f5e4d3c2b1a0");

    private readonly StubHttpHandler _api = new();

    // Read on every request, so a test can change what the API holds after the page has loaded
    // and see whether the page actually asks again.
    private string _positions = Paged();
    private string _departments = Paged(Department(FinanceId, "Finance"), Department(LegalId, "Legal"));

    // When set, the first page of that list fails with this explanation instead.
    private string? _positionsFailure;
    private string? _departmentsFailure;

    public PositionsTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));

        // Every department, not the API's default page of 50: a department missing from the
        // picker is one nobody can add a position to.
        _api.On(HttpMethod.Get, DepartmentsPath, () => _departmentsFailure is null ? Json(_departments) : ServerError(_departmentsFailure))
            .On(HttpMethod.Get, PositionsPath, () => _positionsFailure is null ? Json(_positions) : ServerError(_positionsFailure));
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage ServerError(string detail) =>
        new(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent($$"""{"detail":"{{detail}}"}""", Encoding.UTF8, "application/json")
        };

    private static string Paged(params string[] items) => PageOf(1, 1, items);

    private static string PageOf(int page, int totalPages, params string[] items) =>
        $$"""{"items":[{{string.Join(",", items)}}],"totalCount":{{items.Length}},"page":{{page}},"pageSize":100,"totalPages":{{totalPages}}}""";

    private static string Department(Guid id, string name) =>
        $$"""{"id":"{{id}}","companyId":"{{Guid.NewGuid()}}","parentDepartmentId":null,"parentDepartmentName":null,"name":"{{name}}","code":null,"subDepartmentCount":0}""";

    private static string Position(string title, string department, string? level = null) =>
        $$"""{"id":"{{Guid.NewGuid()}}","departmentId":"{{FinanceId}}","departmentName":"{{department}}","title":"{{title}}","level":{{(level is null ? "null" : $"\"{level}\"")}}}""";

    private IRenderedComponent<Positions> RenderPage()
    {
        var cut = Render<Positions>();
        cut.WaitForAssertion(() => cut.FindAll(".animate-spin").Should().BeEmpty());
        return cut;
    }

    private static IElement DepartmentSelect(IRenderedComponent<Positions> cut) => cut.Find("#newDept");
    private static IElement TitleInput(IRenderedComponent<Positions> cut) => cut.Find("input[placeholder='Position title']");
    private static IElement LevelInput(IRenderedComponent<Positions> cut) => cut.Find("input[placeholder='e.g. Junior, Senior']");
    private static IElement AddButton(IRenderedComponent<Positions> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "+ Add");

    private static List<string[]> Rows(IRenderedComponent<Positions> cut) =>
        cut.FindAll("tbody tr").Select(r => r.QuerySelectorAll("td").Select(td => td.TextContent.Trim()).ToArray()).ToList();

    [Fact]
    public void ListsThePositions_AndOffersEveryDepartmentToAddOneTo()
    {
        _positions = Paged(Position("Accountant", "Finance", "Senior"), Position("Auditor", "Finance"));

        var cut = RenderPage();

        Rows(cut).Should().BeEquivalentTo(
            [new[] { "Accountant", "Finance", "Senior" }, new[] { "Auditor", "Finance", "--" }],
            o => o.WithStrictOrdering());
        DepartmentSelect(cut).QuerySelectorAll("option").Select(o => (o.GetAttribute("value"), o.TextContent))
            .Should().Equal(("", "-- Select --"), (FinanceId.ToString(), "Finance"), (LegalId.ToString(), "Legal"));
    }

    [Fact]
    public void NoPositions_ShowsTheEmptyState()
    {
        var cut = RenderPage();

        cut.Markup.Should().Contain("No positions yet.");
        cut.FindAll("table").Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, "Accountant")]
    [InlineData(true, "")]
    [InlineData(true, "   ")]
    public void AddStaysDisabledUntilThereIsADepartmentAndATitle(bool pickDepartment, string title)
    {
        var cut = RenderPage();

        DepartmentSelect(cut).Change(pickDepartment ? FinanceId.ToString() : "");
        TitleInput(cut).Input(title);

        AddButton(cut).HasAttribute("disabled").Should().BeTrue();
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public void Adding_CreatesThePositionInTheChosenDepartment_ThenResetsTheFormAndReloads()
    {
        _api.On(HttpMethod.Post, "/api/positions", HttpStatusCode.Created, Position("Counsel", "Legal"));
        var cut = RenderPage();

        DepartmentSelect(cut).Change(LegalId.ToString());
        TitleInput(cut).Input("Counsel");
        LevelInput(cut).Input("Senior");
        _positions = Paged(Position("Counsel", "Legal", "Senior"));
        AddButton(cut).Click();

        cut.WaitForAssertion(() => Rows(cut).Should().ContainSingle().Which.Should().Equal("Counsel", "Legal", "Senior"));
        _api.RequestBodies[_api.Requests.FindIndex(r => r.Method == HttpMethod.Post)]
            .Should().Be($$"""{"departmentId":"{{LegalId}}","title":"Counsel","level":"Senior"}""");
        DepartmentSelect(cut).GetAttribute("value").Should().BeEmpty();
        TitleInput(cut).GetAttribute("value").Should().BeEmpty();
        LevelInput(cut).GetAttribute("value").Should().BeEmpty();
    }

    [Fact]
    public void ALevelLeftBlank_IsSentAsNull()
    {
        _api.On(HttpMethod.Post, "/api/positions", HttpStatusCode.Created, Position("Clerk", "Finance"));
        var cut = RenderPage();

        DepartmentSelect(cut).Change(FinanceId.ToString());
        TitleInput(cut).Input("Clerk");
        LevelInput(cut).Input("Junior");
        LevelInput(cut).Input("");
        AddButton(cut).Click();

        cut.WaitForAssertion(() => _api.Requests.Should().Contain(r => r.Method == HttpMethod.Post));
        _api.RequestBodies[_api.Requests.FindIndex(r => r.Method == HttpMethod.Post)]
            .Should().Be($$"""{"departmentId":"{{FinanceId}}","title":"Clerk","level":null}""");
    }

    [Fact]
    public void ARejectedCreate_ShowsTheError_AndKeepsWhatWasEntered()
    {
        _api.On(HttpMethod.Post, "/api/positions", HttpStatusCode.BadRequest, """{"detail":"Title is too long."}""");
        var cut = RenderPage();

        DepartmentSelect(cut).Change(FinanceId.ToString());
        TitleInput(cut).Input("Accountant");
        AddButton(cut).Click();

        cut.WaitForAssertion(() =>
            cut.Find("[role=alert]").TextContent.Should().Contain("Failed to create position. Title is too long."));
        DepartmentSelect(cut).GetAttribute("value").Should().Be(FinanceId.ToString());
        TitleInput(cut).GetAttribute("value").Should().Be("Accountant");
        AddButton(cut).HasAttribute("disabled").Should().BeFalse("the user has to be able to try again");
    }

    [Fact]
    public void ThePicker_OffersDepartmentsFromEveryPage()
    {
        // An organisation with more departments than one page holds must still be able to add a
        // position to the last of them.
        var researchId = Guid.NewGuid();
        _departments = PageOf(1, 2, Department(FinanceId, "Finance"), Department(LegalId, "Legal"));
        _api.On(HttpMethod.Get, "/api/departments?page=2&pageSize=100", () => Json(PageOf(2, 2, Department(researchId, "Research"))));

        var cut = RenderPage();

        cut.WaitForAssertion(() => DepartmentSelect(cut).QuerySelectorAll("option").Select(o => o.GetAttribute("value"))
            .Should().Equal("", FinanceId.ToString(), LegalId.ToString(), researchId.ToString()));
        _api.Requests.Select(r => r.RequestUri!.PathAndQuery).Should().NotContain("/api/departments?page=3&pageSize=100");
    }

    [Fact]
    public void MoreThanOnePageOfPositions_OffersAPager_ThatAsksForTheChosenPage()
    {
        _positions = PageOf(1, 2, Position("Accountant", "Finance"));
        _api.On(HttpMethod.Get, "/api/positions?page=2&pageSize=100", () => Json(PageOf(2, 2, Position("Counsel", "Legal"))));
        var cut = RenderPage();

        cut.Find("nav:not([aria-label=breadcrumb])").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "2").Click();

        cut.WaitForAssertion(() => Rows(cut).Should().ContainSingle().Which[0].Should().Be("Counsel"));
        cut.Find("nav:not([aria-label=breadcrumb])").TextContent.Should().Contain("Page 2 of 2");
    }

    [Fact]
    public void ASinglePageOfPositions_NeedsNoPager()
    {
        _positions = Paged(Position("Accountant", "Finance"));

        var cut = RenderPage();

        cut.FindAll("nav:not([aria-label=breadcrumb])").Should().BeEmpty();
    }

    [Fact]
    public void AFailedLoad_ShowsWhy_InsteadOfSpinningForever()
    {
        _positionsFailure = "Positions are unavailable.";

        var cut = Render<Positions>();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain("Positions are unavailable."));
        cut.FindAll(".animate-spin").Should().BeEmpty();
        cut.FindAll("table").Should().BeEmpty();
        cut.Markup.Should().NotContain("No positions yet.", "a failure is not the same as having none");
    }

    [Fact]
    public void DepartmentsThatFailToLoad_AreExplained_WhileTheListStillShows()
    {
        _departmentsFailure = "Departments are unavailable.";
        _positions = Paged(Position("Accountant", "Finance"));

        var cut = RenderPage();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain("Departments are unavailable."));
        Rows(cut).Should().ContainSingle().Which[0].Should().Be("Accountant");
        TitleInput(cut).Input("Counsel");
        AddButton(cut).HasAttribute("disabled").Should().BeTrue("there is no department to put it in");
    }

    [Fact]
    public void ACreateThatWorked_IsNotReportedAsFailed_WhenOnlyTheReloadAfterItFails()
    {
        // A "failed" create invites a second attempt, which would add the position twice.
        _positions = Paged(Position("Accountant", "Finance"));
        _api.On(HttpMethod.Post, "/api/positions", () =>
        {
            _positionsFailure = "Positions are unavailable.";
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(Position("Counsel", "Legal"), Encoding.UTF8, "application/json")
            };
        });
        var cut = RenderPage();

        DepartmentSelect(cut).Change(LegalId.ToString());
        TitleInput(cut).Input("Counsel");
        AddButton(cut).Click();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain("Positions are unavailable."));
        cut.Markup.Should().NotContain("Failed to create position.");
        cut.FindAll("table").Should().BeEmpty("the rows from before the create are out of date");
    }
}
