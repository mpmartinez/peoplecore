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

    public DepartmentsTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));

        _api.On(HttpMethod.Get, "/api/companies", HttpStatusCode.OK,
                $$"""[{"id":"{{CompanyId}}","name":"Acme PH"},{"id":"{{Guid.NewGuid()}}","name":"Acme SG"}]""")
            .On(HttpMethod.Get, DepartmentsPath, () => Json(_departments));
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string Paged(params string[] items) =>
        $$"""{"items":[{{string.Join(",", items)}}],"totalCount":{{items.Length}},"page":1,"pageSize":50,"totalPages":1}""";

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
}
