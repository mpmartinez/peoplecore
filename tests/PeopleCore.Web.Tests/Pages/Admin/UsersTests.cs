using System.Net;
using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Pages.Admin;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Pages.Admin;

public class UsersTests : BunitContext
{
    private const string FirstPage = "/api/users?page=1&pageSize=20";
    private static readonly Guid MariaId = Guid.Parse("7b0e3c52-5d1f-4a44-9b5e-2f7c1d9a6e10");

    private readonly StubHttpHandler _api = new();

    public UsersTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        var auth = AddAuthorization();
        auth.SetAuthorized("hr@company.test");
        auth.SetRoles("HRManager");
        _api.On(HttpMethod.Get, "/api/users/assignable-roles", HttpStatusCode.OK, """["Manager","Employee","PayrollService"]""");
    }

    private static string Json(bool value) => value ? "true" : "false";

    private static string Account(string id, string[] roles, bool canManage = true, bool active = true, bool mustChange = false, string? employeeName = null) =>
        $$"""
        {"id":"{{id}}","email":"{{id}}@company.test","firstName":"Ana","lastName":"Reyes",
         "roles":[{{string.Join(",", roles.Select(r => $"\"{r}\""))}}],"isActive":{{Json(active)}},"mustChangePassword":{{Json(mustChange)}},
         "employeeId":null,"employeeName":{{(employeeName is null ? "null" : $"\"{employeeName}\"")}},"canManage":{{Json(canManage)}}}
        """;

    private static string Paged(params string[] items) =>
        $$"""{"items":[{{string.Join(",", items)}}],"totalCount":{{items.Length}},"page":1,"pageSize":20,"totalPages":1}""";

    private IRenderedComponent<Users> RenderPage(string firstPage)
    {
        _api.On(HttpMethod.Get, FirstPage, HttpStatusCode.OK, firstPage);
        var cut = Render<Users>();
        cut.WaitForAssertion(() => cut.FindAll(".animate-spin").Should().BeEmpty());
        return cut;
    }

    private static IElement RowFor(IRenderedComponent<Users> cut, string email) =>
        cut.FindAll("tbody tr").Single(r => r.TextContent.Contains(email));

    private static IElement ButtonIn(IElement scope, string text) =>
        scope.QuerySelectorAll("button").Single(b => b.TextContent.Trim() == text);

    private JsonElement BodyOf(HttpMethod method, string path)
    {
        var index = _api.Requests.FindIndex(r => r.Method == method && r.RequestUri!.AbsolutePath == path);
        index.Should().BeGreaterThanOrEqualTo(0, $"{method} {path} should have been sent");
        return JsonDocument.Parse(_api.RequestBodies[index]!).RootElement;
    }

    private static IEnumerable<string?> Strings(JsonElement array) => array.EnumerateArray().Select(e => e.GetString());

    [Fact]
    public void EveryAccount_IsListedWithItsRolesEmployeeAndStatus()
    {
        var cut = RenderPage(Paged(
            Account("u1", ["Employee", "PayrollService"], employeeName: "Maria Santos"),
            Account("u2", ["Employee"], active: false),
            Account("u3", ["Employee"], mustChange: true)));

        RowFor(cut, "u1@company.test").TextContent.Should().Contain("Payroll").And.Contain("Maria Santos").And.Contain("Active");
        RowFor(cut, "u2@company.test").TextContent.Should().Contain("Deactivated");
        RowFor(cut, "u3@company.test").TextContent.Should().Contain("Must change password");
    }

    [Fact]
    public void AnAccountTheCallerMayNotChange_HasEveryActionDisabled_AndSaysWhy()
    {
        var cut = RenderPage(Paged(Account("u1", ["Admin", "Employee"], canManage: false)));

        var row = RowFor(cut, "u1@company.test");
        row.QuerySelectorAll("button").Should().OnlyContain(b => b.HasAttribute("disabled"));
        ButtonIn(row, "Roles").GetAttribute("title").Should().Be("Only an administrator can change an Admin or HR Manager account.");
    }

    [Fact]
    public void ASearchInTheAddress_IsAppliedOnArrival()
    {
        _api.On(HttpMethod.Get, "/api/users?page=1&pageSize=20&search=maria%40company.test", HttpStatusCode.OK, Paged(Account("maria", ["Employee"])));

        // bUnit requires a [SupplyParameterFromQuery] value to arrive via the address, not Add(...).
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(nav.GetUriWithQueryParameter("Search", "maria@company.test"));
        var cut = Render<Users>();

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Should().ContainSingle());
        cut.Find("#account-search").GetAttribute("value").Should().Be("maria@company.test");
    }

    [Fact]
    public void EditingRoles_SendsTheTickedRoles_AlwaysWithEmployee()
    {
        _api.On(HttpMethod.Put, "/api/users/u1/roles", HttpStatusCode.OK, Account("u1", ["Employee", "Manager"]));
        var cut = RenderPage(Paged(Account("u1", ["Employee"])));

        ButtonIn(RowFor(cut, "u1@company.test"), "Roles").Click();
        cut.Find("[data-roles-dialog] [data-role=Employee]").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-roles-dialog] [data-role=Manager]").Click();
        ButtonIn(cut.Find("[data-roles-dialog]"), "Save Roles").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-roles-dialog]").Should().BeEmpty());
        Strings(BodyOf(HttpMethod.Put, "/api/users/u1/roles").GetProperty("roles")).Should().BeEquivalentTo("Employee", "Manager");
    }

    [Fact]
    public void LinkingAnEmployee_FindsThemBySearch_AndSendsTheirId()
    {
        _api.On(HttpMethod.Get, "/api/employees?page=1&pageSize=10&search=maria", HttpStatusCode.OK,
            $$"""
            {"items":[{"id":"{{MariaId}}","employeeNumber":"EMP-0007","firstName":"Maria","lastName":"Santos","fullName":"Maria Santos",
              "workEmail":"maria@company.test","departmentName":null,"positionTitle":null,"employmentStatus":"Regular","isActive":true}],
             "totalCount":1,"page":1,"pageSize":10,"totalPages":1}
            """);
        _api.On(HttpMethod.Put, "/api/users/u1/employee", HttpStatusCode.OK, Account("u1", ["Employee"], employeeName: "Maria Santos"));
        var cut = RenderPage(Paged(Account("u1", ["Employee"])));

        ButtonIn(RowFor(cut, "u1@company.test"), "Employee").Click();
        cut.Find("#link-employee").Input("maria");
        cut.WaitForAssertion(() => cut.FindAll("[data-employee-option]").Should().ContainSingle());
        cut.Find("[data-employee-option]").Click();
        ButtonIn(cut.Find("[data-link-dialog]"), "Link Employee").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-link-dialog]").Should().BeEmpty());
        BodyOf(HttpMethod.Put, "/api/users/u1/employee").GetProperty("employeeId").GetGuid().Should().Be(MariaId);
    }

    [Fact]
    public void ResettingAPassword_AfterConfirming_ShowsTheNewPasswordOnce()
    {
        _api.On(HttpMethod.Post, "/api/users/u1/reset-password", HttpStatusCode.OK, """{"temporaryPassword":"Zp8nQw3LmT6yHc2v"}""");
        var cut = RenderPage(Paged(Account("u1", ["Employee"])));

        ButtonIn(RowFor(cut, "u1@company.test"), "Reset Password").Click();
        cut.FindAll("button").Last(b => b.TextContent.Trim() == "Reset Password").Click();

        cut.WaitForAssertion(() => cut.Find("[data-temporary-password]").TextContent.Trim().Should().Be("Zp8nQw3LmT6yHc2v"));
        cut.Find("[data-temporary-password-dialog]").TextContent.Should().Contain(
            "This password is shown once. Share it with the user securely; they will be asked to change it when they sign in.");

        ButtonIn(cut.Find("[data-temporary-password-dialog]"), "Done").Click();
        cut.FindAll("[data-temporary-password]").Should().BeEmpty();
    }

    [Fact]
    public void Deactivating_AfterConfirming_IsSent_AndTheListRefreshes()
    {
        _api.On(HttpMethod.Post, "/api/users/u1/deactivate", HttpStatusCode.OK, Account("u1", ["Employee"], active: false));
        var cut = RenderPage(Paged(Account("u1", ["Employee"])));

        ButtonIn(RowFor(cut, "u1@company.test"), "Deactivate").Click();
        cut.FindAll("button").Last(b => b.TextContent.Trim() == "Deactivate").Click();

        cut.WaitForAssertion(() => _api.Requests.Count(r => r.RequestUri!.PathAndQuery == FirstPage).Should().Be(2));
        _api.Requests.Should().Contain(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == "/api/users/u1/deactivate");
    }

    [Fact]
    public void ARefusedAction_ShowsTheApisReason()
    {
        _api.On(HttpMethod.Post, "/api/users/u1/reactivate", HttpStatusCode.Forbidden,
            """{"title":"Not allowed","status":403,"detail":"Only an administrator can change an Admin or HR Manager account."}""");
        var cut = RenderPage(Paged(Account("u1", ["Employee"], active: false)));

        ButtonIn(RowFor(cut, "u1@company.test"), "Reactivate").Click();

        cut.WaitForAssertion(() => cut.Find("[data-action-error]").TextContent
            .Should().Contain("Only an administrator can change an Admin or HR Manager account."));
    }

    [Fact]
    public void CreatingAnAccount_SendsItsDetails_AndShowsTheTemporaryPassword()
    {
        _api.On(HttpMethod.Post, "/api/users", HttpStatusCode.Created,
            $$"""{"account":{{Account("new.hire", ["Employee", "Manager"], mustChange: true)}},"temporaryPassword":"Kx7mPq2RtW9zNb4s"}""");
        var cut = RenderPage(Paged(Account("u1", ["Employee"])));

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "New Account").Click();
        cut.Find("#account-email").Input("new.hire@company.test");
        cut.Find("#account-first-name").Input("Ana");
        cut.Find("#account-last-name").Input("Reyes");
        cut.Find("form[data-create-account] [data-role=Manager]").Click();
        cut.Find("form[data-create-account]").Submit();

        cut.WaitForAssertion(() => cut.Find("[data-temporary-password]").TextContent.Trim().Should().Be("Kx7mPq2RtW9zNb4s"));
        var body = BodyOf(HttpMethod.Post, "/api/users");
        body.GetProperty("email").GetString().Should().Be("new.hire@company.test");
        body.GetProperty("firstName").GetString().Should().Be("Ana");
        body.GetProperty("employeeId").ValueKind.Should().Be(JsonValueKind.Null);
        Strings(body.GetProperty("roles")).Should().BeEquivalentTo("Employee", "Manager");
    }

    [Fact]
    public void CreatingAnAccountWithoutAnEmail_SaysSo_WithoutCallingTheApi()
    {
        var cut = RenderPage(Paged(Account("u1", ["Employee"])));

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "New Account").Click();
        cut.Find("form[data-create-account]").Submit();

        cut.Find("[data-create-account-error]").TextContent.Should().Contain("Enter an email address.");
        _api.Requests.Should().NotContain(r => r.Method == HttpMethod.Post);
    }
}
