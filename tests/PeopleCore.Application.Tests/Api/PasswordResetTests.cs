using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PeopleCore.API.Accounts;
using PeopleCore.API.Controllers.Auth;
using PeopleCore.Infrastructure.Email;
using PeopleCore.Infrastructure.Identity;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// Asking for a reset link and using one. The answer to a request never says whether the address
/// has an account: not for an unknown address, a deactivated one, a rate-limited caller, or a mail
/// server that refused the message.
/// </summary>
public class PasswordResetTests
{
    private const string Email = "ana@company.test";
    private const string Answer = "If that address has an account, we've sent a link to reset the password.";

    private static readonly MailAccount Account = new(
        "smtp.example.com", 587, true, "mailer@example.com", "s3cret",
        "hr@example.com", "PeopleCore", "https://people.example.com");

    private readonly ApplicationUser _user = new()
    {
        Id = "u1", Email = Email, FirstName = "Ana", SecurityStamp = "stamp-1", IsActive = true
    };

    private readonly Mock<UserManager<ApplicationUser>> _users;
    private readonly Mock<IEmailSender> _email = new();
    private readonly Mock<IEmailSettingsStore> _mailSettings = new();
    private readonly Mock<IResetRequestThrottle> _throttle = new();
    private readonly AuthController _sut;

