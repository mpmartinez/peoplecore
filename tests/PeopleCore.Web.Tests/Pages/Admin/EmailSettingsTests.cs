using System.Net;
using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Pages.Admin;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Pages.Admin;

/// <summary>
/// Configuring the mail account. The stored password is never shown - the field is left blank and
/// blank means "keep it" - and the test button reports what the server said.
/// </summary>
public class EmailSettingsTests : BunitContext
{
    private const string Configured = """{"host":"smtp.example.com","port":587,"useStartTls":true,"username":"mailer@example.com","hasPassword":true,"fromAddress":"hr@example.com","fromName":"PeopleCore","appBaseUrl":"https://people.example.com","updatedAt":"2026-09-16T09:00:00Z"}""";

    private readonly StubHttpHandler _api = new();
    private readonly BunitAuthorizationContext _auth;

    public EmailSettingsTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        _auth = AddAuthorization();
        _auth.SetAuthorized("admin@company.test");
        _auth.SetClaims(SeededPermissions.ClaimsFor("Admin"));
    }

    [Fact]
    public void TheStoredSettings_FillTheForm_ButNotThePassword()
    {
        _api.On(HttpMethod.Get, "/api/email-settings", HttpStatusCode.OK, Configured);

        var cut = Render<EmailSettings>();

        cut.Find("#smtp-host").GetAttribute("value").Should().Be("smtp.example.com");
        cut.Find("#from-address").GetAttribute("value").Should().Be("hr@example.com");
        cut.Find("#smtp-password").GetAttribute("value").Should().BeNullOrEmpty();
        cut.Markup.Should().Contain("A password is stored");
    }

    [Fact]
    public void WithNothingConfigured_TheFormStartsEmpty_WithTheUsualPort()
    {
        _api.On(HttpMethod.Get, "/api/email-settings", HttpStatusCode.NoContent);

        var cut = Render<EmailSettings>();

        cut.Find("#smtp-host").GetAttribute("value").Should().BeNullOrEmpty();
        cut.Find("#smtp-port").GetAttribute("value").Should().Be("587");
    }

    [Fact]
    public void Saving_SendsWhatWasTyped()
    {
        _api.On(HttpMethod.Get, "/api/email-settings", HttpStatusCode.NoContent)
            .On(HttpMethod.Put, "/api/email-settings", HttpStatusCode.OK, Configured);

        var cut = Render<EmailSettings>();
        cut.Find("#smtp-host").Input("smtp.example.com");
        cut.Find("#from-address").Input("hr@example.com");
        cut.Find("#app-base-url").Input("https://people.example.com");
        cut.Find("form[data-email-settings]").Submit();

        _api.RequestBodies.Last().Should().Contain("smtp.example.com").And.Contain("https://people.example.com");
        cut.Find("[data-email-result]").TextContent.Should().Contain("Saved");
    }

    [Fact]
    public void WhatTheApiRefuses_IsShown()
    {
        _api.On(HttpMethod.Get, "/api/email-settings", HttpStatusCode.NoContent)
            .On(HttpMethod.Put, "/api/email-settings", HttpStatusCode.BadRequest,
                """{"title":"Email settings not saved","detail":"Enter the mail server's address.","status":400}""");

        var cut = Render<EmailSettings>();
        cut.Find("form[data-email-settings]").Submit();

        cut.Find("[data-email-error]").TextContent.Should().Contain("Enter the mail server's address.");
    }

    [Fact]
    public void TheTestButton_ReportsWhereTheMessageWent()
    {
        _api.On(HttpMethod.Get, "/api/email-settings", HttpStatusCode.OK, Configured)
            .On(HttpMethod.Post, "/api/email-settings/test", HttpStatusCode.OK, """{"sent":true,"to":"admin@company.test"}""");

        var cut = Render<EmailSettings>();
        cut.Find("button[data-send-test]").Click();

        cut.Find("[data-email-result]").TextContent.Should().Contain("admin@company.test");
    }

    [Fact]
    public void AFailedTest_ShowsTheServersReason()
    {
        _api.On(HttpMethod.Get, "/api/email-settings", HttpStatusCode.OK, Configured)
            .On(HttpMethod.Post, "/api/email-settings/test", HttpStatusCode.BadRequest,
                """{"title":"Email settings not saved","detail":"The mail server refused the message: 535 authentication failed","status":400}""");

        var cut = Render<EmailSettings>();
        cut.Find("button[data-send-test]").Click();

        cut.Find("[data-email-error]").TextContent.Should().Contain("535 authentication failed");
    }

    [Fact]
    public void TheTestMessage_GoesToTheAddressTyped()
    {
        _api.On(HttpMethod.Get, "/api/email-settings", HttpStatusCode.OK, Configured)
            .On(HttpMethod.Post, "/api/email-settings/test", HttpStatusCode.OK, """{"sent":true,"to":"someone@example.com"}""");

        var cut = Render<EmailSettings>();
        cut.Find("#test-recipient").Input("someone@example.com");
        cut.Find("button[data-send-test]").Click();

        _api.RequestBodies.Last().Should().Contain("someone@example.com");
    }
}
