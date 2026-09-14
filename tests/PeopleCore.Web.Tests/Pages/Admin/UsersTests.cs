using System.Net;
using System.Security.Claims;
using System.Text;
using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Pages.Admin;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Pages.Admin;

public class UsersTests : BunitContext
{
    private const string FirstPagePath = "/api/users?page=1&pageSize=20";
    private const string RolesPath = "/api/users/roles";
    private const string CallerId = "11111111-aaaa-4aaa-8aaa-111111111111";
    private const string AnaId = "22222222-bbbb-4bbb-8bbb-222222222222";
    private static readonly Guid CarlaEmployeeId = Guid.Parse("7b0e3c52-5d1f-4a44-9b5e-2f7c1d9a6e10");

    private readonly StubHttpHandler _api = new();

    // Read on every request, so a test can change what the API holds and see the page ask again.
    private string _users = Paged(
        User(CallerId, "admin@company.test", "Site", "Admin", ["Admin"]),
        User(AnaId, "ana@company.test", "Ana", "Reyes", ["Employee", "Manager"], CarlaEmployeeId, "EMP-0042", "Ana Reyes"),
        User("33333333-cccc-4ccc-8ccc-333333333333", "robot@company.test", null, null, []));

    private HttpStatusCode _rolesStatus = HttpStatusCode.OK;

