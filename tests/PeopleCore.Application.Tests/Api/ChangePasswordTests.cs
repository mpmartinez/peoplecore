using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PeopleCore.API.Controllers.Auth;
using PeopleCore.Infrastructure.Identity;
using Xunit;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// Changing a password is self-service: the account is the one the bearer token names, never one
/// the request body picks. The current password is checked through the same lockout counter as
/// sign-in, so a stolen token cannot be used to guess it an unlimited number of times.
/// </summary>
public class ChangePasswordTests
{
    private const string UserId = "3f2b8c1e-7a4d-4e9b-9c6f-1d2e3f4a5b6c";
    private const string Current = "OldPassw0rd";
    private const string Next = "NewPassw0rd";

    private readonly ApplicationUser _user = new() { Id = UserId, Email = "ana@company.test" };
    private readonly Mock<UserManager<ApplicationUser>> _users;
    private readonly Mock<SignInManager<ApplicationUser>> _signIn;
    private readonly AuthController _sut;

    public ChangePasswordTests()
    {
        _users = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        _signIn = new Mock<SignInManager<ApplicationUser>>(
            _users.Object, Mock.Of<IHttpContextAccessor>(), Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(),
            null!, null!, null!, null!);

        _users.Setup(u => u.FindByIdAsync(UserId)).ReturnsAsync(_user);
        _signIn.Setup(s => s.CheckPasswordSignInAsync(_user, Current, true)).ReturnsAsync(SignInResult.Success);
        _signIn.Setup(s => s.CheckPasswordSignInAsync(_user, It.Is<string>(p => p != Current), true)).ReturnsAsync(SignInResult.Failed);
        _users.Setup(u => u.ChangePasswordAsync(_user, Current, Next)).ReturnsAsync(IdentityResult.Success);
        _users.Setup(u => u.GetRolesAsync(_user)).ReturnsAsync(new List<string> { "Employee" });

        var permissions = new Mock<IRolePermissionReader>();
        permissions.Setup(p => p.GetPermissionsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        _sut = new AuthController(_users.Object, _signIn.Object, TestJwtConfiguration.Create(), permissions.Object);
        SignInAs(UserId);
    }

    private void SignInAs(string? userId)
    {
        var claims = userId is null ? [] : new[] { new Claim(ClaimTypes.NameIdentifier, userId) };
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")) }
        };
    }

    private void PasswordWasNeverChecked() =>
        _signIn.Verify(s => s.CheckPasswordSignInAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);

    private static string? DetailOf(IActionResult result) =>
        result.Should().BeOfType<BadRequestObjectResult>().Which.Value.Should().BeOfType<ProblemDetails>().Which.Detail;

    [Fact]
    public void TheEndpoint_RequiresASignedInCaller()
    {
        typeof(AuthController).GetMethod(nameof(AuthController.ChangePassword))!
            .GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
    }

    [Fact]
    public async Task TheRightCurrentPassword_ChangesThePasswordOfTheTokensOwnAccount_AndReturnsAFreshToken()
    {
        // Changing a password replaces the security stamp, which revokes the token the request came
        // with - so the response has to carry a new one or the user would be signed out.
        var result = await _sut.ChangePassword(new ChangePasswordRequest(Current, Next));

        result.Should().BeOfType<OkObjectResult>()
              .Which.Value.Should().BeOfType<AuthTokenResponse>()
              .Which.Token.Should().NotBeNullOrEmpty();
        _users.Verify(u => u.ChangePasswordAsync(_user, Current, Next), Times.Once);
    }

    [Fact]
    public async Task AWrongCurrentPassword_IsRejected_AndCountsTowardsLockout()
    {
        var result = await _sut.ChangePassword(new ChangePasswordRequest("guess1234", Next));

        DetailOf(result).Should().Be("Your current password is incorrect.");
        _signIn.Verify(s => s.CheckPasswordSignInAsync(_user, "guess1234", true), Times.Once);
        _users.Verify(u => u.ChangePasswordAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ALockedOutAccount_IsToldToWait_EvenWithTheRightPassword()
    {
        _signIn.Setup(s => s.CheckPasswordSignInAsync(_user, Current, true)).ReturnsAsync(SignInResult.LockedOut);

        var result = await _sut.ChangePassword(new ChangePasswordRequest(Current, Next));

        DetailOf(result).Should().Contain("Too many incorrect attempts");
        _users.Verify(u => u.ChangePasswordAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ANewPasswordIdenticalToTheCurrentOne_IsRejected_WithoutTouchingTheAccount()
    {
        var result = await _sut.ChangePassword(new ChangePasswordRequest(Current, Current));

        DetailOf(result).Should().Be("Your new password must be different from your current password.");
        PasswordWasNeverChecked();
    }

    [Fact]
    public async Task ANewPasswordThePolicyRejects_ReportsThePolicysOwnReasons()
    {
        _users.Setup(u => u.ChangePasswordAsync(_user, Current, "short"))
              .ReturnsAsync(IdentityResult.Failed(
                  new IdentityError { Code = "PasswordTooShort", Description = "Passwords must be at least 8 characters." },
                  new IdentityError { Code = "PasswordRequiresDigit", Description = "Passwords must have at least one digit ('0'-'9')." }));

        var result = await _sut.ChangePassword(new ChangePasswordRequest(Current, "short"));

        DetailOf(result).Should().Be("Passwords must be at least 8 characters. Passwords must have at least one digit ('0'-'9').");
    }

    [Theory]
    [InlineData("", Next)]
    [InlineData(Current, "")]
    public async Task AMissingPassword_IsRejected_WithoutTouchingTheAccount(string current, string next)
    {
        var result = await _sut.ChangePassword(new ChangePasswordRequest(current, next));

        result.Should().BeOfType<BadRequestObjectResult>();
        _users.Verify(u => u.FindByIdAsync(It.IsAny<string>()), Times.Never);
        PasswordWasNeverChecked();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("a-user-since-deleted")]
    public async Task ATokenWhoseAccountCannotBeFound_IsUnauthorized(string? userId)
    {
        SignInAs(userId);

        var result = await _sut.ChangePassword(new ChangePasswordRequest(Current, Next));

        result.Should().BeOfType<UnauthorizedResult>();
        PasswordWasNeverChecked();
    }
}
