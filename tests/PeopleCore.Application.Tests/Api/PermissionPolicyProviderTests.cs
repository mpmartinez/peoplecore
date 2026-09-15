using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PeopleCore.API.Authorization;
using PeopleCore.Application.Common.Authorization;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// [RequirePermission] names a policy; the provider turns that name into "signed in and holding any
/// of these permissions". Evaluated through the real IAuthorizationService, as the framework does.
/// </summary>
public class PermissionPolicyProviderTests
{
    private readonly IAuthorizationService _authorization = new ServiceCollection()
        .AddLogging()
        .AddAuthorization()
        .AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>()
        .BuildServiceProvider()
        .GetRequiredService<IAuthorizationService>();

    private static ClaimsPrincipal SignedInWith(params string[] permissions) =>
        new(new ClaimsIdentity(permissions.Select(p => new Claim(Permissions.ClaimType, p)), "Bearer"));

    private async Task<bool> Allowed(ClaimsPrincipal user, params string[] anyOf) =>
        (await _authorization.AuthorizeAsync(user, null, PermissionPolicy.NameFor(anyOf))).Succeeded;

    [Fact]
    public async Task HoldingThePermission_IsAllowed()
    {
        (await Allowed(SignedInWith(Permissions.PayrollManage), Permissions.PayrollManage)).Should().BeTrue();
    }

    [Fact]
    public async Task HoldingAnyOneOfSeveral_IsAllowed()
    {
        (await Allowed(SignedInWith(Permissions.ApprovalsTeam), Permissions.ApprovalsTeam, Permissions.ApprovalsAll)).Should().BeTrue();
    }

    [Fact]
    public async Task HoldingSomethingElse_IsRefused()
    {
        (await Allowed(SignedInWith(Permissions.ApprovalsTeam), Permissions.PayrollManage)).Should().BeFalse();
    }

    [Fact]
    public async Task ARoleClaimWithTheSameName_DoesNotCount()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, Permissions.PayrollManage)], "Bearer"));

        (await Allowed(user, Permissions.PayrollManage)).Should().BeFalse();
    }

    [Fact]
    public async Task AnAnonymousCaller_IsRefused_EvenWithTheClaim()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity([new Claim(Permissions.ClaimType, Permissions.PayrollManage)]));

        (await Allowed(anonymous, Permissions.PayrollManage)).Should().BeFalse();
    }

    [Fact]
    public async Task APolicyNameThatIsNotAPermission_IsLeftToTheDefaultProvider()
    {
        var provider = new PermissionPolicyProvider(Options.Create(new AuthorizationOptions()));

        (await provider.GetPolicyAsync("SomeOtherPolicy")).Should().BeNull();
    }

    [Fact]
    public void TheAttribute_NamesThePolicyForItsPermissions_AndIsStillAnAuthorizeAttribute()
    {
        var attribute = new RequirePermissionAttribute(Permissions.ApprovalsTeam, Permissions.ApprovalsAll);

        attribute.Policy.Should().Be("permission:approvals.team|approvals.all");
        attribute.AnyOf.Should().Equal("approvals.team", "approvals.all");
        attribute.Should().BeAssignableTo<IAuthorizeData>("the password-change filter and the coverage test look for IAuthorizeData");
    }
}
