using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PeopleCore.API.Controllers.Account;
using PeopleCore.Infrastructure.Email;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// Configuring the account the application sends from. The stored password never comes back out,
/// and the test button reports what the server actually said - this is the person fixing it.
/// </summary>
public class EmailSettingsControllerTests
{
    private readonly Mock<IEmailSettingsStore> _store = new();
    private readonly Mock<IEmailSender> _sender = new();
    private readonly EmailSettingsController _sut;

    private static readonly SaveEmailSettingsRequest Valid = new(
        "smtp.example.com", 587, true, "mailer@example.com", "s3cret",
        "hr@example.com", "PeopleCore", "https://people.example.com");

    public EmailSettingsControllerTests()
    {
        _sut = new EmailSettingsController(_store.Object, _sender.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, "admin@company.test")], "test"))
                }
            }
        };
    }

    private static string DetailOf(IActionResult result) =>
        result.Should().BeOfType<BadRequestObjectResult>().Which.Value
            .Should().BeOfType<ProblemDetails>().Subject.Detail!;

    [Fact]
    public async Task WithNothingConfigured_ThereIsNoContent()
    {
        _store.Setup(s => s.GetViewAsync(It.IsAny<CancellationToken>())).ReturnsAsync((MailAccountView?)null);

        (await _sut.Get(CancellationToken.None)).Result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task TheSettingsComeBackWithoutThePassword()
    {
        _store.Setup(s => s.GetViewAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new MailAccountView("smtp.example.com", 587, true, "mailer@example.com", true,
                "hr@example.com", "PeopleCore", "https://people.example.com", DateTime.UtcNow));

        var dto = (await _sut.Get(CancellationToken.None)).Result
            .Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<EmailSettingsDto>().Subject;

        dto.HasPassword.Should().BeTrue();
        dto.Host.Should().Be("smtp.example.com");
        dto.GetType().GetProperties().Select(p => p.Name).Should().NotContain("Password");
    }

    [Fact]
    public async Task SavingStoresTheAccount()
    {
        _store.Setup(s => s.GetViewAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new MailAccountView("smtp.example.com", 587, true, "mailer@example.com", true,
                "hr@example.com", "PeopleCore", "https://people.example.com", DateTime.UtcNow));

        await _sut.Save(Valid, CancellationToken.None);

        _store.Verify(s => s.SaveAsync(It.Is<MailAccount>(a =>
            a.Host == "smtp.example.com" && a.Port == 587 && a.UseStartTls
            && a.Password == "s3cret" && a.FromAddress == "hr@example.com"
            && a.AppBaseUrl == "https://people.example.com"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("", 587, "hr@example.com", "https://people.example.com", "Enter the mail server's address.")]
    [InlineData("smtp.example.com", 0, "hr@example.com", "https://people.example.com", "Enter a port between 1 and 65535.")]
    [InlineData("smtp.example.com", 587, "not-an-address", "https://people.example.com", "Enter the address mail is sent from.")]
    [InlineData("smtp.example.com", 587, "hr@example.com", "people.example.com", "Enter the app's web address, starting with https://.")]
    [InlineData("smtp.example.com", 587, "hr@example.com", "https://people.example.com/\"onmouseover=x", "Enter the app's web address, starting with https://.")]
    public async Task WhatIsNotUsable_IsRefusedWithTheReason(string host, int port, string from, string baseUrl, string expected)
    {
        var result = await _sut.Save(Valid with { Host = host, Port = port, FromAddress = from, AppBaseUrl = baseUrl },
            CancellationToken.None);

        DetailOf(result.Result!).Should().Be(expected);
        _store.Verify(s => s.SaveAsync(It.IsAny<MailAccount>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ATestMessage_GoesToTheAddressGiven()
    {
        var result = await _sut.SendTest(new SendTestEmailRequest("someone@example.com"), CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value!.Should().BeEquivalentTo(new { sent = true, to = "someone@example.com" });
        _sender.Verify(s => s.SendAsync(It.Is<EmailMessage>(m =>
            m.ToAddress == "someone@example.com" && m.Subject == "PeopleCore test message"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WithNoAddressGiven_ATestMessage_GoesToTheSignedInAdministrator()
    {
        var withNoBody = await _sut.SendTest(null, CancellationToken.None);
        withNoBody.Should().BeOfType<OkObjectResult>();

        var withBlankTo = await _sut.SendTest(new SendTestEmailRequest("  "), CancellationToken.None);
        withBlankTo.Should().BeOfType<OkObjectResult>();

        _sender.Verify(s => s.SendAsync(It.Is<EmailMessage>(m =>
            m.ToAddress == "admin@company.test" && m.Subject == "PeopleCore test message"), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("a@b.com, c@d.com")]
    [InlineData("Evil <a@b.com>")]
    [InlineData("a@b.com\r\nBcc: x@y.com")]
    public async Task AnInvalidAddress_IsRefused(string to)
    {
        var result = await _sut.SendTest(new SendTestEmailRequest(to), CancellationToken.None);

        DetailOf(result).Should().Be("Enter a valid email address to send the test to.");
        _sender.Verify(s => s.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AFailedTest_ReportsWhatTheServerSaid()
    {
        _sender.Setup(s => s.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("535 authentication failed"));

        var result = await _sut.SendTest(null, CancellationToken.None);

        DetailOf(result).Should().Contain("535 authentication failed");
    }
}
