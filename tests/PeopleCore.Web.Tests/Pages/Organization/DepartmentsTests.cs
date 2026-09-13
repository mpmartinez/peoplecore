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

public class DepartmentsTests : BunitContext
{
    private const string DepartmentsPath = "/api/departments?page=1&pageSize=50";
    private static readonly Guid CompanyId = Guid.Parse("0c1d2e3f-4a5b-4c6d-8e7f-901a2b3c4d5e");

    private readonly StubHttpHandler _api = new();

    // Read on every request, so a test can change what the API holds after the page has loaded
    // and see whether the page actually asks again.
    private string _departments = Paged();
    private string _companies = $$"""[{"id":"{{CompanyId}}","name":"Acme PH"},{"id":"{{Guid.NewGuid()}}","name":"Acme SG"}]""";

    // When set, the first page of departments fails with this explanation instead.
    private string? _departmentsFailure;

    public DepartmentsTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));

        _api.On(HttpMethod.Get, "/api/companies", () => Json(_companies))
            .On(HttpMethod.Get, DepartmentsPath, () => _departmentsFailure is null ? Json(_departments) : ServerError(_departmentsFailure));
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
        $$"""{"items":[{{string.Join(",", items)}}],"totalCount":{{items.Length}},"page":{{page}},"pageSize":50,"totalPages":{{totalPages}}}""";

    private static string Department(string name, string? code = null, string? parent = null, int subDepartments = 0) =>
        $$"""
        {"id":"{{Guid.NewGuid()}}","companyId":"{{CompanyId}}","parentDepartmentId":null,
         "parentDepartmentName":{{(parent is null ? "null" : $"\"{parent}\"")}},"name":"{{name}}",
         "code":{{(code is null ? "null" : $"\"{code}\"")}},"subDepartmentCount":{{subDepartments}}}
        """;

    private IRenderedComponent<Departments> RenderPage()
    {
        var cut = Render<Departments>();
        cut.WaitForAssertion(() => cut.FindAll(".animate-spin").Should().BeEmpty());
        return cut;
    }

    private static IElement NameInput(IRenderedComponent<Departments> cut) => cut.Find("input[placeholder='Department name']");
    private static IElement CodeInput(IRenderedComponent<Departments> cut) => cut.Find("input[placeholder='e.g. HR, FIN']");
    private static IElement AddButton(IRenderedComponent<Departments> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "+ Add");

    private static List<string[]> Rows(IRenderedComponent<Departments> cut) =>
        cut.FindAll("tbody tr").Select(r => r.QuerySelectorAll("td").Select(td => td.TextContent.Trim()).ToArray()).ToList();

    [Fact]
    public void ListsTheDepartments_WithDashesWhereThereIsNoCodeOrParent()
    {
        _departments = Paged(Department("Finance", "FIN", subDepartments: 2), Department("Payroll", parent: "Finance"));

        var cut = RenderPage();

        Rows(cut).Should().BeEquivalentTo(
            [new[] { "Finance", "FIN", "--", "2" }, new[] { "Payroll", "--", "Finance", "0" }],
            o => o.WithStrictOrdering());
    }

    [Fact]
    public void NoDepartments_ShowsTheEmptyState()
    {
        var cut = RenderPage();

        cut.Markup.Should().Contain("No departments yet.");
        cut.FindAll("table").Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddStaysDisabledUntilThereIsAName(string name)
    {
        var cut = RenderPage();

        NameInput(cut).Input(name);
        CodeInput(cut).Input("FIN");

        AddButton(cut).HasAttribute("disabled").Should().BeTrue();
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public void Adding_CreatesTheDepartmentUnderTheFirstCompany_ThenClearsTheFormAndReloads()
    {
        _api.On(HttpMethod.Post, "/api/departments", HttpStatusCode.Created, Department("Finance", "FIN"));
        var cut = RenderPage();

        NameInput(cut).Input("Finance");
        CodeInput(cut).Input("FIN");
        _departments = Paged(Department("Finance", "FIN"));
        AddButton(cut).Click();

        cut.WaitForAssertion(() => Rows(cut).Should().ContainSingle().Which[0].Should().Be("Finance"));
        var body = _api.RequestBodies[_api.Requests.FindIndex(r => r.Method == HttpMethod.Post)];
        body.Should().Be($$"""{"companyId":"{{CompanyId}}","name":"Finance","code":"FIN","parentDepartmentId":null}""");
        NameInput(cut).GetAttribute("value").Should().BeEmpty();
        CodeInput(cut).GetAttribute("value").Should().BeEmpty();
    }

    [Fact]
    public void AnErasedCode_IsSentAsNull_NotAsAnEmptyCode()
    {
        // An empty string is still a code as far as a uniqueness check is concerned; a second
        // department without one would collide with the first.
        _api.On(HttpMethod.Post, "/api/departments", HttpStatusCode.Created, Department("Legal"));
        var cut = RenderPage();

        NameInput(cut).Input("Legal");
        CodeInput(cut).Input("LG");
        CodeInput(cut).Input("");
        AddButton(cut).Click();

        cut.WaitForAssertion(() => _api.Requests.Should().Contain(r => r.Method == HttpMethod.Post));
        _api.RequestBodies[_api.Requests.FindIndex(r => r.Method == HttpMethod.Post)]
            .Should().Contain("\"code\":null");
    }

    [Fact]
    public void ARejectedCreate_ShowsTheError_AndKeepsWhatWasTyped()
    {
        _api.On(HttpMethod.Post, "/api/departments", HttpStatusCode.Conflict);
        var cut = RenderPage();

        NameInput(cut).Input("Finance");
        CodeInput(cut).Input("FIN");
        AddButton(cut).Click();

        cut.WaitForAssertion(() =>
            cut.Find("[role=alert]").TextContent.Should().Contain("Failed to create department."));
        NameInput(cut).GetAttribute("value").Should().Be("Finance");
        CodeInput(cut).GetAttribute("value").Should().Be("FIN");
        AddButton(cut).HasAttribute("disabled").Should().BeFalse("the user has to be able to try again");
    }

    [Fact]
    public void WithNoCompany_AddExplainsWhyItCannotWork_InsteadOfDoingNothing()
    {
        // Every department belongs to a company. With none set up, an enabled Add that silently
        // ignores the click leaves the user guessing.
        _companies = "[]";
        var cut = RenderPage();

        NameInput(cut).Input("Finance");

        cut.Find("[role=alert]").TextContent.Should().Contain("no company has been set up");
        AddButton(cut).HasAttribute("disabled").Should().BeTrue();
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public void CompaniesThatFailToLoad_AreExplained_AndAddStaysOff_WhileTheListStillShows()
    {
        // A separate API: the stub answers with the first matching route, so the constructor's
        // working companies route cannot be overridden.
        var api = new StubHttpHandler()
            .On(HttpMethod.Get, "/api/companies", () => ServerError("Companies are unavailable."))
            .On(HttpMethod.Get, DepartmentsPath, () => Json(Paged(Department("Finance", "FIN"))));
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(api)));

        var cut = RenderPage();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain("Companies are unavailable."));
        Rows(cut).Should().ContainSingle().Which[0].Should().Be("Finance");
        NameInput(cut).Input("Legal");
        AddButton(cut).HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void AFailedLoad_ShowsWhy_InsteadOfSpinningForever()
    {
        _departmentsFailure = "Departments are unavailable.";

        var cut = Render<Departments>();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain("Departments are unavailable."));
        cut.FindAll(".animate-spin").Should().BeEmpty();
        cut.FindAll("table").Should().BeEmpty();
        cut.Markup.Should().NotContain("No departments yet.", "a failure is not the same as having none");
    }

    [Fact]
    public void ACreateThatWorked_IsNotReportedAsFailed_WhenOnlyTheReloadAfterItFails()
    {
        // A "failed" create invites a second attempt, which would add the department twice.
        _departments = Paged(Department("Legal"));
        _api.On(HttpMethod.Post, "/api/departments", () =>
        {
            _departmentsFailure = "Departments are unavailable.";
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(Department("Finance", "FIN"), Encoding.UTF8, "application/json")
            };
        });
        var cut = RenderPage();

        NameInput(cut).Input("Finance");
        AddButton(cut).Click();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.Should().Contain("Departments are unavailable."));
        cut.Markup.Should().NotContain("Failed to create department.");
        cut.FindAll("table").Should().BeEmpty("the rows from before the create are out of date");
    }

    [Fact]
    public void MoreThanOnePage_OffersAPager_ThatAsksForTheChosenPage()
    {
        _departments = PageOf(1, 2, Department("Finance"));
        _api.On(HttpMethod.Get, "/api/departments?page=2&pageSize=50", () => Json(PageOf(2, 2, Department("Legal"))));
        var cut = RenderPage();

        cut.Find("nav:not([aria-label=breadcrumb])").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "2").Click();

        cut.WaitForAssertion(() => Rows(cut).Should().ContainSingle().Which[0].Should().Be("Legal"));
        cut.Find("nav:not([aria-label=breadcrumb])").TextContent.Should().Contain("Page 2 of 2");
    }

    [Fact]
    public void ASinglePage_NeedsNoPager()
    {
        _departments = Paged(Department("Finance"));

        var cut = RenderPage();

        cut.FindAll("nav:not([aria-label=breadcrumb])").Should().BeEmpty();
    }
}
