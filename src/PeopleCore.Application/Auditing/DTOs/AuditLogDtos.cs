using PeopleCore.Domain.Enums;

namespace PeopleCore.Application.Auditing.DTOs;

/// <summary>
/// What the audit trail page filters by. <see cref="From"/> and <see cref="To"/> are Philippine
/// calendar dates, both included.
/// </summary>
public sealed record AuditLogFilter(
    DateOnly? From = null,
    DateOnly? To = null,
    AuditCategory? Category = null,
    string? Action = null,
    string? EntityType = null,
    string? EntityId = null,
    Guid? EmployeeId = null,
    string? User = null,
    AuditOutcome? Outcome = null,
    string? CorrelationId = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 50);

public sealed record AuditLogEntryDto(
    long Id,
    DateTime OccurredAt,
    AuditCategory Category,
    string Action,
    string? Summary,
    AuditOutcome Outcome,
    string? FailureReason,
    string? UserId,
    string? UserEmail,
    string? ActorName,
    string? IpAddress,
    string? EntityType,
    string? EntityId,
    Guid? SubjectEmployeeId,
    string? SubjectEmployeeName,
    int? StatusCode,
    string? CorrelationId,
    int ChangeCount);

/// <summary>One field of a data change, as text: a value, <c>(changed)</c> for a secret, or null.</summary>
public sealed record AuditFieldChangeDto(string Field, string? Old, string? New);

public sealed record AuditLogDetailDto(
    AuditLogEntryDto Entry,
    string? UserAgent,
    string? HttpMethod,
    string? Path,
    IReadOnlyList<AuditFieldChangeDto> Changes,
    IReadOnlyDictionary<string, string?> Metadata,
    IReadOnlyList<AuditLogEntryDto> Related);

public sealed record AuditFacetsDto(IReadOnlyList<string> EntityTypes, IReadOnlyList<string> Actions);
