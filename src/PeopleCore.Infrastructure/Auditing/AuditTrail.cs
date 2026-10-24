using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PeopleCore.Application.Common.Auditing;
using PeopleCore.Domain.Entities.System;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Auditing;

/// <summary>
/// Writes explicit audit entries through a context of its own, so an entry neither waits on nor
/// flushes the request's change tracker, and survives the request failing.
/// </summary>
public class AuditTrail : IAuditTrail
{
    private const int MaxSummaryLength = 500;
    private const int MaxReasonLength = 1000;

    private readonly DbContextOptions<AppDbContext> _options;
    private readonly IAuditContext _context;
    private readonly ILogger<AuditTrail> _log;

    public AuditTrail(DbContextOptions<AppDbContext> options, IAuditContext context, ILogger<AuditTrail> log)
    {
        _options = options;
        _context = context;
        _log = log;
    }

    public async Task RecordAsync(AuditEvent auditEvent, CancellationToken ct = default)
    {
        var log = AuditActor.Stamp(new AuditLog
        {
            Category = auditEvent.Category,
            Action = auditEvent.Action,
            Summary = Truncate(auditEvent.Summary, MaxSummaryLength),
            Outcome = auditEvent.Outcome,
            FailureReason = Truncate(auditEvent.FailureReason, MaxReasonLength),
            EntityType = auditEvent.EntityType,
            EntityId = auditEvent.EntityId,
            SubjectEmployeeId = auditEvent.SubjectEmployeeId,
            StatusCode = auditEvent.StatusCode,
            Metadata = AuditJson.Serialize(auditEvent.Metadata),
            UserId = auditEvent.UserId,
            UserEmail = auditEvent.UserEmail,
        }, _context);

        try
        {
            // Not the request's token: an entry about a request the caller abandoned is still kept.
            await using var db = new AppDbContext(_options);
            db.AuditLogs.Add(log);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Could not write the audit entry {Action} ({Category}).", log.Action, log.Category);
        }
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
