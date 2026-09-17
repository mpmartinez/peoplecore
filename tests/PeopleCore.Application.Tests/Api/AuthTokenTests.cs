using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PeopleCore.API.Accounts;
using PeopleCore.API.Controllers.Auth;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Infrastructure.Email;
using PeopleCore.Infrastructure.Identity;
using Xunit;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// What a token carries now that accounts can be revoked: the security stamp it was issued
/// against, and whether the account is still on a temporary password.
/// </summary>
public class AuthTokenTests
{
    private const string Email = "ana@company.test";
    private const string Password = "Passw0rd1";

    private readonly ApplicationUser _user = new() { Id = "u1", Email = Email, SecurityStamp = "stamp-1", IsActive = true };
    private readonly Mock<UserManager<ApplicationUser>> _users;
    private readonly Mock<SignInManager<ApplicationUser>> _signIn;
    private readonly Mock<IRolePermissionReader> _permissions = new();
    private readonly AuthController _sut;

    public AuthTokenTests()
    {
        _users = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        _signIn = new Mock<SignInManager<ApplicationUser>>(
            _users.Object, Mock.Of<IHttpContextAccessor>(), Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(),
            null!, null!, null!, null!);

        _users.Setup(u => u.FindByEmailAsync(Email)).ReturnsAsync(_user);
        _users.Setup(u => u.GetRolesAsync(_user)).ReturnsAsync(new List<string> { "Employee" });
        _signIn.Setup(s => s.CheckPasswordSignInAsync(_user, Password, true)).ReturnsAsync(SignInResult.Success);
        _permissions.Setup(p => p.GetPermissionsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync([]);

        _sut = new AuthController(_users.Object, _signIn.Object, TestJwtConfiguration.Create(), _permissions.Object,
            Mock.Of<IEmailSender>(), Mock.Of<IEmailSettingsStore>(), Mock.Of<IResetRequestThrottle>(),
            NullLogger<AuthController>.Instance);
    }

    private static AuthTokenResponse Session(IActionResult result) =>
        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<AuthTokenResponse>().Subject;

    private static Dictionary<string, string> ClaimsOf(AuthTokenResponse session) =>
        new JwtSecurityTokenHandler().ReadJwtToken(session.Token).Claims
            .GroupBy(c => c.Type).ToDictionary(g => g.Key, g => g.First().Value);

    private static List<string> PermissionsIn(AuthTokenResponse session) =>
        new JwtSecurityTokenHandler().ReadJwtToken(session.Token).Claims
            .Where(c => c.Type == "permission").Select(c => c.Value).ToList();

    [Fact]
    public async Task SigningInToADeactivatedAccount_IsRefused_ExactlyLikeAWrongPassword()
    {
        _user.IsActive = false;

        var result = await _sut.Login(new LoginRequest(Email, Password));

        result.Should().BeOfType<UnauthorizedObjectResult>();
        _signIn.Verify(s => s.CheckPasswordSignInAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task AToken_CarriesTheSecurityStampItWasIssuedAgainst()
    {
        var session = Session(await _sut.Login(new LoginRequest(Email, Password)));

        ClaimsOf(session).Should().Contain("sec_stamp", "stamp-1").And.NotContainKey("must_change_password");
        session.MustChangePassword.Should().BeFalse();
    }

    [Fact]
    public async Task AnAccountOnATemporaryPassword_SaysSo_InTheTokenAndTheResponse()
    {
        _user.MustChangePassword = true;

        var session = Session(await _sut.Login(new LoginRequest(Email, Password)));

        ClaimsOf(session).Should().Contain("must_change_password", "true");
        session.MustChangePassword.Should().BeTrue();
    }

    [Fact]
    public async Task ChangingATemporaryPassword_ClearsTheFlag_AndReturnsATokenForTheNewStamp()
    {
        _user.MustChangePassword = true;
        _users.Setup(u => u.FindByIdAsync("u1")).ReturnsAsync(_user);
        _users.Setup(u => u.ChangePasswordAsync(_user, Password, "NewPassw0rd"))
              .Callback(() => _user.SecurityStamp = "stamp-2")
              .ReturnsAsync(IdentityResult.Success);
        _users.Setup(u => u.UpdateAsync(_user)).ReturnsAsync(IdentityResult.Success);
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                    [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "u1")], "Bearer"))
            }
        };

        var session = Session(await _sut.ChangePassword(new ChangePasswordRequest(Password, "NewPassw0rd")));

        _user.MustChangePassword.Should().BeFalse();
        _users.Verify(u => u.UpdateAsync(_user), Times.Once);
        ClaimsOf(session).Should().Contain("sec_stamp", "stamp-2").And.NotContainKey("must_change_password");
    }

    [Fact]
    public async Task AToken_CarriesEveryPermissionTheAccountsRolesGrant_AndThePermissionsVersion()
    {
        _users.Setup(u => u.GetRolesAsync(_user)).ReturnsAsync(new List<string> { "Employee", "Manager" });
        _permissions.Setup(p => p.GetPermissionsAsync(
                It.Is<IEnumerable<string>>(r => r.OrderBy(x => x).SequenceEqual(new[] { "Employee", "Manager" })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([Permissions.ApprovalsTeam, Permissions.AnalyticsHr]);

        var session = Session(await _sut.Login(new LoginRequest(Email, Password)));

        PermissionsIn(session).Should().Equal("approvals.team", "analytics.hr");
        ClaimsOf(session).Should().Contain("perm_v", "1");
    }

    [Fact]
    public async Task AnAccountWhoseRolesGrantNothing_StillGetsThePermissionsVersion()
    {
        var session = Session(await _sut.Login(new LoginRequest(Email, Password)));

        PermissionsIn(session).Should().BeEmpty();
        ClaimsOf(session).Should().Contain("perm_v", "1");
    }
}
