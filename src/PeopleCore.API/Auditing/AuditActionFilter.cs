using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using PeopleCore.Application.Common.Auditing;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.API.Auditing;

/// <summary>
/// Records every state-changing call (POST, PUT, PATCH, DELETE) as an action, and every call to an
/// <see cref="AuditAccessAttribute"/> endpoint as an access, with the outcome and the simple
/// arguments it was called with. Registered globally in Program.cs, so a new endpoint is recorded
/// without anyone remembering to. Request bodies are never kept - they can hold passwords - and the
/// field-level detail of a change is in its data-change entries, which share the request's id.
/// </summary>
public sealed class AuditActionFilter : IAsyncActionFilter
{
    private static readonly HashSet<string> StateChangingMethods =
        new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "PATCH", "DELETE" };

    private readonly IAuditTrail _audit;

    public AuditActionFilter(IAuditTrail audit) => _audit = audit;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var metadata = context.ActionDescriptor.EndpointMetadata;
        var access = metadata.OfType<AuditAccessAttribute>().FirstOrDefault();
        var named = metadata.OfType<AuditAsAttribute>().FirstOrDefault();
        var stateChanging = StateChangingMethods.Contains(context.HttpContext.Request.Method);
        var skipped = metadata.OfType<SkipAuditActionAttribute>().Any();

        // Read before the action runs: model binding has filled them, and the action cannot have
        // changed them yet.
        var arguments = SimpleArguments(context.ActionArguments);

        var executed = await next();

        if (access is not null)
        {
            var (status, _) = Outcome(executed);
            if (status is >= 200 and < 300)
            {
                var entry = Event(AuditCategory.Access, access.Action, access.Summary,
                    access.EntityType, access.IdRoute, access.EmployeeRoute, context, arguments, executed);
                if (access.SubjectIsCaller
                    && Guid.TryParse(context.HttpContext.User.FindFirst("employee_id")?.Value, out var caller))
                    entry = entry with { SubjectEmployeeId = caller };
                await _audit.RecordAsync(entry);
            }
            return;
        }

        if (!stateChanging || skipped) return;

        var descriptor = context.ActionDescriptor as ControllerActionDescriptor;
        var controller = descriptor?.ControllerName ?? "Api";
        var action = descriptor?.ActionName ?? context.ActionDescriptor.DisplayName ?? "Call";

        await _audit.RecordAsync(Event(
            AuditCategory.Action,
            named?.Action ?? $"{controller}.{action}",
            named?.Summary ?? $"{controller}: {Humanize(action)}",
            named?.EntityType ?? controller,
            named?.IdRoute ?? "id",
            named?.EmployeeRoute ?? "employeeId",
            context, arguments, executed));
    }

    private static AuditEvent Event(
        AuditCategory category, string action, string summary,
        string? entityType, string? idRoute, string? employeeRoute,
        ActionExecutingContext context, Dictionary<string, object?> arguments, ActionExecutedContext executed)
    {
        var (status, failure) = Outcome(executed);
        var entityId = idRoute is not null && arguments.TryGetValue(idRoute, out var id) ? id?.ToString() : null;
        Guid? employee = employeeRoute is not null && arguments.TryGetValue(employeeRoute, out var e) && e is Guid g ? g : null;

        return new AuditEvent(category, action, summary)
        {
            Outcome = failure is null ? AuditOutcome.Success : AuditOutcome.Failure,
            FailureReason = failure,
            StatusCode = status,
            EntityType = entityType,
            EntityId = entityId,
            SubjectEmployeeId = employee ?? (entityType == "Employee" && Guid.TryParse(entityId, out var self) ? self : null),
            Metadata = arguments.Count == 0 ? null : arguments,
        };
    }

    /// <summary>The status code the call answers with, and why it failed when it did.</summary>
    private static (int Status, string? Failure) Outcome(ActionExecutedContext executed)
    {
        if (executed.Exception is { } ex && !executed.ExceptionHandled)
        {
            // ExceptionHandlingMiddleware turns these into the response further out.
            return ex switch
            {
                DomainException => (StatusCodes.Status400BadRequest, ex.Message),
                KeyNotFoundException => (StatusCodes.Status404NotFound, ex.Message),
                _ => (StatusCodes.Status500InternalServerError, $"Unexpected error ({ex.GetType().Name})."),
            };
        }

        var status = executed.Result is IStatusCodeActionResult { StatusCode: { } code } ? code : StatusCodes.Status200OK;
        if (status < 400) return (status, null);

        var detail = executed.Result is ObjectResult { Value: var value } ? value switch
        {
            ProblemDetails problem => problem.Detail ?? problem.Title,
            string text => text,
            _ => null,
        } : null;
        return (status, detail ?? $"Answered {status}.");
    }

    /// <summary>
    /// The route and query values - ids, years, months, a report name. Request bodies and anything
    /// that looks like a secret are left out.
    /// </summary>
    private static Dictionary<string, object?> SimpleArguments(IDictionary<string, object?> arguments)
    {
        var simple = new Dictionary<string, object?>();
        foreach (var (name, value) in arguments)
        {
            if (name.Contains("password", StringComparison.OrdinalIgnoreCase)
                || name.Contains("token", StringComparison.OrdinalIgnoreCase))
                continue;
            if (value is null || IsSimple(value.GetType()))
                simple[name] = value is Enum ? value.ToString() : value;
        }
        return simple;
    }

    private static bool IsSimple(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsPrimitive || type.IsEnum
            || type == typeof(string) || type == typeof(Guid) || type == typeof(decimal)
            || type == typeof(DateOnly) || type == typeof(DateTime) || type == typeof(TimeOnly);
    }

    private static string Humanize(string name) =>
        string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : c.ToString()));
}
