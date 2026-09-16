using System.Net;
using System.Text.Json;
using FluentAssertions;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Services;

public class ApiClientAccountsTests
{
    private readonly StubHttpHandler _api = new();

    private ApiClient CreateClient() => new(StubHttpHandler.ClientFor(_api));

    private const string AccountJson =
        """{"id":"u-1","email":"ana@company.test","firstName":"Ana","lastName":"Reyes","roles":["Employee","Manager"],"isActive":true,"mustChangePassword":true,"employeeId":null,"employeeName":null,"canManage":true}""";

    private JsonElement OnlyBody() => JsonDocument.Parse(_api.RequestBodies.Single()!).RootElement;

    [Fact]
    public async Task ChangePassword_ReturnsTheFreshTokenTheApiIssues()
    {
        _api.On(HttpMethod.Post, "/api/auth/change-password", HttpStatusCode.OK,
            """{"token":"fresh-token","email":"ana@company.test","roles":["Employee"],"mustChangePassword":false}""");

        var session = await CreateClient().ChangePasswordAsync("Temp0rary1", "NewPassw0rd");

        session!.Token.Should().Be("fresh-token");
        session.MustChangePassword.Should().BeFalse();
    }

    [Fact]
    public async Task ListingAccounts_SendsTheSearch()
    {
        _api.On(HttpMethod.Get, "/api/users?page=2&pageSize=20&search=ana%40company", HttpStatusCode.OK,
            $$"""{"items":[{{AccountJson}}],"totalCount":21,"page":2,"pageSize":20,"totalPages":2}""");

        var page = await CreateClient().GetUserAccountsAsync(2, 20, "ana@company");

        page!.Items.Single().Roles.Should().Equal("Employee", "Manager");
    }

    [Fact]
    public async Task CreatingAnAccount_PostsItsDetails_AndReturnsTheTemporaryPassword()
    {
        var employeeId = Guid.Parse("9c4e1a7b-3d2f-4b8a-8e6c-0f5d2a9b7c44");
        _api.On(HttpMethod.Post, "/api/users", HttpStatusCode.Created,
            $$"""{"account":{{AccountJson}},"temporaryPassword":"Kx7mPq2RtW9zNb4s"}""");

        var created = await CreateClient().CreateUserAccountAsync(
            new CreateUserAccountRequest("ana@company.test", "Ana", "Reyes", employeeId, ["Employee", "Manager"]));

        created!.TemporaryPassword.Should().Be("Kx7mPq2RtW9zNb4s");
        var body = OnlyBody();
        body.GetProperty("email").GetString().Should().Be("ana@company.test");
        body.GetProperty("employeeId").GetGuid().Should().Be(employeeId);
        body.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().Equal("Employee", "Manager");
    }

    [Fact]
    public async Task SettingRoles_PutsTheWholeSet()
    {
        _api.On(HttpMethod.Put, "/api/users/u-1/roles", HttpStatusCode.OK, AccountJson);

        await CreateClient().SetUserRolesAsync("u-1", ["Employee", "Manager"]);

        OnlyBody().GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().Equal("Employee", "Manager");
    }

    [Fact]
    public async Task ResettingAPassword_ReturnsTheNewTemporaryPassword()
    {
        _api.On(HttpMethod.Post, "/api/users/u-1/reset-password", HttpStatusCode.OK, """{"temporaryPassword":"Zp8nQw3LmT6yHc2v"}""");

        (await CreateClient().ResetUserPasswordAsync("u-1")).Should().Be("Zp8nQw3LmT6yHc2v");
    }

    [Fact]
    public async Task ARefusedChange_ThrowsWithTheApisReason()
    {
        _api.On(HttpMethod.Post, "/api/users/u-1/deactivate", HttpStatusCode.Forbidden,
            """{"title":"Not allowed","status":403,"detail":"You can't deactivate your own account."}""");

        var act = () => CreateClient().DeactivateUserAsync("u-1");

        (await act.Should().ThrowAsync<HttpRequestException>())
            .Which.Message.Should().Be("You can't deactivate your own account.");
    }

    [Fact]
    public async Task AssignableRoles_SayWhichTheCallerMayGrant_AndWhyNot()
    {
        _api.On(HttpMethod.Get, "/api/users/assignable-roles", HttpStatusCode.OK,
            """[{"name":"Admin","grantable":false,"reason":"Only an administrator can grant or remove the Admin role."},{"name":"Manager","grantable":true,"reason":null}]""");

        var roles = await CreateClient().GetAssignableRolesAsync();

        roles!.Select(r => (r.Name, r.Grantable)).Should().Equal(("Admin", false), ("Manager", true));
        roles![0].Reason.Should().Contain("Only an administrator");
    }

    [Fact]
    public async Task Roles_AreListed_Created_Updated_AndDeleted()
    {
        const string role = """{"id":"r1","name":"Recruiter","description":null,"isSystem":false,"permissions":["recruitment.manage"],"accountCount":0,"canEdit":true}""";
        _api.On(HttpMethod.Get, "/api/roles", HttpStatusCode.OK, $"[{role}]")
            .On(HttpMethod.Get, "/api/roles/permissions", HttpStatusCode.OK,
                """[{"key":"recruitment.manage","group":"Recruitment","label":"Manage recruitment","description":"Job postings."}]""")
            .On(HttpMethod.Post, "/api/roles", HttpStatusCode.Created, role)
            .On(HttpMethod.Put, "/api/roles/r1", HttpStatusCode.OK, role)
            .On(HttpMethod.Delete, "/api/roles/r1", HttpStatusCode.NoContent);
        var client = CreateClient();

        (await client.GetRolesAsync())!.Single().Permissions.Should().Equal("recruitment.manage");
        (await client.GetPermissionCatalogueAsync())!.Single().Label.Should().Be("Manage recruitment");
        (await client.CreateRoleAsync(new SaveRoleRequest("Recruiter", null, ["recruitment.manage"])))!.Id.Should().Be("r1");
        (await client.UpdateRoleAsync("r1", new SaveRoleRequest("Recruiter", "Hires.", ["recruitment.manage"])))!.Name.Should().Be("Recruiter");
        await client.DeleteRoleAsync("r1");

        var post = JsonDocument.Parse(_api.RequestBodies[_api.Requests.FindIndex(r => r.Method == HttpMethod.Post)]!).RootElement;
        post.GetProperty("name").GetString().Should().Be("Recruiter");
        post.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).Should().Equal("recruitment.manage");
        _api.Requests.Should().Contain(r => r.Method == HttpMethod.Delete && r.RequestUri!.AbsolutePath == "/api/roles/r1");
    }

    [Fact]
    public async Task DeletingARoleStillInUse_ThrowsWithTheApisReason()
    {
        _api.On(HttpMethod.Delete, "/api/roles/r1", HttpStatusCode.BadRequest,
            """{"title":"Role not saved","status":400,"detail":"2 accounts still have Recruiter — remove it from them first."}""");

        var act = () => CreateClient().DeleteRoleAsync("r1");

        (await act.Should().ThrowAsync<HttpRequestException>()).Which.Message.Should().StartWith("2 accounts still have Recruiter");
    }
}
