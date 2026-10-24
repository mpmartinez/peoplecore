using System.Text.Json;
using System.Text.Json.Serialization;

namespace PeopleCore.Application.Common.Auditing;

/// <summary>One field of a data change. <c>Old</c> is null for an insert, <c>New</c> for a delete.</summary>
public sealed record AuditFieldChange(string Field, object? Old, object? New);

/// <summary>How audit entries serialize their changes and metadata.</summary>
public static class AuditJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string? Serialize(object? value) =>
        value is null ? null : JsonSerializer.Serialize(value, Options);
}
