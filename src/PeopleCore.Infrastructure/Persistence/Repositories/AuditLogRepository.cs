using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Auditing.DTOs;
using PeopleCore.Application.Auditing.Interfaces;
using PeopleCore.Application.Common.Time;
using PeopleCore.Domain.Entities.System;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class AuditLogRepository : IAuditLogRepository
{
    private readonly AppDbContext _context;

    public AuditLogRepository(AppDbContext context) => _context = context;

    public async Task<(IReadOnlyList<AuditLog> Items, int TotalCount)> FindAsync(
        AuditLogFilter filter, int skip, int? take, CancellationToken ct = default)
    {
        var query = _context.AuditLogs.AsNoTracking();

        // The dates are Philippine calendar days; OccurredAt is a real UTC instant.
        if (filter.From is { } from)
        {
            var start = DateTime.SpecifyKind(from.ToDateTime(TimeOnly.MinValue) - PhilippineTime.UtcOffset, DateTimeKind.Utc);
            query = query.Where(a => a.OccurredAt >= start);
        }
        if (filter.To is { } to)
        {
            var end = DateTime.SpecifyKind(to.AddDays(1).ToDateTime(TimeOnly.MinValue) - PhilippineTime.UtcOffset, DateTimeKind.Utc);
            query = query.Where(a => a.OccurredAt < end);
        }

        if (filter.Category is { } category) query = query.Where(a => a.Category == category);
        if (filter.Outcome is { } outcome) query = query.Where(a => a.Outcome == outcome);
        if (!string.IsNullOrWhiteSpace(filter.Action)) query = query.Where(a => a.Action == filter.Action);
        if (!string.IsNullOrWhiteSpace(filter.EntityType)) query = query.Where(a => a.EntityType == filter.EntityType);
        if (!string.IsNullOrWhiteSpace(filter.EntityId)) query = query.Where(a => a.EntityId == filter.EntityId);
        if (filter.EmployeeId is { } employee) query = query.Where(a => a.SubjectEmployeeId == employee);
        if (!string.IsNullOrWhiteSpace(filter.CorrelationId)) query = query.Where(a => a.CorrelationId == filter.CorrelationId);

        if (!string.IsNullOrWhiteSpace(filter.User))
        {
            var user = $"%{filter.User.Trim()}%";
            query = query.Where(a =>
                EF.Functions.ILike(a.UserEmail ?? "", user)
                || EF.Functions.ILike(a.ActorName ?? "", user)
                || a.UserId == filter.User.Trim());
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = $"%{filter.Search.Trim()}%";
            query = query.Where(a =>
                EF.Functions.ILike(a.Summary ?? "", search)
                || EF.Functions.ILike(a.Action, search)
                || EF.Functions.ILike(a.EntityId ?? "", search)
                || EF.Functions.ILike(a.Path ?? "", search)
                || EF.Functions.ILike(a.IpAddress ?? "", search)
                || EF.Functions.ILike(a.FailureReason ?? "", search));
        }

        var total = await query.CountAsync(ct);
        var page = query.OrderByDescending(a => a.Id).Skip(skip);
        if (take is { } count) page = page.Take(count);

        return (await page.ToListAsync(ct), total);
    }

    public Task<AuditLog?> GetAsync(long id, CancellationToken ct = default) =>
        _context.AuditLogs.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct);

    public async Task<IReadOnlyList<AuditLog>> GetByCorrelationAsync(string correlationId, long exceptId, CancellationToken ct = default) =>
        await _context.AuditLogs.AsNoTracking()
            .Where(a => a.CorrelationId == correlationId && a.Id != exceptId)
            .OrderBy(a => a.Id)
            .Take(200)
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, string>> GetEmployeeNamesAsync(IEnumerable<Guid> employeeIds, CancellationToken ct = default)
    {
        var ids = employeeIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, string>();

        return await _context.Employees.AsNoTracking()
            .Where(e => ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.FirstName + " " + e.LastName, ct);
    }

    public async Task<AuditFacetsDto> GetFacetsAsync(CancellationToken ct = default)
    {
        var entityTypes = await _context.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType != null)
            .Select(a => a.EntityType!)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync(ct);
        var actions = await _context.AuditLogs.AsNoTracking()
            .Select(a => a.Action)
            .Distinct()
            .OrderBy(a => a)
            .ToListAsync(ct);
        return new AuditFacetsDto(entityTypes, actions);
    }
}
