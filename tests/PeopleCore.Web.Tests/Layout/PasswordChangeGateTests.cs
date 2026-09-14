using System.Net;
using System.Security.Claims;
using Blazored.LocalStorage;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PeopleCore.Web.Auth;
using PeopleCore.Web.Layout;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Layout;

/// <summary>
/// Someone signed in with a temporary password sees one thing - a form to replace it - whichever
/// page they asked for. The API refuses everything else anyway; this makes that make sense.
/// </summary>
public class PasswordChangeGateTests : BunitContext
{
    private readonly StubHttpHandler _api = new();
    private readonly Mock<ILocalStorageService> _storage = new();
    private readonly BunitAuthorizationContext _auth;

    public PasswordChangeGateTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        Services.AddSingleton(new JwtAuthStateProvider(_storage.Object));
        _auth = AddAuthorization();
        _auth.SetAuthorized("new.hire@company.test");
    }

    private IRenderedComponent<PasswordChangeGate> RenderGate() =>
        Render<PasswordChangeGate>(p => p.AddChildContent("""<p id="routed-page">Dashboard</p>"""));

    [Fact]
    public void WithAPasswordOfTheirOwn_TheRequestedPageShows()
    {
        var cut = RenderGate();

        cut.FindAll("#routed-page").Should().ContainSingle();
        cut.FindAll("[data-password-change-gate]").Should().BeEmpty();
    }

    [Fact]
    public void OnATemporaryPassword_TheFormReplacesThePage()
    {
        _auth.SetClaims(new Claim("must_change_password", "true"));

        var cut = RenderGate();

        cut.FindAll("#routed-page").Should().BeEmpty();
        cut.Find("[data-password-change-gate]").TextContent.Should().Contain("Set a new password");
        cut.Find("label[for=current-password]").TextContent.Should().Contain("Temporary password");
    }

    [Fact]
    public void SettingANewPassword_KeepsTheFreshToken_AndReloadsSignedInWithIt()
    {
        _auth.SetClaims(new Claim("must_change_password", "true"));
        _api.On(HttpMethod.Post, "/api/auth/change-password", HttpStatusCode.OK,
            """{"token":"fresh-token","email":"new.hire@company.test","roles":["Employee"],"mustChangePassword":false}""");
        var cut = RenderGate();

        cut.Find("#current-password").Input("Kx7mPq2RtW9zNb4s");
        cut.Find("#new-password").Input("MyOwnPassw0rd");
        cut.Find("#confirm-password").Input("MyOwnPassw0rd");
        cut.Find("form#change-password").Submit();

        var nav = Services.GetRequiredService<BunitNavigationManager>();
        cut.WaitForAssertion(() => nav.History.Should().NotBeEmpty());
        _storage.Verify(s => s.SetItemAsync("auth_token", "fresh-token"), Times.Once);
        nav.History.First().Options.ForceLoad.Should().BeTrue();
    }
}
