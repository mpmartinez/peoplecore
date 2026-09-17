using System.Net;
using Blazored.LocalStorage;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PeopleCore.Web.Auth;
using PeopleCore.Web.Pages.Auth;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Pages;

public class LoginTests : BunitContext
{
    private readonly StubHttpHandler _api = new();
    private readonly Mock<ILocalStorageService> _storage = new();

    public LoginTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        Services.AddSingleton(new JwtAuthStateProvider(_storage.Object));
    }

    private string CurrentUri => Services.GetRequiredService<NavigationManager>().Uri;

    private static void SignIn(IRenderedComponent<Login> cut, string email, string password)
    {
        cut.Find("#email").Input(email);
        cut.Find("#password").Input(password);
        cut.Find("button:not([data-password-toggle])").Click();
    }

    [Theory]
    [InlineData("", "secret")]
    [InlineData("hr@company.test", "")]
    [InlineData("   ", "   ")]
    public void AsksForBothFields_WithoutCallingTheApi(string email, string password)
    {
        var cut = Render<Login>();

        SignIn(cut, email, password);

        cut.Find("[role=alert]").TextContent.Should().Contain("Please enter your email and password.");
        // Login also asks whether a reset link can be sent, on every render - that call is unrelated
        // to this sign-in attempt, so it is excluded rather than asserting no requests happened at all.
        _api.Requests.Should().NotContain(r => r.RequestUri!.PathAndQuery.Contains("/api/auth/login"));
    }

    [Fact]
    public void RejectedCredentials_ShowAnErrorAndStayPut()
    {
        _api.On(HttpMethod.Post, "/api/auth/login", HttpStatusCode.Unauthorized);
        var cut = Render<Login>();
        var before = CurrentUri;

        SignIn(cut, "hr@company.test", "wrong");

        cut.WaitForAssertion(() =>
            cut.Find("[role=alert]").TextContent.Should().Contain("Invalid email or password."));
        CurrentUri.Should().Be(before);
        _storage.Verify(s => s.SetItemAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void AnUnreachableApi_ShowsTheSameErrorInsteadOfCrashing()
    {
        _api.On(HttpMethod.Post, "/api/auth/login", () => throw new HttpRequestException("connection refused"));
        var cut = Render<Login>();

        SignIn(cut, "hr@company.test", "secret");

        cut.WaitForAssertion(() =>
            cut.Find("[role=alert]").TextContent.Should().Contain("Invalid email or password."));
        cut.Find("button:not([data-password-toggle])").HasAttribute("disabled").Should().BeFalse("the user has to be able to try again");
    }

    [Fact]
    public void AcceptedCredentials_StoreTheTokenAndOpenTheDashboard()
    {
        _api.On(HttpMethod.Post, "/api/auth/login", HttpStatusCode.OK,
            """{"token":"issued-token","email":"hr@company.test","roles":["HRManager"]}""");
        var cut = Render<Login>();

        SignIn(cut, "hr@company.test", "secret");

        cut.WaitForAssertion(() => CurrentUri.Should().Be("http://localhost/"));
        _storage.Verify(s => s.SetItemAsync("auth_token", "issued-token"), Times.Once);
        cut.FindAll("[role=alert]").Should().BeEmpty();
    }

    [Fact]
    public void PressingEnterInThePasswordField_SignsIn()
    {
        _api.On(HttpMethod.Post, "/api/auth/login", HttpStatusCode.OK,
            """{"token":"issued-token","email":"hr@company.test","roles":[]}""");
        var cut = Render<Login>();

        cut.Find("#email").Input("hr@company.test");
        cut.Find("#password").Input("secret");
        cut.Find("#password").KeyDown(Key.Enter);

        cut.WaitForAssertion(() =>
            _api.Requests.Should().ContainSingle(r => r.RequestUri!.PathAndQuery.Contains("/api/auth/login")));
    }

    [Fact]
    public void TheForgotPasswordLink_ShowsOnlyWhenTheApiCanSendMail()
    {
        _api.On(HttpMethod.Get, "/api/auth/password-reset-available", HttpStatusCode.OK, """{"available":true}""");

        Render<Login>().FindAll("a[href='/forgot-password']").Should().NotBeEmpty();
    }

    [Fact]
    public void WithNoMailConfigured_ThereIsNoForgotPasswordLink()
    {
        _api.On(HttpMethod.Get, "/api/auth/password-reset-available", HttpStatusCode.OK, """{"available":false}""");

        Render<Login>().FindAll("a[href='/forgot-password']").Should().BeEmpty();
    }

    [Fact]
    public void AfterAReset_TheLoginPageSaysSo()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/login?reset=1");

        Render<Login>().Find("[data-reset-done]").TextContent
            .Should().Contain("Your password has been changed. Sign in with your new password.");
    }
}
