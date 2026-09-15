using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace PeopleCore.Web.Auth;

/// <summary>Builds "signed in and holding any of these permissions" for every permission policy name a page uses.</summary>
public class PermissionPolicyProvider : DefaultAuthorizationPolicyProvider
{
    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : base(options) { }

    public override Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!PermissionPolicy.TryParse(policyName, out var anyOf))
            return base.GetPolicyAsync(policyName);

        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAssertion(context => context.User.HasAnyPermission([.. anyOf]))
            .Build();
        return Task.FromResult<AuthorizationPolicy?>(policy);
    }
}
