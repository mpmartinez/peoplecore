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
/// Never blocks an endpoint marked <see cref="IAllowAnonymous"/>. Those let anyone through regardless
/// of what a bearer token claims, so refusing them would add no security, only break them: the web
/// client's AuthTokenHandler attaches the stored token to every request, /login included, and a
/// still-valid temporary-password token must not stop a user who abandoned the "set a new password"
/// screen from signing in again. Only an explicit [AllowAnonymous] counts - an action without
/// [Authorize] of its own may still require sign-in through a fallback policy or
/// RequireAuthorization() on the route, which this metadata does not show. ControllerAuthorizationCoverageTests
/// keeps every endpoint declaring one or the other.
/// </summary>
public sealed class PasswordChangeRequiredFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.HttpContext.User.HasClaim(c => c.Type == AccountClaims.MustChangePassword)) return;
        if (context.ActionDescriptor.EndpointMetadata.OfType<AllowDuringPasswordChangeAttribute>().Any()) return;

        if (context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any()) return;

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
