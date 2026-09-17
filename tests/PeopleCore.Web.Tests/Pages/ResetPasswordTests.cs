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

/// <summary>Setting a new password from a link.</summary>
public class ResetPasswordTests : BunitContext
{
    private readonly StubHttpHandler _api = new();

    public ResetPasswordTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        Services.AddSingleton(new JwtAuthStateProvider(Mock.Of<ILocalStorageService>()));
    }

    private NavigationManager Nav => Services.GetRequiredService<NavigationManager>();

    private IRenderedComponent<ResetPassword> OpenLink(string email = "ana@company.test", string token = "tok")
    {
        Nav.NavigateTo($"/reset-password?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}");
        return Render<ResetPassword>();
    }

    private static void Fill(IRenderedComponent<ResetPassword> cut, string password)
    {
        cut.Find("#new-password").Input(password);
        cut.Find("#confirm-password").Input(password);
        cut.Find("form").Submit();
    }

    [Fact]
    public void TheCurrentPasswordIsNotAskedFor()
    {
        OpenLink().FindAll("#current-password").Should().BeEmpty();
    }

    [Fact]
    public void ANewPassword_IsSentWithTheTokenFromTheLink()
    {
        _api.On(HttpMethod.Post, "/api/auth/reset-password", HttpStatusCode.OK, """{"message":"Your password has been changed. Sign in with your new password."}""");

        Fill(OpenLink(), "N3wPassword");

        _api.RequestBodies.Single().Should().Contain("tok").And.Contain("ana@company.test");
    }

    [Fact]
    public void AfterwardsTheUserIsSentToSignIn()
    {
        _api.On(HttpMethod.Post, "/api/auth/reset-password", HttpStatusCode.OK, """{"message":"Your password has been changed. Sign in with your new password."}""");

        Fill(OpenLink(), "N3wPassword");

        Nav.Uri.Should().EndWith("/login?reset=1");
    }

    [Fact]
    public void AStaleLink_SaysSo_AndOffersAnotherOne()
    {
        _api.On(HttpMethod.Post, "/api/auth/reset-password", HttpStatusCode.BadRequest,
            """{"title":"Link no longer valid","detail":"This link has expired or has already been used. Ask for a new one.","status":400}""");

        var cut = OpenLink();
        Fill(cut, "N3wPassword");

        cut.Find("[data-password-result]").TextContent.Should().Contain("This link has expired or has already been used");
        cut.FindAll("a[href='/forgot-password']").Should().NotBeEmpty();
    }

    [Fact]
    public void ALinkWithNoToken_SaysItIsNotUsable_AndAsksForNothing()
    {
        Nav.NavigateTo("/reset-password");

        var cut = Render<ResetPassword>();

        cut.Find("[data-link-error]").TextContent.Should().Contain("This link has expired or has already been used");
        cut.FindAll("#new-password").Should().BeEmpty();
    }
}
