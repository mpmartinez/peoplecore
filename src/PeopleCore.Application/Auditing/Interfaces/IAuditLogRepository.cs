using PeopleCore.Application.Auditing.DTOs;
using PeopleCore.Domain.Entities.System;

namespace PeopleCore.Application.Auditing.Interfaces;

/// <summary>Reads the audit trail. There is nothing to write here: entries are only ever added on save.</summary>
public interface IAuditLogRepository
{
    /// <summary>Newest first. <paramref name="take"/> null takes every match after <paramref name="skip"/>.</summary>
    Task<(IReadOnlyList<AuditLog> Items, int TotalCount)> FindAsync(
        AuditLogFilter filter, int skip, int? take, CancellationToken ct = default);

    Task<AuditLog?> GetAsync(long id, CancellationToken ct = default);

    /// <summary>The other entries the same request wrote, oldest first.</summary>
    Task<IReadOnlyList<AuditLog>> GetByCorrelationAsync(string correlationId, long exceptId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, string>> GetEmployeeNamesAsync(IEnumerable<Guid> employeeIds, CancellationToken ct = default);

    Task<AuditFacetsDto> GetFacetsAsync(CancellationToken ct = default);
}

public interface IAuditLogQueryService
{
    Task<PeopleCore.Application.Common.DTOs.PagedResult<AuditLogEntryDto>> SearchAsync(AuditLogFilter filter, CancellationToken ct = default);
    Task<AuditLogDetailDto?> GetAsync(long id, CancellationToken ct = default);
    Task<AuditFacetsDto> GetFacetsAsync(CancellationToken ct = default);

    /// <summary>Every entry the filter matches, newest first, as CSV - at most <c>MaxExportRows</c>.</summary>
    Task<byte[]> ExportCsvAsync(AuditLogFilter filter, CancellationToken ct = default);
}
