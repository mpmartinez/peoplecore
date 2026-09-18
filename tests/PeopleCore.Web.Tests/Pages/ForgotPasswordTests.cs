using System.Net;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Pages.Auth;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Pages;

/// <summary>
/// Asking for a link. Whatever the address, the page says the same thing - the page must not become
/// the tell the API refuses to be.
/// </summary>
public class ForgotPasswordTests : BunitContext
{
    private const string Answer = "If that address has an account, we've sent a link to reset the password.";

    private readonly StubHttpHandler _api = new();

    public ForgotPasswordTests() => Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));

    private IRenderedComponent<ForgotPassword> Ask(string email)
    {
        var cut = Render<ForgotPassword>();
        cut.Find("#email").Input(email);
        cut.Find("form").Submit();
        return cut;
    }

    [Fact]
    public void AnAddress_IsSentToTheApi_AndTheAnswerIsShown()
    {
        _api.On(HttpMethod.Post, "/api/auth/forgot-password", HttpStatusCode.OK, $$"""{"message":"{{Answer}}"}""");

        var cut = Ask("ana@company.test");

        cut.Find("[data-forgot-result]").TextContent.Should().Contain(Answer);
        _api.RequestBodies.Single().Should().Contain("ana@company.test");
    }

    [Fact]
    public void AnEmptyAddress_IsNotSent()
    {
        var cut = Ask("   ");

        cut.Find("[data-forgot-error]").TextContent.Should().Contain("Enter your email address.");
        _api.Requests.Should().BeEmpty();
    }

    [Fact]
    public void WhenTheApiFails_ThePageStillSaysTheSameThing()
    {
        _api.On(HttpMethod.Post, "/api/auth/forgot-password", HttpStatusCode.InternalServerError);

        var cut = Ask("ana@company.test");

        cut.Find("[data-forgot-result]").TextContent.Should().Contain(Answer);
    }

    [Fact]
    public void ThereIsAWayBackToSignIn()
    {
        Render<ForgotPassword>().FindAll("a[href='/login']").Should().NotBeEmpty();
    }
}
