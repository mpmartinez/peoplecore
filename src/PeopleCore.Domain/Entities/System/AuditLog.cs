using PeopleCore.Domain.Enums;

namespace PeopleCore.Domain.Entities.System;

/// <summary>
/// One entry in the audit trail. Append-only: written once, never updated or deleted - a database
/// trigger refuses both, and AppDbContext refuses to save either. The actor and request are
/// snapshots, so an entry still says who did it after the account is renamed or removed.
/// </summary>
public class AuditLog
{
    /// <summary>Database identity, so entries sort in the order they were written.</summary>
    public long Id { get; set; }

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public AuditCategory Category { get; set; }

    /// <summary><c>Created</c>/<c>Updated</c>/<c>Deleted</c> for data changes, otherwise a dotted name such as <c>auth.login-failed</c>.</summary>
    public string Action { get; set; } = string.Empty;

    public string? Summary { get; set; }
    public AuditOutcome Outcome { get; set; } = AuditOutcome.Success;
    public string? FailureReason { get; set; }

    // Who. UserId is the AspNetUsers key; null for anonymous calls and system jobs.
    public string? UserId { get; set; }
    public string? UserEmail { get; set; }

    /// <summary>Set when no user is signed in: <c>System: Leave accrual job</c>.</summary>
    public string? ActorName { get; set; }

    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    // The request. Every entry one request writes shares its CorrelationId, so an action can be
    // read alongside the data changes it made.
    public string? CorrelationId { get; set; }
    public string? HttpMethod { get; set; }
    public string? Path { get; set; }
    public int? StatusCode { get; set; }

    // What.
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }

    /// <summary>The employee the entry is about, when there is one - drives an employee's history.</summary>
    public Guid? SubjectEmployeeId { get; set; }

    /// <summary>JSON array of <c>{ "field", "old", "new" }</c>.</summary>
    public string? Changes { get; set; }

    /// <summary>JSON object of anything else worth keeping: route values, roles added, the email tried.</summary>
    public string? Metadata { get; set; }
}
