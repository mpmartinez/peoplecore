using System.Net;
using Blazored.LocalStorage;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PeopleCore.Web.Auth;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Auth;

public class AuthTokenHandlerTests : IDisposable
{
    // Only here for its NavigationManager, which records where the handler tried to send the user.
    private readonly BunitContext _ctx = new();
    private readonly Mock<ILocalStorageService> _storage = new();
    private readonly StubHttpHandler _api = new();

    private BunitNavigationManager Nav => _ctx.Services.GetRequiredService<BunitNavigationManager>();

    private HttpClient CreateClient(string? storedToken)
    {
        _storage.Setup(s => s.GetItemAsync<string?>("auth_token")).ReturnsAsync(storedToken);
        var handler = new AuthTokenHandler(_storage.Object, Nav) { InnerHandler = _api };
        return StubHttpHandler.ClientFor(handler);
    }

    [Fact]
    public async Task AttachesTheStoredTokenAsABearerHeader()
    {
        _api.On(HttpMethod.Get, "/api/employees", HttpStatusCode.OK, "[]");
        using var client = CreateClient("the-token");

        await client.GetAsync("api/employees");

        var auth = _api.Requests.Single().Headers.Authorization;
        auth!.Scheme.Should().Be("Bearer");
        auth.Parameter.Should().Be("the-token");
    }

    [Fact]
    public async Task SendsNoAuthorizationHeader_WhenNoTokenIsStored()
    {
        _api.On(HttpMethod.Get, "/api/employees", HttpStatusCode.OK, "[]");
        using var client = CreateClient(null);

        await client.GetAsync("api/employees");

        _api.Requests.Single().Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task Unauthorized_OnAnOrdinaryCall_ClearsTheTokenAndSendsTheUserToLogin()
    {
        // An expired token: without this the app would keep sending it and every page would fail.
        _api.On(HttpMethod.Get, "/api/employees", HttpStatusCode.Unauthorized);
        using var client = CreateClient("expired-token");

        var response = await client.GetAsync("api/employees");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _storage.Verify(s => s.RemoveItemAsync("auth_token"), Times.Once);
        Nav.Uri.Should().EndWith("/login");
    }

    [Fact]
    public async Task Unauthorized_OnALoginAttempt_LeavesTheUserOnTheLoginPage()
    {
        // A wrong password is a 401 from /auth/login. Force-reloading the login page there would
        // wipe the error message the page is about to show.
        _api.On(HttpMethod.Post, "/api/auth/login", HttpStatusCode.Unauthorized);
        using var client = CreateClient(null);
        var before = Nav.Uri;

        await client.PostAsync("api/auth/login", null);

        _storage.Verify(s => s.RemoveItemAsync(It.IsAny<string>()), Times.Never);
        Nav.Uri.Should().Be(before);
    }

    public void Dispose() => _ctx.Dispose();
}
