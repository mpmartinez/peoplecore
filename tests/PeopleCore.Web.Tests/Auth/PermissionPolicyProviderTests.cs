using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Auth;

namespace PeopleCore.Web.Tests.Auth;

/// <summary>The same policies as the API, so a [RequirePermission] page admits exactly who the API does.</summary>
public class PermissionPolicyProviderTests
{
    private readonly IAuthorizationService _authorization = new ServiceCollection()
        .AddLogging()
        .AddAuthorizationCore()
        .AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>()
        .BuildServiceProvider()
        .GetRequiredService<IAuthorizationService>();

    private async Task<bool> Allowed(ClaimsPrincipal user, params string[] anyOf) =>
        (await _authorization.AuthorizeAsync(user, null, PermissionPolicy.NameFor(anyOf))).Succeeded;

    [Fact]
    public async Task APageRequiringAnyOfSeveral_AdmitsAHolderOfOne_AndNobodyElse()
    {
        var teamApprover = new ClaimsPrincipal(new ClaimsIdentity([new Claim(Permissions.ClaimType, Permissions.ApprovalsTeam)], "jwt"));
        var payroll = new ClaimsPrincipal(new ClaimsIdentity([new Claim(Permissions.ClaimType, Permissions.PayrollManage)], "jwt"));

        (await Allowed(teamApprover, Permissions.ApprovalsTeam, Permissions.ApprovalsAll)).Should().BeTrue();
        (await Allowed(payroll, Permissions.ApprovalsTeam, Permissions.ApprovalsAll)).Should().BeFalse();
    }

    [Fact]
    public void TheAttribute_NamesThePermissionPolicy()
    {
        new RequirePermissionAttribute(Permissions.UsersManage).Policy.Should().Be("permission:users.manage");
    }

    [Fact]
    public void APolicyForAKeyNotInTheCatalogue_CannotBeNamed()
    {
        // A typo here would otherwise reach the policy provider, which returns null for an
        // unrecognised policy name: [RequirePermission] throws when it builds the page, and
        // PermissionView would just as silently hide its content.
        var act = () => PermissionPolicy.NameFor("employees.veiw-all");

        act.Should().Throw<ArgumentException>().WithMessage("*employees.veiw-all*");
    }
}
