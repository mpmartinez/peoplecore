using System.Security.Claims;
using Blazored.LocalStorage;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Authorization;
using Moq;
using PeopleCore.Web.Auth;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Auth;

public class JwtAuthStateProviderTests
{
    private readonly Mock<ILocalStorageService> _storage = new();

    private JwtAuthStateProvider CreateProvider() => new(_storage.Object);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetAuthenticationState_WithNoStoredToken_IsAnonymous(string? stored)
    {
        _storage.Setup(s => s.GetItemAsync<string?>("auth_token")).ReturnsAsync(stored);

        var state = await CreateProvider().GetAuthenticationStateAsync();

        state.User.Identity!.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task GetAuthenticationState_WithStoredToken_ExposesItsRoles()
    {
        // The nav menu and every [Authorize(Roles = ...)] page read roles off this principal, so
        // the role claims have to survive the trip through the JWT with a type IsInRole recognises.
        _storage.Setup(s => s.GetItemAsync<string?>("auth_token"))
            .ReturnsAsync(FakeJwt.For("hr@company.test", "HRManager", "Manager"));

        var state = await CreateProvider().GetAuthenticationStateAsync();

        state.User.Identity!.IsAuthenticated.Should().BeTrue();
        state.User.IsInRole("HRManager").Should().BeTrue();
        state.User.IsInRole("Manager").Should().BeTrue();
        state.User.IsInRole("Admin").Should().BeFalse();
        state.User.FindFirst(ClaimTypes.Email)!.Value.Should().Be("hr@company.test");
    }

    [Fact]
    public async Task Login_StoresTheTokenAndAnnouncesTheNewUser()
    {
        var token = FakeJwt.For("admin@company.test", "Admin");
        _storage.Setup(s => s.GetItemAsync<string?>("auth_token")).ReturnsAsync(token);
        var provider = CreateProvider();
        Task<AuthenticationState>? announced = null;
        provider.AuthenticationStateChanged += task => announced = task;

        await provider.LoginAsync(token);

        _storage.Verify(s => s.SetItemAsync("auth_token", token), Times.Once);
        announced.Should().NotBeNull();
        (await announced!).User.IsInRole("Admin").Should().BeTrue();
    }

    [Fact]
    public async Task Logout_RemovesTheTokenAndAnnouncesAnAnonymousUser()
    {
        var provider = CreateProvider();
        Task<AuthenticationState>? announced = null;
        provider.AuthenticationStateChanged += task => announced = task;

        await provider.LogoutAsync();

        _storage.Verify(s => s.RemoveItemAsync("auth_token"), Times.Once);
        announced.Should().NotBeNull();
        (await announced!).User.Identity!.IsAuthenticated.Should().BeFalse();
    }
}
