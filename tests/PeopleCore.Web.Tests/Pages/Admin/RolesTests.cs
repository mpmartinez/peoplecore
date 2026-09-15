using System.Net;
using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Pages.Admin;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Pages.Admin;

public class RolesTests : BunitContext
{
    private readonly StubHttpHandler _api = new();
    private readonly Bunit.TestDoubles.BunitAuthorizationContext _auth;

    private const string Catalogue = """
        [{"key":"recruitment.manage","group":"Recruitment","label":"Manage recruitment","description":"Job postings, applicants and interviews."},
         {"key":"analytics.executive","group":"Analytics","label":"Executive analytics","description":"Workforce summary."}]
        """;

    private static string Role(string id, string name, bool system, string[] permissions, int accounts, bool canEdit) =>
        $$"""{"id":"{{id}}","name":"{{name}}","description":"About {{name}}.","isSystem":{{(system ? "true" : "false")}},"permissions":[{{string.Join(",", permissions.Select(p => $"\"{p}\""))}}],"accountCount":{{accounts}},"canEdit":{{(canEdit ? "true" : "false")}}}""";

    private static readonly string TwoRoles =
        $"[{Role("admin", "Admin", true, ["recruitment.manage", "analytics.executive"], 1, false)},{Role("r1", "Recruiter", false, ["recruitment.manage"], 3, true)}]";

    public RolesTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        _auth = AddAuthorization();
        _auth.SetAuthorized("admin@company.test");
        _auth.SetClaims(SeededPermissions.ClaimsFor("Admin"));
        _api.On(HttpMethod.Get, "/api/roles/permissions", HttpStatusCode.OK, Catalogue);
    }

    private IRenderedComponent<Roles> RenderPage(string roles)
    {
        _api.On(HttpMethod.Get, "/api/roles", HttpStatusCode.OK, roles);
        var cut = Render<Roles>();
        cut.WaitForAssertion(() => cut.FindAll("[data-role-row]").Should().NotBeEmpty());
        return cut;
    }

    private static IElement ButtonIn(IElement scope, string text) =>
        scope.QuerySelectorAll("button").Single(b => b.TextContent.Trim() == text);

    [Fact]
    public void EveryRole_IsListed_WithItsDescription_AccountCount_AndASystemBadge()
    {
        var cut = RenderPage(TwoRoles);

        cut.Find("[data-role-row=Admin]").TextContent.Should().Contain("System").And.Contain("About Admin.");
        cut.Find("[data-role-row=Recruiter]").TextContent.Should().Contain("3").And.NotContain("System");
    }

    [Fact]
    public void ASystemRole_OpensReadOnly()
    {
        var cut = RenderPage(TwoRoles);

        ButtonIn(cut.Find("[data-role-row=Admin]"), "View").Click();

        cut.FindAll("[data-role-editor] [data-permission]").Should().OnlyContain(b => b.HasAttribute("disabled"));
        cut.Find("[data-role-editor]").QuerySelectorAll("button").Should().NotContain(b => b.TextContent.Trim() == "Save Role");
        ButtonIn(cut.Find("[data-role-row=Admin]"), "Delete").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void CreatingARole_SendsItsNameDescriptionAndPermissions_AndReloadsTheList()
    {
        _api.On(HttpMethod.Post, "/api/roles", HttpStatusCode.Created, Role("r2", "Auditor", false, ["analytics.executive"], 0, true));
        var cut = RenderPage(TwoRoles);

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "New Role").Click();
        cut.Find("#role-name").Input("Auditor");
        cut.Find("#role-description").Input("Reads the numbers.");
        cut.Find("[data-permission='analytics.executive']").Click();
        cut.Find("form[data-role-editor]").Submit();

        cut.WaitForAssertion(() => cut.FindAll("form[data-role-editor]").Should().BeEmpty());
        var index = _api.Requests.FindIndex(r => r.Method == HttpMethod.Post);
        var body = JsonDocument.Parse(_api.RequestBodies[index]!).RootElement;
        body.GetProperty("name").GetString().Should().Be("Auditor");
        body.GetProperty("description").GetString().Should().Be("Reads the numbers.");
        body.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).Should().Equal("analytics.executive");
        _api.Requests.Count(r => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath == "/api/roles").Should().Be(2);
    }

    [Fact]
    public void APermissionTheCallerLacks_CannotBeTicked()
    {
        _auth.SetClaims(SeededPermissions.ClaimsFor("HRManager"));
        var cut = RenderPage(TwoRoles);

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "New Role").Click();

        cut.Find("[data-permission='analytics.executive']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-permission='recruitment.manage']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void ASaveTheApiRefuses_ShowsItsReason_InTheDialog()
    {
        _api.On(HttpMethod.Put, "/api/roles/r1", HttpStatusCode.BadRequest,
            """{"title":"Role not saved","status":400,"detail":"A role called Payroll already exists."}""");
        var cut = RenderPage(TwoRoles);

        ButtonIn(cut.Find("[data-role-row=Recruiter]"), "Edit").Click();
        cut.Find("#role-name").Input("Payroll");
        cut.Find("form[data-role-editor]").Submit();

        cut.WaitForAssertion(() => cut.Find("[data-role-editor-error]").TextContent.Should().Contain("A role called Payroll already exists."));
    }

    [Fact]
    public void DeletingARoleStillInUse_AfterConfirming_ShowsTheApisReason()
    {
        _api.On(HttpMethod.Delete, "/api/roles/r1", HttpStatusCode.BadRequest,
            """{"title":"Role not saved","status":400,"detail":"3 accounts still have Recruiter — remove it from them first."}""");
        var cut = RenderPage(TwoRoles);

        ButtonIn(cut.Find("[data-role-row=Recruiter]"), "Delete").Click();
        cut.FindAll("button").Last(b => b.TextContent.Trim() == "Delete").Click();

        cut.WaitForAssertion(() => cut.Find("[data-action-error]").TextContent.Should().Contain("3 accounts still have Recruiter"));
    }
}
