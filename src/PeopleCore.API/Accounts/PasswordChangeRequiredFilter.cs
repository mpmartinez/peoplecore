using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace PeopleCore.API.Accounts;

/// <summary>
/// Refuses every action, except those marked <see cref="AllowDuringPasswordChangeAttribute"/>, to a
/// token that says its account is on a temporary password. Registered globally in Program.cs.
/// Enforced here rather than only in the web client, or the API would honour a temporary password
/// for as long as nobody used the browser.
///
/// Never blocks an anonymous endpoint - one with an <see cref="IAllowAnonymous"/> metadata entry, or
/// with no <see cref="IAuthorizeData"/> at all. Those endpoints let anyone through regardless of
/// what a bearer token claims, so refusing them here would not add security, only break them: a
/// still-valid temporary-password token attached to a request for one of them (the web client's
/// AuthTokenHandler attaches the stored token to every request, /login included) must not stop a
/// user who abandoned the "set a new password" screen from signing in again.
/// </summary>
public sealed class PasswordChangeRequiredFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.HttpContext.User.HasClaim(c => c.Type == AccountClaims.MustChangePassword)) return;
        if (context.ActionDescriptor.EndpointMetadata.OfType<AllowDuringPasswordChangeAttribute>().Any()) return;

        var metadata = context.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAllowAnonymous>().Any()) return;
        if (!metadata.OfType<IAuthorizeData>().Any()) return;

        context.Result = new ObjectResult(new ProblemDetails
        {
            Title = "Password change required",
            Detail = "You signed in with a temporary password. Choose a new password before doing anything else.",
            Status = StatusCodes.Status403Forbidden
        })
        {
            StatusCode = StatusCodes.Status403Forbidden
        };
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}

/// <summary>An action a user on a temporary password may still call.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AllowDuringPasswordChangeAttribute : Attribute { }
