namespace PeopleCore.Application.Common.Auditing;

/// <summary>
/// Who is acting and from where, as the audit trail records it. Read from the HTTP request; a
/// background job, which has none, names itself through <see cref="SystemActor"/>.
/// </summary>
public interface IAuditContext
{
    string? UserId { get; }
    string? UserEmail { get; }
    string? IpAddress { get; }
    string? UserAgent { get; }

    /// <summary>The request's id. Every entry one request writes carries it.</summary>
    string? CorrelationId { get; }

    string? HttpMethod { get; }
    string? Path { get; }

    /// <summary>
    /// The name a job acting without a user goes by, written as <c>System: {SystemActor}</c>. Set it
    /// at the start of the job's scope.
    /// </summary>
    string? SystemActor { get; set; }
}
