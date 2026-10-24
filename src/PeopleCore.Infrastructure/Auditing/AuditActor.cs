using PeopleCore.Application.Common.Auditing;
using PeopleCore.Domain.Entities.System;

namespace PeopleCore.Infrastructure.Auditing;

/// <summary>Copies who is acting, and the request they act through, onto an audit entry.</summary>
internal static class AuditActor
{
    public static AuditLog Stamp(AuditLog log, IAuditContext? context)
    {
        if (context is null)
        {
            log.ActorName ??= "System";
            return log;
        }

        log.UserId ??= context.UserId;
        log.UserEmail ??= context.UserEmail;
        log.IpAddress ??= context.IpAddress;
        log.UserAgent ??= context.UserAgent;
        log.CorrelationId ??= context.CorrelationId;
        log.HttpMethod ??= context.HttpMethod;
        log.Path ??= Truncate(context.Path, 500);

        if (log.UserId is null)
        {
            // No one is signed in: a job (it names itself), the startup seeder (no request at
            // all), or an anonymous call such as a sign-in or a careers application.
            log.ActorName ??= context.SystemActor is { } job ? $"System: {job}"
                : context.CorrelationId is null ? "System"
                : "Anonymous";
        }

        return log;
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
