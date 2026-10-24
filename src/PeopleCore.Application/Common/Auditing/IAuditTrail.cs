using PeopleCore.Domain.Enums;

namespace PeopleCore.Application.Common.Auditing;

/// <summary>
/// An audit entry recorded explicitly, rather than caught from a save: a sign-in, a role change,
/// a download, an action's outcome. The actor and request come from <see cref="IAuditContext"/>
/// unless set here - a sign-in, for one, names the account before its token exists.
/// </summary>
public sealed record AuditEvent(AuditCategory Category, string Action, string? Summary = null)
{
    public AuditOutcome Outcome { get; init; } = AuditOutcome.Success;
    public string? FailureReason { get; init; }
    public string? EntityType { get; init; }
    public string? EntityId { get; init; }
    public Guid? SubjectEmployeeId { get; init; }
    public int? StatusCode { get; init; }

    /// <summary>Anything else worth keeping; serialized to JSON. Never a password or a request body.</summary>
    public object? Metadata { get; init; }

    public string? UserId { get; init; }
    public string? UserEmail { get; init; }

    public static AuditEvent Failed(AuditCategory category, string action, string summary, string reason) =>
        new(category, action, summary) { Outcome = AuditOutcome.Failure, FailureReason = reason };
}

public interface IAuditTrail
{
    /// <summary>
    /// Writes the entry at once, on its own connection: it is kept even when the request it
    /// describes fails, and it never saves the request's pending changes along with it. A failure
    /// to write is logged, never thrown - the trail must not take a sign-in down with it.
    /// </summary>
    Task RecordAsync(AuditEvent auditEvent, CancellationToken ct = default);
}
