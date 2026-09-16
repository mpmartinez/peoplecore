using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Moq;
using PeopleCore.API.Authorization;
using PeopleCore.API.Controllers.Auth;
using PeopleCore.API.Extensions;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Infrastructure.Identity;
using Xunit;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// AuthTokenTests reads the token's claims straight out of the JWT; the reflection-based tests
/// pin the policy names an attribute builds. Neither proves a token AuthController issues is
/// actually accepted by the bearer handler and then admitted or refused by a real permission
/// policy - this test runs a token through both, end to end.
/// </summary>
public class TokenToPolicyTests
{
    private const string Email = "ana@company.test";
    private const string Password = "Passw0rd1";

    private static async Task<string> IssueTokenAsync(IReadOnlyList<string> permissions)
    {
        var user = new ApplicationUser { Id = "u1", Email = Email, SecurityStamp = "stamp-1", IsActive = true };
        var users = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        var signIn = new Mock<SignInManager<ApplicationUser>>(
            users.Object, Mock.Of<IHttpContextAccessor>(), Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(),
            null!, null!, null!, null!);
        var permissionReader = new Mock<IRolePermissionReader>();

        users.Setup(u => u.FindByEmailAsync(Email)).ReturnsAsync(user);
        users.Setup(u => u.GetRolesAsync(user)).ReturnsAsync(new List<string> { "Employee" });
        signIn.Setup(s => s.CheckPasswordSignInAsync(user, Password, true)).ReturnsAsync(SignInResult.Success);
        permissionReader.Setup(p => p.GetPermissionsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(permissions);

        var sut = new AuthController(users.Object, signIn.Object, TestJwtConfiguration.Create(), permissionReader.Object);

        var result = await sut.Login(new LoginRequest(Email, Password));
        var session = result.Should().BeOfType<Microsoft.AspNetCore.Mvc.OkObjectResult>()
            .Which.Value.Should().BeOfType<AuthTokenResponse>().Subject;
        return session.Token;
    }

    /// <summary>
    /// The same TokenValidationParameters ServiceExtensions.AddJwtBearer configures, built from the
    /// same test configuration AuthController signed the token with - so this proves the token the
    /// controller issues is one the API's own bearer handler would actually accept.
    /// </summary>
    private static async Task<ClaimsPrincipal> ValidateAsync(string token)
    {
        var configuration = TestJwtConfiguration.Create();
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = configuration["Jwt:Issuer"],
            ValidAudience = configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(ServiceExtensions.ResolveJwtSigningKey(configuration))
        };

        var result = await new JsonWebTokenHandler { MapInboundClaims = true }.ValidateTokenAsync(token, parameters);
        result.IsValid.Should().BeTrue(result.Exception?.ToString());
        return new ClaimsPrincipal(result.ClaimsIdentity);
    }

    private static IAuthorizationService BuildAuthorizationService() => new ServiceCollection()
        .AddLogging()
        .AddAuthorization()
        .AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>()
        .BuildServiceProvider()
        .GetRequiredService<IAuthorizationService>();

    private static Task<bool> AllowedAsync(IAuthorizationService authorization, ClaimsPrincipal principal, params string[] anyOf) =>
        authorization.AuthorizeAsync(principal, null, PermissionPolicy.NameFor(anyOf))
            .ContinueWith(t => t.Result.Succeeded);

    [Fact]
    public async Task ATokenIssuedForSeveralPermissions_PassesItsPoliciesAndRefusesEverythingElse_ThroughTheRealPipeline()
    {
        var token = await IssueTokenAsync([Permissions.ApprovalsTeam, Permissions.PayrollManage]);

        var principal = await ValidateAsync(token);
        var authorization = BuildAuthorizationService();

        (await AllowedAsync(authorization, principal, Permissions.PayrollManage)).Should().BeTrue();
        (await AllowedAsync(authorization, principal, Permissions.ApprovalsAll)).Should().BeFalse();

        principal.FindAll(Permissions.ClaimType).Select(c => c.Value).Should()
            .BeEquivalentTo([Permissions.ApprovalsTeam, Permissions.PayrollManage]);
        principal.FindFirstValue(Permissions.VersionClaimType).Should().Be("1");
    }

    [Fact]
    public async Task ATokenIssuedForExactlyOnePermission_StillPassesThatPermissionsPolicy()
    {
        // A single "permission" claim serializes to a JSON string rather than an array; this
        // proves the validated principal still exposes it as a claim the policy can find.
        var token = await IssueTokenAsync([Permissions.PayrollManage]);

        var principal = await ValidateAsync(token);
        var authorization = BuildAuthorizationService();

        (await AllowedAsync(authorization, principal, Permissions.PayrollManage)).Should().BeTrue();
        principal.FindAll(Permissions.ClaimType).Select(c => c.Value).Should().Equal(Permissions.PayrollManage);
    }
}