    public UsersTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        var auth = AddAuthorization();
        auth.SetAuthorized("admin@company.test");
        auth.SetRoles("Admin");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, CallerId));

        _api.On(HttpMethod.Get, FirstPagePath, () => Json(HttpStatusCode.OK, _users))
            .On(HttpMethod.Get, RolesPath, () => Json(_rolesStatus, """["Admin","Employee","HRManager","Manager"]"""));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string Paged(params string[] items) =>
        $$"""{"items":[{{string.Join(",", items)}}],"totalCount":{{items.Length}},"page":1,"pageSize":20,"totalPages":1}""";

    private static string Quote(string? value) => value is null ? "null" : $"\"{value}\"";

    private static string User(string id, string email, string? first, string? last, string[] roles,
        Guid? employeeId = null, string? employeeNumber = null, string? employeeName = null) =>
        $$"""
        {"id":"{{id}}","email":"{{email}}","firstName":{{Quote(first)}},"lastName":{{Quote(last)}},
         "roles":[{{string.Join(",", roles.Select(r => $"\"{r}\""))}}],"employeeId":{{Quote(employeeId?.ToString())}},
         "employeeNumber":{{Quote(employeeNumber)}},"employeeName":{{Quote(employeeName)}}}
        """;

    private IRenderedComponent<Users> RenderPage()
    {
        var cut = Render<Users>();
        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Should().NotBeEmpty());
        return cut;
    }

    private static List<string[]> Rows(IRenderedComponent<Users> cut) =>
        cut.FindAll("tbody tr")
            .Select(r => r.QuerySelectorAll("td")
                .Select(td => string.Join(' ', td.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
                .ToArray())
            .ToList();

    private static IElement RowButton(IRenderedComponent<Users> cut, string userId, string text) =>
        cut.Find($"tr[data-user-id='{userId}']").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == text);

    private static IElement ButtonNamed(IRenderedComponent<Users> cut, string text) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == text);

    private static bool IsTicked(IRenderedComponent<Users> cut, string role) =>
        // Blazor writes a true boolean attribute bare and leaves a false one out.
        cut.Find($"[data-role='{role}']").HasAttribute("aria-checked");

    private static IElement ConfirmDeleteButton(IRenderedComponent<Users> cut) =>
        cut.Find("[data-delete-dialog]").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Delete");

    private string? BodyOf(HttpMethod method, string path) =>
        _api.Requests.Select((r, i) => (r, i))
            .Where(x => x.r.Method == method && x.r.RequestUri!.AbsolutePath == path)
            .Select(x => _api.RequestBodies[x.i]).SingleOrDefault();

    private int ListRequests => _api.Requests.Count(r => r.RequestUri!.PathAndQuery == FirstPagePath);

    [Fact]
    public void ListsEveryAccount_WithItsRolesAndLinkedEmployee()
    {
        var cut = RenderPage();

        Rows(cut).Select(r => (r[0], r[2])).Should().Equal(
            ("Site Admin admin@company.test", "Not linked"),
            ("Ana Reyes ana@company.test", "Ana Reyes EMP-0042"),
            ("-- robot@company.test", "Not linked"));
        cut.FindAll("tbody tr").Select(r => string.Join(",", r.QuerySelectorAll("[data-role-badge]").Select(b => b.TextContent.Trim())))
            .Should().Equal("Admin", "Employee,Manager", "");
        cut.FindAll("tbody tr")[2].QuerySelectorAll("td")[1].TextContent.Trim().Should().Be("--");
    }

    [Fact]
    public void TheSignedInAccount_CannotBeDeletedFromItsOwnRow()
    {
        var cut = RenderPage();

        RowButton(cut, CallerId, "Delete").HasAttribute("disabled").Should().BeTrue();
        RowButton(cut, AnaId, "Delete").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Searching_AsksTheApiForMatchingAccounts()
    {
        _api.On(HttpMethod.Get, "/api/users?page=1&pageSize=20&search=ana", HttpStatusCode.OK,
            Paged(User(AnaId, "ana@company.test", "Ana", "Reyes", ["Employee"])));
        var cut = RenderPage();

        cut.Find("input[placeholder='Search by email or name...']").Input("ana");

        cut.WaitForAssertion(() => Rows(cut).Should().ContainSingle().Which[0].Should().Be("Ana Reyes ana@company.test"));
    }

    [Fact]
    public void AddingAUser_SendsTheAccount_WithItsRolesAndEmployee_ThenRefreshesTheList()
    {
        _api.On(HttpMethod.Get, "/api/employees?page=1&pageSize=10&search=carla", HttpStatusCode.OK,
                $$"""
                {"items":[{"id":"{{CarlaEmployeeId}}","employeeNumber":"EMP-0042","firstName":"Carla","lastName":"Santos","fullName":"Carla Santos",
                 "workEmail":"carla@company.test","departmentName":null,"positionTitle":null,"employmentStatus":"Regular","isActive":true}],
                 "totalCount":1,"page":1,"pageSize":10,"totalPages":1}
                """)
            .On(HttpMethod.Post, "/api/users", HttpStatusCode.Created,
                User("44444444-dddd-4ddd-8ddd-444444444444", "carla@company.test", "Carla", "Santos", ["Employee", "Manager"]));
        var cut = RenderPage();

        ButtonNamed(cut, "Add User").Click();
        cut.Find("#user-first-name").Input("Carla");
        cut.Find("#user-last-name").Input("Santos");
        cut.Find("#user-email").Input(" carla@company.test ");
        cut.Find("#user-password").Input("Passw0rd!");
        cut.Find("[data-role='Manager']").Click();
        cut.Find("[data-role='Employee']").Click();
        cut.Find("[data-employee-search]").Input("carla");
        cut.WaitForAssertion(() => cut.Find($"[data-employee-option='{CarlaEmployeeId}']").Click());
        cut.Find("[data-linked-employee]").TextContent.Should().Contain("Carla Santos (EMP-0042)");
        cut.Find("form#user-form").Submit();

        cut.WaitForAssertion(() => cut.Find("[data-users-notice]").TextContent.Should().Contain("Created carla@company.test."));
        BodyOf(HttpMethod.Post, "/api/users").Should().Be(
            $$"""{"email":"carla@company.test","password":"Passw0rd!","firstName":"Carla","lastName":"Santos","roles":["Employee","Manager"],"employeeId":"{{CarlaEmployeeId}}"}""");
        cut.FindAll("form#user-form").Should().BeEmpty("the form closes once the account exists");
        ListRequests.Should().Be(2);
    }

    [Fact]
    public void AddingAUserWithoutAPassword_IsCaughtBeforeCallingTheApi()
    {
        var cut = RenderPage();

        ButtonNamed(cut, "Add User").Click();
        cut.Find("#user-first-name").Input("Carla");
        cut.Find("#user-last-name").Input("Santos");
        cut.Find("#user-email").Input("carla@company.test");
        cut.Find("form#user-form").Submit();

        cut.Find("[data-form-error]").TextContent.Should().Contain("Enter a password.");
        BodyOf(HttpMethod.Post, "/api/users").Should().BeNull();
    }

    [Fact]
    public void ARefusedAdd_ShowsTheApisReason_AndKeepsTheFormOpen()
    {
        _api.On(HttpMethod.Post, "/api/users", HttpStatusCode.Conflict,
            """{"title":"Account not created","detail":"An account with the email ana@company.test already exists.","status":409}""");
        var cut = RenderPage();

        ButtonNamed(cut, "Add User").Click();
        cut.Find("#user-first-name").Input("Ana");
        cut.Find("#user-last-name").Input("Reyes");
        cut.Find("#user-email").Input("ana@company.test");
        cut.Find("#user-password").Input("Passw0rd!");
        cut.Find("form#user-form").Submit();

        cut.WaitForAssertion(() => cut.Find("[data-form-error]").TextContent
            .Should().Contain("An account with the email ana@company.test already exists."));
        cut.Find("#user-email").GetAttribute("value").Should().Be("ana@company.test");
        cut.FindAll("[data-users-notice]").Should().BeEmpty();
    }

    [Fact]
    public void EditingAUser_OpensWithTheirDetails_AndSavesWithoutTouchingThePasswordWhenLeftBlank()
    {
        _api.On(HttpMethod.Put, $"/api/users/{AnaId}", HttpStatusCode.OK,
            User(AnaId, "ana@company.test", "Annie", "Reyes", ["Employee", "HRManager", "Manager"], CarlaEmployeeId, "EMP-0042", "Ana Reyes"));
        var cut = RenderPage();

        RowButton(cut, AnaId, "Edit").Click();

        cut.Find("#user-first-name").GetAttribute("value").Should().Be("Ana");
        cut.Find("#user-email").GetAttribute("value").Should().Be("ana@company.test");
        cut.Find("#user-password").GetAttribute("value").Should().BeNullOrEmpty();
        IsTicked(cut, "Manager").Should().BeTrue();
        IsTicked(cut, "Employee").Should().BeTrue();
        IsTicked(cut, "Admin").Should().BeFalse();
        cut.Find("[data-linked-employee]").TextContent.Should().Contain("Ana Reyes (EMP-0042)");

        cut.Find("#user-first-name").Input("Annie");
        cut.Find("[data-role='HRManager']").Click();
        cut.Find("form#user-form").Submit();

        cut.WaitForAssertion(() => cut.Find("[data-users-notice]").TextContent.Should().Contain("Saved ana@company.test."));
        BodyOf(HttpMethod.Put, $"/api/users/{AnaId}").Should().Be(
            $$"""{"email":"ana@company.test","firstName":"Annie","lastName":"Reyes","roles":["Employee","HRManager","Manager"],"employeeId":"{{CarlaEmployeeId}}","newPassword":null}""");
    }

    [Fact]
    public void EditingAUser_WhenTheRoleListFailedToLoad_KeepsTheRolesTheyHave()
    {
        _rolesStatus = HttpStatusCode.InternalServerError;
        _api.On(HttpMethod.Put, $"/api/users/{AnaId}", HttpStatusCode.OK,
            User(AnaId, "ana@company.test", "Ana", "Reyes", ["Employee", "Manager"]));
        var cut = RenderPage();

        RowButton(cut, AnaId, "Edit").Click();
        cut.Markup.Should().Contain("Roles could not be loaded");
        cut.Find("[data-linked-employee]").QuerySelector("button")!.Click();
        cut.Find("form#user-form").Submit();

        cut.WaitForAssertion(() => BodyOf(HttpMethod.Put, $"/api/users/{AnaId}").Should()
            .Be("""{"email":"ana@company.test","firstName":"Ana","lastName":"Reyes","roles":["Employee","Manager"],"employeeId":null,"newPassword":null}"""));
    }

    [Fact]
    public void DeletingAUser_AsksFirst_ThenDeletesAndRefreshesTheList()
    {
        _api.On(HttpMethod.Delete, $"/api/users/{AnaId}", () =>
        {
            _users = Paged(User(CallerId, "admin@company.test", "Site", "Admin", ["Admin"]));
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        var cut = RenderPage();

        RowButton(cut, AnaId, "Delete").Click();
        cut.Markup.Should().Contain("Delete ana@company.test?");
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Delete, "nothing is deleted until the dialog is confirmed");

        ConfirmDeleteButton(cut).Click();

        cut.WaitForAssertion(() => cut.Find("[data-users-notice]").TextContent.Should().Contain("Deleted ana@company.test."));
        Rows(cut).Should().ContainSingle();
        cut.Markup.Should().NotContain("Delete ana@company.test?");
    }

    [Fact]
    public void CancellingADelete_DeletesNothing()
    {
        var cut = RenderPage();

        RowButton(cut, AnaId, "Delete").Click();
        cut.Find("[data-delete-dialog]").QuerySelectorAll("button").Single(b => b.TextContent.Trim() == "Cancel").Click();

        cut.Markup.Should().NotContain("Delete ana@company.test?");
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Delete);
    }

    [Fact]
    public void ARefusedDelete_ShowsTheApisReason()
    {
        _api.On(HttpMethod.Delete, $"/api/users/{AnaId}", HttpStatusCode.BadRequest,
            """{"title":"Account not deleted","detail":"This is the only Admin account. Give another account the Admin role first.","status":400}""");
        var cut = RenderPage();

        RowButton(cut, AnaId, "Delete").Click();
        ConfirmDeleteButton(cut).Click();

        cut.WaitForAssertion(() => cut.Find("[data-delete-error]").TextContent.Should()
            .Contain("ana@company.test was not deleted. This is the only Admin account."));
        Rows(cut).Should().HaveCount(3);
    }
}
