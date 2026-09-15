using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using PeopleCore.Application.Common.Authorization;

namespace PeopleCore.API.Authorization;

/// <summary>
/// Builds the policy behind every "permission:..." name on demand, so the permissions need no
/// registering one by one. Any other name falls through to the default provider.
/// </summary>
public class PermissionPolicyProvider : DefaultAuthorizationPolicyProvider
{
    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : base(options) { }

    public override Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!PermissionPolicy.TryParse(policyName, out var anyOf))
            return base.GetPolicyAsync(policyName);

        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAssertion(context => anyOf.Any(key => context.User.HasClaim(Permissions.ClaimType, key)))
            .Build();
        return Task.FromResult<AuthorizationPolicy?>(policy);
    }
}
