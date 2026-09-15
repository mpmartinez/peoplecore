using Microsoft.AspNetCore.Authorization;

namespace PeopleCore.Web.Auth;

/// <summary>Guards a page the way the API guards the endpoints behind it: any one of these permissions will do.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public RequirePermissionAttribute(params string[] anyOf) : base(PermissionPolicy.NameFor(anyOf)) { }
}
