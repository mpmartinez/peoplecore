using System.Globalization;
using System.Text;
using System.Text.Json;
using PeopleCore.Application.Auditing.DTOs;
using PeopleCore.Application.Auditing.Interfaces;
using PeopleCore.Application.Common.DTOs;
using PeopleCore.Domain.Entities.System;

namespace PeopleCore.Application.Auditing.Services;

public class AuditLogQueryService : IAuditLogQueryService
{
    public const int MaxPageSize = 100;
    public const int MaxExportRows = 50_000;

    private readonly IAuditLogRepository _repository;

    public AuditLogQueryService(IAuditLogRepository repository) => _repository = repository;

    public async Task<PagedResult<AuditLogEntryDto>> SearchAsync(AuditLogFilter filter, CancellationToken ct = default)
    {
        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, MaxPageSize);

        var (items, total) = await _repository.FindAsync(filter, (page - 1) * pageSize, pageSize, ct);
        return PagedResult<AuditLogEntryDto>.Create(await ToEntriesAsync(items, ct), total, page, pageSize);
    }

    public async Task<AuditLogDetailDto?> GetAsync(long id, CancellationToken ct = default)
    {
        var log = await _repository.GetAsync(id, ct);
        if (log is null) return null;

        var related = log.CorrelationId is null
            ? []
            : await _repository.GetByCorrelationAsync(log.CorrelationId, log.Id, ct);
        var entries = await ToEntriesAsync([log, .. related], ct);

        return new AuditLogDetailDto(
            entries[0], log.UserAgent, log.HttpMethod, log.Path,
            ReadChanges(log.Changes), ReadMetadata(log.Metadata), entries.Skip(1).ToList());
    }

    public Task<AuditFacetsDto> GetFacetsAsync(CancellationToken ct = default) => _repository.GetFacetsAsync(ct);

    public async Task<byte[]> ExportCsvAsync(AuditLogFilter filter, CancellationToken ct = default)
    {
        var (items, _) = await _repository.FindAsync(filter, 0, MaxExportRows, ct);
        var names = await _repository.GetEmployeeNamesAsync(SubjectsOf(items), ct);

        var csv = new StringBuilder();
        csv.AppendLine(string.Join(',',
            "Id", "OccurredAtUtc", "Category", "Action", "Summary", "Outcome", "FailureReason",
            "UserId", "UserEmail", "Actor", "IpAddress", "UserAgent", "Method", "Path", "StatusCode",
            "EntityType", "EntityId", "EmployeeId", "Employee", "CorrelationId", "Changes", "Metadata"));
        foreach (var log in items)
        {
            csv.AppendLine(string.Join(',', new[]
            {
                log.Id.ToString(CultureInfo.InvariantCulture),
                log.OccurredAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                log.Category.ToString(), log.Action, log.Summary, log.Outcome.ToString(), log.FailureReason,
                log.UserId, log.UserEmail, log.ActorName, log.IpAddress, log.UserAgent, log.HttpMethod, log.Path,
                log.StatusCode?.ToString(CultureInfo.InvariantCulture),
                log.EntityType, log.EntityId, log.SubjectEmployeeId?.ToString(),
                log.SubjectEmployeeId is { } e && names.TryGetValue(e, out var name) ? name : null,
                log.CorrelationId, log.Changes, log.Metadata,
            }.Select(Escape)));
        }

        // A byte-order mark so Excel reads the file as UTF-8 - names carry ñ.
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    private async Task<List<AuditLogEntryDto>> ToEntriesAsync(IReadOnlyList<AuditLog> logs, CancellationToken ct)
    {
        var names = await _repository.GetEmployeeNamesAsync(SubjectsOf(logs), ct);
        return logs.Select(log => new AuditLogEntryDto(
            log.Id, log.OccurredAt, log.Category, log.Action, log.Summary, log.Outcome, log.FailureReason,
            log.UserId, log.UserEmail, log.ActorName, log.IpAddress, log.EntityType, log.EntityId,
            log.SubjectEmployeeId,
            log.SubjectEmployeeId is { } id && names.TryGetValue(id, out var name) ? name : null,
            log.StatusCode, log.CorrelationId, CountChanges(log.Changes))).ToList();
    }

    private static IEnumerable<Guid> SubjectsOf(IEnumerable<AuditLog> logs) =>
        logs.Where(l => l.SubjectEmployeeId is not null).Select(l => l.SubjectEmployeeId!.Value).Distinct();

    private static int CountChanges(string? changes)
    {
        if (string.IsNullOrEmpty(changes)) return 0;
        using var document = JsonDocument.Parse(changes);
        return document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.GetArrayLength() : 0;
    }

    internal static IReadOnlyList<AuditFieldChangeDto> ReadChanges(string? changes)
    {
        if (string.IsNullOrEmpty(changes)) return [];
        using var document = JsonDocument.Parse(changes);
        if (document.RootElement.ValueKind != JsonValueKind.Array) return [];
        return document.RootElement.EnumerateArray()
            .Select(c => new AuditFieldChangeDto(
                c.TryGetProperty("field", out var field) ? field.GetString() ?? "" : "",
                c.TryGetProperty("old", out var old) ? Text(old) : null,
                c.TryGetProperty("new", out var @new) ? Text(@new) : null))
            .ToList();
    }

    internal static IReadOnlyDictionary<string, string?> ReadMetadata(string? metadata)
    {
        if (string.IsNullOrEmpty(metadata)) return new Dictionary<string, string?>();
        using var document = JsonDocument.Parse(metadata);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return new Dictionary<string, string?>();
        return document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => Text(p.Value));
    }

    /// <summary>A value as the page shows it: strings as they are, lists as "a, b", the rest as JSON.</summary>
    private static string? Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Array when value.EnumerateArray().All(v => v.ValueKind is JsonValueKind.String or JsonValueKind.Number) =>
            string.Join(", ", value.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText())),
        _ => value.GetRawText(),
    };

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        // A leading =, +, - or @ is a formula to a spreadsheet; the trail holds whatever was typed.
        if (value[0] is '=' or '+' or '-' or '@') value = "'" + value;
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
