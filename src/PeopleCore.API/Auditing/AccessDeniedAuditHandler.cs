using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using PeopleCore.Application.Common.Auditing;
using PeopleCore.Domain.Enums;

namespace PeopleCore.API.Auditing;

/// <summary>
/// Records a signed-in caller being refused an endpoint by its permission policy, then answers as
/// the framework always does. A caller with no token is not recorded: every expired session would
/// fill the trail, and failed sign-ins are recorded where they happen.
/// </summary>
public sealed class AccessDeniedAuditHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden && context.User.Identity?.IsAuthenticated == true)
        {
            var required = authorizeResult.AuthorizationFailure?.FailedRequirements
                .Select(r => r.ToString() ?? r.GetType().Name)
                .ToList() ?? [];

            await context.RequestServices.GetRequiredService<IAuditTrail>().RecordAsync(
                AuditEvent.Failed(AuditCategory.Security, AuditActions.AccessDenied,
                    $"Access denied: {context.Request.Method} {context.Request.Path}",
                    "The account does not hold the permission this needs.") with
                {
                    StatusCode = StatusCodes.Status403Forbidden,
                    Metadata = new { Requirements = required },
                });
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
