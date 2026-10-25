using System.Text.RegularExpressions;
using Microsoft.Playwright;
using PeopleCore.E2E.Tests.Infrastructure;

namespace PeopleCore.E2E.Tests.Auth;

public sealed class SignInTests(PeopleCoreApp app) : E2ETest(app)
{
    [Fact]
    public async Task TheSeededAdministrator_CanSignIn_AndLandsOnTheDashboard()
    {
        await SignInAsync();

        await Expect(Page).ToHaveURLAsync($"{App.WebUrl}/");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task AWrongPassword_IsRefused_AndStaysOnTheSignInPage()
    {
        await Page.GotoAsync("/login");

        await FillSignInFormAsync(E2ECredentials.AdminEmail, "not-the-password");

        await Expect(Page.GetByText("Invalid email or password.")).ToBeVisibleAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/login$"));
    }

    [Fact]
    public async Task AnAnonymousVisitor_ToAProtectedPage_IsSentToSignIn_WithTheWayBack()
    {
        await Page.GotoAsync("/employees");

        await Expect(Page).ToHaveURLAsync(new Regex(@"/login\?returnUrl=.*employees"));
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Sign In" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task LoggingOut_EndsTheSession()
    {
        await SignInAsync();

        await Page.GetByTitle("Account").ClickAsync();
        await Page.GetByText("Log out").ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/login"));

        // The token is gone, not merely the page: a protected page sends us back to sign in.
        await Page.GotoAsync("/");
        await Expect(Page).ToHaveURLAsync(new Regex("/login"));
    }
}
