using System.Net;
using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
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
         {"key":"approvals.team","group":"Approvals","label":"Approve for my team","description":"Requests from direct reports."},
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

    /// <summary>
    /// A JSON body held back until the test releases it, so a request answered with it finishes
    /// only after whatever the test waits for first - the order a slow roles response and a quick
    /// failure arrive in for real, without leaning on a timer.
    /// </summary>
    private sealed class HeldJsonContent(string json, Task release) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
        {
            await release;
            await stream.WriteAsync(Encoding.UTF8.GetBytes(json));
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }

    private IRenderedComponent<Roles> RenderPage(string roles)
    {
        _api.On(HttpMethod.Get, "/api/roles", HttpStatusCode.OK, roles);
        var cut = Render<Roles>();
        cut.WaitForAssertion(() => cut.FindAll("[data-role-row]").Should().NotBeEmpty());
        return cut;
    }

    // Events are awaited (ClickAsync, not Click): while the renderer is still finishing the render
    // a wait woke on, a plain Click only queues the event, and the next Find can run before it renders.
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
    public async Task ASystemRole_OpensReadOnly()
    {
        var cut = RenderPage(TwoRoles);

        await ButtonIn(cut.Find("[data-role-row=Admin]"), "View").ClickAsync(new MouseEventArgs());

        cut.FindAll("[data-role-editor] [data-permission]").Should().OnlyContain(b => b.HasAttribute("disabled"));
        cut.Find("[data-role-editor]").QuerySelectorAll("button").Should().NotContain(b => b.TextContent.Trim() == "Save Role");
        ButtonIn(cut.Find("[data-role-row=Admin]"), "Delete").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public async Task CreatingARole_SendsItsNameDescriptionAndPermissions_AndReloadsTheList()
    {
        _api.On(HttpMethod.Post, "/api/roles", HttpStatusCode.Created, Role("r2", "Auditor", false, ["analytics.executive"], 0, true));
        var cut = RenderPage(TwoRoles);

        await cut.FindAll("button").Single(b => b.TextContent.Trim() == "New Role").ClickAsync(new MouseEventArgs());
        await cut.Find("#role-name").InputAsync(new ChangeEventArgs { Value = "Auditor" });
        await cut.Find("#role-description").InputAsync(new ChangeEventArgs { Value = "Reads the numbers." });
        await cut.Find("[data-permission='analytics.executive']").ClickAsync(new MouseEventArgs());
        await cut.Find("form[data-role-editor]").SubmitAsync(EventArgs.Empty);

        cut.WaitForAssertion(() => cut.FindAll("form[data-role-editor]").Should().BeEmpty());
        var index = _api.Requests.FindIndex(r => r.Method == HttpMethod.Post);
        var body = JsonDocument.Parse(_api.RequestBodies[index]!).RootElement;
        body.GetProperty("name").GetString().Should().Be("Auditor");
        body.GetProperty("description").GetString().Should().Be("Reads the numbers.");
        body.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).Should().Equal("analytics.executive");
        _api.Requests.Count(r => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath == "/api/roles").Should().Be(2);
    }

    [Fact]
    public async Task APermissionTheCallerLacks_CannotBeTicked()
    {
        _auth.SetClaims(SeededPermissions.ClaimsFor("HRManager"));
        var cut = RenderPage(TwoRoles);

        await cut.FindAll("button").Single(b => b.TextContent.Trim() == "New Role").ClickAsync(new MouseEventArgs());

        cut.Find("[data-permission='analytics.executive']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-permission='recruitment.manage']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public async Task ApprovingForEveryone_LetsTheCallerTickApprovingForATeam_AsTheApiAllows()
    {
        // HR approves for everyone and holds no team-approval permission of its own.
        _auth.SetClaims(SeededPermissions.ClaimsFor("HRManager"));
        var cut = RenderPage(TwoRoles);

        await cut.FindAll("button").Single(b => b.TextContent.Trim() == "New Role").ClickAsync(new MouseEventArgs());

        cut.Find("[data-permission='approvals.team']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public async Task ASaveTheApiRefuses_ShowsItsReason_InTheDialog()
    {
        _api.On(HttpMethod.Put, "/api/roles/r1", HttpStatusCode.BadRequest,
            """{"title":"Role not saved","status":400,"detail":"A role called Payroll already exists."}""");
        var cut = RenderPage(TwoRoles);

        await ButtonIn(cut.Find("[data-role-row=Recruiter]"), "Edit").ClickAsync(new MouseEventArgs());
        await cut.Find("#role-name").InputAsync(new ChangeEventArgs { Value = "Payroll" });
        await cut.Find("form[data-role-editor]").SubmitAsync(EventArgs.Empty);

        cut.WaitForAssertion(() => cut.Find("[data-role-editor-error]").TextContent.Should().Contain("A role called Payroll already exists."));
    }

    [Fact]
    public async Task DeletingARoleStillInUse_AfterConfirming_ShowsTheApisReason()
    {
        _api.On(HttpMethod.Delete, "/api/roles/r1", HttpStatusCode.BadRequest,
            """{"title":"Role not saved","status":400,"detail":"3 accounts still have Recruiter — remove it from them first."}""");
        var cut = RenderPage(TwoRoles);

        await ButtonIn(cut.Find("[data-role-row=Recruiter]"), "Delete").ClickAsync(new MouseEventArgs());
        await cut.FindAll("button").Last(b => b.TextContent.Trim() == "Delete").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => cut.Find("[data-action-error]").TextContent.Should().Contain("3 accounts still have Recruiter"));
    }

    [Fact]
    public async Task WhenThePermissionListFailsToLoad_ButTheRolesLoadAfterIt_TheErrorStays_AndNoRoleCanBeSaved()
    {
        // The catalogue route registered in the constructor would answer first, so start from a fresh stub.
        var api = new StubHttpHandler();
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(api)));
        api.On(HttpMethod.Get, "/api/roles/permissions", HttpStatusCode.InternalServerError,
            """{"title":"Server error","status":500,"detail":"The permission list is unavailable."}""");
        var releaseRoles = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        api.On(HttpMethod.Get, "/api/roles", () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new HeldJsonContent(TwoRoles, releaseRoles.Task) { Headers = { ContentType = new("application/json") } }
        });
        api.On(HttpMethod.Put, "/api/roles/r1", HttpStatusCode.OK, Role("r1", "Recruiter", false, [], 3, true));
        api.On(HttpMethod.Post, "/api/roles", HttpStatusCode.Created, Role("r2", "Blank", false, [], 0, true));

        var cut = Render<Roles>();
        cut.WaitForAssertion(() => cut.Find("[data-catalogue-error]"));
        cut.FindAll("[data-role-row]").Should().BeEmpty("the roles are still held back");

        releaseRoles.SetResult();
        cut.WaitForAssertion(() => cut.FindAll("[data-role-row]").Should().NotBeEmpty());

        cut.Find("[data-catalogue-error]").TextContent.Should().Contain("The permission list is unavailable.");
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "New Role").HasAttribute("disabled").Should().BeTrue();

        var recruiter = cut.Find("[data-role-row=Recruiter]");
        recruiter.QuerySelectorAll("button").Should().NotContain(b => b.TextContent.Trim() == "Edit");
        await ButtonIn(recruiter, "View").ClickAsync(new MouseEventArgs());
        cut.Find("[data-role-editor]").QuerySelectorAll("button").Should().NotContain(b => b.TextContent.Trim() == "Save Role");
        await cut.Find("form[data-role-editor]").SubmitAsync(EventArgs.Empty);

        api.Requests.Should().NotContain(r => r.Method == HttpMethod.Put || r.Method == HttpMethod.Post);
    }
}
