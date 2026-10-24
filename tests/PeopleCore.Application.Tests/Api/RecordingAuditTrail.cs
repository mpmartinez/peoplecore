using PeopleCore.Application.Common.Auditing;

namespace PeopleCore.Application.Tests.Api;

/// <summary>An audit trail that keeps what it is given, for tests to read back.</summary>
public sealed class RecordingAuditTrail : IAuditTrail
{
    public List<AuditEvent> Events { get; } = [];

    public Task RecordAsync(AuditEvent auditEvent, CancellationToken ct = default)
    {
        Events.Add(auditEvent);
        return Task.CompletedTask;
    }
}
