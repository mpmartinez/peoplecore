using System.Net;
using System.Text.Json;
using FluentAssertions;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Services;

/// <summary>Password resets and the mail settings, over the wire.</summary>
public class ApiClientEmailTests
{
    private readonly StubHttpHandler _api = new();

    private ApiClient CreateClient() => new(StubHttpHandler.ClientFor(_api));

    private string BodyField(string name) =>
        JsonDocument.Parse(_api.RequestBodies.Single(b => b is not null)!).RootElement.GetProperty(name).ToString();

    [Fact]
    public async Task WhetherResetsAreAvailable_ComesFromTheApi()
    {
        _api.On(HttpMethod.Get, "/api/auth/password-reset-available", HttpStatusCode.OK, """{"available":true}""");

        (await CreateClient().IsPasswordResetAvailableAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task WhenThatCallFails_ResetsCountAsUnavailable()
    {
        _api.On(HttpMethod.Get, "/api/auth/password-reset-available", HttpStatusCode.InternalServerError);

        (await CreateClient().IsPasswordResetAvailableAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task AskingForALink_PostsTheAddress()
    {
        _api.On(HttpMethod.Post, "/api/auth/forgot-password", HttpStatusCode.OK, """{"message":"If that address has an account, we've sent a link to reset the password."}""");

        await CreateClient().ForgotPasswordAsync("ana@company.test");

        BodyField("email").Should().Be("ana@company.test");
    }

    [Fact]
    public async Task UsingALink_PostsTheTokenAndTheNewPassword()
    {
        _api.On(HttpMethod.Post, "/api/auth/reset-password", HttpStatusCode.OK, """{"message":"Your password has been changed. Sign in with your new password."}""");

        await CreateClient().ResetPasswordAsync("ana@company.test", "tok", "N3wPassword");

        BodyField("email").Should().Be("ana@company.test");
        BodyField("token").Should().Be("tok");
        BodyField("newPassword").Should().Be("N3wPassword");
    }

    [Fact]
    public async Task AStaleLink_ThrowsWithTheApisSentence()
    {
        _api.On(HttpMethod.Post, "/api/auth/reset-password", HttpStatusCode.BadRequest,
            """{"title":"Link no longer valid","detail":"This link has expired or has already been used. Ask for a new one.","status":400}""");

        var act = () => CreateClient().ResetPasswordAsync("ana@company.test", "stale", "N3wPassword");

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*This link has expired or has already been used*");
    }

    [Fact]
    public async Task WithNoMailConfigured_TheSettingsAreNull()
    {
        _api.On(HttpMethod.Get, "/api/email-settings", HttpStatusCode.NoContent);

        (await CreateClient().GetEmailSettingsAsync()).Should().BeNull();
    }

    [Fact]
    public async Task TheSettings_AreReadAndSaved()
    {
        const string json = """{"host":"smtp.example.com","port":587,"useStartTls":true,"username":"mailer@example.com","hasPassword":true,"fromAddress":"hr@example.com","fromName":"PeopleCore","appBaseUrl":"https://people.example.com","updatedAt":"2026-09-16T09:00:00Z"}""";
        _api.On(HttpMethod.Get, "/api/email-settings", HttpStatusCode.OK, json)
            .On(HttpMethod.Put, "/api/email-settings", HttpStatusCode.OK, json);
        var client = CreateClient();

        (await client.GetEmailSettingsAsync())!.HasPassword.Should().BeTrue();
        var saved = await client.SaveEmailSettingsAsync(new SaveEmailSettingsRequest(
            "smtp.example.com", 587, true, "mailer@example.com", "s3cret", "hr@example.com", "PeopleCore", "https://people.example.com"));

        saved!.Host.Should().Be("smtp.example.com");
        BodyField("host").Should().Be("smtp.example.com");
        BodyField("port").Should().Be("587");
        BodyField("useStartTls").Should().Be("True");
        BodyField("password").Should().Be("s3cret");
        BodyField("fromAddress").Should().Be("hr@example.com");
        BodyField("appBaseUrl").Should().Be("https://people.example.com");
    }

    [Fact]
    public async Task ATestMessage_ReturnsWhereItWent()
    {
        _api.On(HttpMethod.Post, "/api/email-settings/test", HttpStatusCode.OK, """{"sent":true,"to":"admin@company.test"}""");

        (await CreateClient().SendTestEmailAsync("someone@example.com")).Should().Be("admin@company.test");

        BodyField("to").Should().Be("someone@example.com");
    }

    [Fact]
    public async Task AFailedTestMessage_ThrowsWithTheServersReason()
    {
        _api.On(HttpMethod.Post, "/api/email-settings/test", HttpStatusCode.BadRequest,
            """{"title":"Email settings not saved","detail":"The mail server refused the message: 535 authentication failed","status":400}""");

        var act = () => CreateClient().SendTestEmailAsync();

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("*535 authentication failed*");
    }
}
