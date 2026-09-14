using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PeopleCore.API.Accounts;
using PeopleCore.API.Extensions;
using PeopleCore.Infrastructure.Identity;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// A token is only as good as the account behind it right now. Deactivating the account, or any
/// change that replaces its security stamp, stops the token working on its very next request.
/// </summary>
public class AccountTokenValidatorTests
{
    private readonly Mock<UserManager<ApplicationUser>> _users = new(
        Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
    private readonly ApplicationUser _user = new() { Id = "u1", SecurityStamp = "stamp-1", IsActive = true };

    public AccountTokenValidatorTests() => _users.Setup(u => u.FindByIdAsync("u1")).ReturnsAsync(_user);

    private static ClaimsPrincipal Token(string? userId = "u1", string? stamp = "stamp-1")
    {
        var claims = new List<Claim>();
        if (userId is not null) claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
        if (stamp is not null) claims.Add(new Claim(AccountClaims.SecurityStamp, stamp));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }

    private Task<string?> Check(ClaimsPrincipal token) => new AccountTokenValidator(_users.Object).FindRejectionAsync(token);

    [Fact]
    public async Task ATokenForAnUnchangedActiveAccount_IsAccepted()
    {
        (await Check(Token())).Should().BeNull();
    }

    [Fact]
    public async Task ATokenIssuedBeforeTheAccountChanged_IsRejected()
    {
        _user.SecurityStamp = "stamp-2";

        (await Check(Token())).Should().Be("The account has changed since the token was issued.");
    }

    [Fact]
    public async Task ATokenForADeactivatedAccount_IsRejected()
    {
        _user.IsActive = false;

        (await Check(Token())).Should().Be("The account has been deactivated.");
    }

    [Fact]
    public async Task ATokenForADeletedAccount_IsRejected()
    {
        (await Check(Token(userId: "gone"))).Should().Be("The account no longer exists.");
    }

    [Fact]
    public async Task ATokenWithoutAStamp_IsRejected_SoTokensFromBeforeThisExistedStopWorking()
    {
        (await Check(Token(stamp: null))).Should().Be("The token was issued before revocation checks existed.");
    }

    [Fact]
    public async Task TheBearerPipeline_FailsARevokedToken_AndPassesAGoodOne()
    {
        var services = new ServiceCollection().AddSingleton(new AccountTokenValidator(_users.Object)).BuildServiceProvider();
        TokenValidatedContext ContextFor(ClaimsPrincipal principal) => new(
            new DefaultHttpContext { RequestServices = services },
            new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler)),
            new JwtBearerOptions()) { Principal = principal };
        var events = ServiceExtensions.CreateJwtBearerEvents();

        var good = ContextFor(Token());
        await events.OnTokenValidated(good);
        good.Result.Should().BeNull();

        _user.IsActive = false;
        var revoked = ContextFor(Token());
        await events.OnTokenValidated(revoked);
        revoked.Result!.Failure!.Message.Should().Be("The account has been deactivated.");
    }
}