    public PasswordResetTests()
    {
        _users = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        var signIn = new Mock<SignInManager<ApplicationUser>>(
            _users.Object, Mock.Of<IHttpContextAccessor>(), Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(),
            null!, null!, null!, null!);

        _users.Setup(u => u.FindByEmailAsync(Email)).ReturnsAsync(_user);
        _users.Setup(u => u.GeneratePasswordResetTokenAsync(_user)).ReturnsAsync("reset-token");
        _users.Setup(u => u.ResetPasswordAsync(_user, "reset-token", It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        _users.Setup(u => u.UpdateAsync(_user)).ReturnsAsync(IdentityResult.Success);
        _users.Setup(u => u.SetLockoutEndDateAsync(_user, null)).ReturnsAsync(IdentityResult.Success);
        _users.Setup(u => u.ResetAccessFailedCountAsync(_user)).ReturnsAsync(IdentityResult.Success);
        _mailSettings.Setup(s => s.GetAccountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Account);
        _throttle.Setup(t => t.TryRequest(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

        _sut = new AuthController(_users.Object, signIn.Object, TestJwtConfiguration.Create(),
            Mock.Of<IRolePermissionReader>(), _email.Object, _mailSettings.Object, _throttle.Object,
            NullLogger<AuthController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    // The endpoints answer with an anonymous object, so the message is read by reflection.
    private static string MessageOf(IActionResult result)
    {
        var value = result.Should().BeOfType<OkObjectResult>().Which.Value!;
        return value.GetType().GetProperty("message")!.GetValue(value)!.ToString()!;
    }

    private static string DetailOf(IActionResult result) =>
        result.Should().BeOfType<BadRequestObjectResult>().Which.Value
            .Should().BeOfType<ProblemDetails>().Subject.Detail!;

    private EmailMessage? Sent() =>
        _email.Invocations.Where(i => i.Method.Name == nameof(IEmailSender.SendAsync))
            .Select(i => (EmailMessage)i.Arguments[0]).SingleOrDefault();

    [Fact]
    public async Task AKnownAddress_IsSentALinkCarryingTheToken()
    {
        var result = await _sut.ForgotPassword(new ForgotPasswordRequest(Email), CancellationToken.None);

        MessageOf(result).Should().Be(Answer);
        Sent()!.ToAddress.Should().Be(Email);
        Sent()!.Text.Should()
            .Contain("https://people.example.com/reset-password?")
            .And.Contain("token=reset-token")
            .And.Contain("email=ana%40company.test");
    }

    [Fact]
    public async Task AnUnknownAddress_GetsTheSameAnswer_AndNoMail()
    {
        _users.Setup(u => u.FindByEmailAsync("nobody@company.test")).ReturnsAsync((ApplicationUser?)null);

        var result = await _sut.ForgotPassword(new ForgotPasswordRequest("nobody@company.test"), CancellationToken.None);

        MessageOf(result).Should().Be(Answer);
        Sent().Should().BeNull();
    }

    [Fact]
    public async Task ADeactivatedAccount_GetsTheSameAnswer_AndNoMail()
    {
        _user.IsActive = false;

        var result = await _sut.ForgotPassword(new ForgotPasswordRequest(Email), CancellationToken.None);

        MessageOf(result).Should().Be(Answer);
        Sent().Should().BeNull();
    }

    [Fact]
    public async Task ARateLimitedCaller_GetsTheSameAnswer_AndNoMail()
    {
        _throttle.Setup(t => t.TryRequest(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        var result = await _sut.ForgotPassword(new ForgotPasswordRequest(Email), CancellationToken.None);

        MessageOf(result).Should().Be(Answer);
        Sent().Should().BeNull();
        _users.Verify(u => u.GeneratePasswordResetTokenAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    [Fact]
    public async Task WithNoMailConfigured_TheAnswerIsTheSame_AndNothingIsSent()
    {
        _mailSettings.Setup(s => s.GetAccountAsync(It.IsAny<CancellationToken>())).ReturnsAsync((MailAccount?)null);

        var result = await _sut.ForgotPassword(new ForgotPasswordRequest(Email), CancellationToken.None);

        MessageOf(result).Should().Be(Answer);
        Sent().Should().BeNull();
    }

    [Fact]
    public async Task AMailServerThatRefuses_DoesNotChangeTheAnswer()
    {
        _email.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new InvalidOperationException("relay refused"));

        var result = await _sut.ForgotPassword(new ForgotPasswordRequest(Email), CancellationToken.None);

        MessageOf(result).Should().Be(Answer);
    }

    // A closed tab aborts the request. Once the throttle has let it through, the real user's link is
    // still sent: the database read and the send both ignore the request's token.
    [Fact]
    public async Task AClosedBrowser_DoesNotStopTheLinkBeingSent()
    {
        using var closed = new CancellationTokenSource();
        closed.Cancel();
        _mailSettings.Setup(s => s.GetAccountAsync(It.Is<CancellationToken>(t => t.IsCancellationRequested)))
                     .ThrowsAsync(new OperationCanceledException());

        var result = await _sut.ForgotPassword(new ForgotPasswordRequest(Email), closed.Token);

        MessageOf(result).Should().Be(Answer);
        Sent().Should().NotBeNull();
        var send = _email.Invocations.Single(i => i.Method.Name == nameof(IEmailSender.SendAsync));
        ((CancellationToken)send.Arguments[1]).IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task AGoodLink_SetsTheNewPassword()
    {
        var result = await _sut.ResetPassword(new ResetPasswordRequest(Email, "reset-token", "N3wPassword"));

        MessageOf(result).Should().Be("Your password has been changed. Sign in with your new password.");
        _users.Verify(u => u.ResetPasswordAsync(_user, "reset-token", "N3wPassword"), Times.Once);
    }

    [Fact]
    public async Task AGoodLink_ClearsALockout_AndTheMustChangeFlag()
    {
        _user.MustChangePassword = true;

        await _sut.ResetPassword(new ResetPasswordRequest(Email, "reset-token", "N3wPassword"));

        _users.Verify(u => u.SetLockoutEndDateAsync(_user, null), Times.Once);
        _users.Verify(u => u.ResetAccessFailedCountAsync(_user), Times.Once);
        _user.MustChangePassword.Should().BeFalse();
        _users.Verify(u => u.UpdateAsync(_user), Times.Once);
    }

    [Fact]
    public async Task AnExpiredOrUsedLink_SaysSo()
    {
        _users.Setup(u => u.ResetPasswordAsync(_user, "stale", It.IsAny<string>()))
              .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "InvalidToken", Description = "Invalid token." }));

        var result = await _sut.ResetPassword(new ResetPasswordRequest(Email, "stale", "N3wPassword"));

        DetailOf(result).Should().Be("This link has expired or has already been used. Ask for a new one.");
    }

    [Fact]
    public async Task ALinkForAnAddressWithNoAccount_SaysTheSameThing()
    {
        _users.Setup(u => u.FindByEmailAsync("nobody@company.test")).ReturnsAsync((ApplicationUser?)null);

        var result = await _sut.ResetPassword(new ResetPasswordRequest("nobody@company.test", "reset-token", "N3wPassword"));

        DetailOf(result).Should().Be("This link has expired or has already been used. Ask for a new one.");
    }

    // Deactivation has to hold even against a link issued before it.
    [Fact]
    public async Task ADeactivatedAccount_CannotUseEvenAValidLink()
    {
        _user.IsActive = false;

        var result = await _sut.ResetPassword(new ResetPasswordRequest(Email, "reset-token", "N3wPassword"));

        DetailOf(result).Should().Be("This link has expired or has already been used. Ask for a new one.");
        _users.Verify(u => u.ResetPasswordAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task APasswordThePolicyRefuses_ComesBackWithThePolicysReason()
    {
        _users.Setup(u => u.ResetPasswordAsync(_user, "reset-token", "short"))
              .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "PasswordTooShort", Description = "Passwords must be at least 8 characters." }));

        var result = await _sut.ResetPassword(new ResetPasswordRequest(Email, "reset-token", "short"));

        DetailOf(result).Should().Be("Passwords must be at least 8 characters.");
    }

    [Fact]
    public async Task WhetherResetsAreAvailable_FollowsWhetherMailIsConfigured()
    {
        var configured = await _sut.PasswordResetAvailable(CancellationToken.None);
        configured.Should().BeOfType<OkObjectResult>().Which.Value!.ToString().Should().Contain("True");

        _mailSettings.Setup(s => s.GetAccountAsync(It.IsAny<CancellationToken>())).ReturnsAsync((MailAccount?)null);

        var not = await _sut.PasswordResetAvailable(CancellationToken.None);
        not.Should().BeOfType<OkObjectResult>().Which.Value!.ToString().Should().Contain("False");
    }
}
