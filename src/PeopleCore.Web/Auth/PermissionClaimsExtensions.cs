using System.Security.Claims;

namespace PeopleCore.Web.Auth;

public static class PermissionClaimsExtensions
{
    /// <summary>True for a signed-in user whose token grants any of <paramref name="anyOf"/>.</summary>
    public static bool HasAnyPermission(this ClaimsPrincipal user, params string[] anyOf) =>
        user.Identity?.IsAuthenticated == true && anyOf.Any(key => user.HasClaim(Permissions.ClaimType, key));
}
