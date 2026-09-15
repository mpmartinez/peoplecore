using Microsoft.AspNetCore.Authorization;
using PeopleCore.Application.Common.Authorization;

namespace PeopleCore.API.Authorization;

/// <summary>
/// Allows a signed-in caller holding any one of <see cref="AnyOf"/>. Replaces role-name
/// authorization: roles are data an admin can change, so an endpoint names what it needs rather
/// than who used to have it. Still an AuthorizeAttribute, so everything that looks for
/// IAuthorizeData - PasswordChangeRequiredFilter, ControllerAuthorizationCoverageTests - sees it.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public RequirePermissionAttribute(params string[] anyOf) : base(PermissionPolicy.NameFor(anyOf))
    {
        AnyOf = anyOf;
    }

    public IReadOnlyList<string> AnyOf { get; }
}
