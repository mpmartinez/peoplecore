using System.Globalization;
using System.Text;

namespace PeopleCore.Web.Services;

/// <summary>How the audit trail pages word what the API sends.</summary>
public static class AuditLabels
{
    private static readonly TimeSpan Manila = TimeSpan.FromHours(8);

    public static readonly IReadOnlyList<(string Value, string Label)> Categories =
    [
        ("DataChange", "Data change"),
        ("Security", "Security"),
        ("Access", "Access"),
        ("Action", "Action"),
    ];

    /// <summary>An entry's time is a real UTC instant; it is shown as Philippine time.</summary>
    public static string When(DateTime occurredAtUtc) =>
        (DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc) + Manila)
            .ToString("MMM d, yyyy h:mm:ss tt", CultureInfo.InvariantCulture);

    public static string Category(string category) =>
        Categories.FirstOrDefault(c => c.Value == category).Label ?? category;

    public static string CategoryVariant(string category) => category switch
    {
        "Security" => "warning",
        "Access" => "accent",
        "Action" => "secondary",
        _ => "outline",
    };

    /// <summary>Who did it: the account's email, or what acted without one (a job, an anonymous caller).</summary>
    public static string Actor(AuditLogEntryDto entry) => entry.UserEmail ?? entry.ActorName ?? "Unknown";

    /// <summary>The summary the API wrote, or the action's own name when there is none.</summary>
    public static string What(AuditLogEntryDto entry) => entry.Summary ?? Humanize(entry.Action);

    /// <summary><c>BasicSalary</c> as "Basic salary"; <c>auth.login-failed</c> as "auth login failed".</summary>
    public static string Humanize(string name)
    {
        var text = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c is '.' or '-' or '_') { text.Append(' '); continue; }
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1])) text.Append(' ').Append(char.ToLowerInvariant(c));
            else text.Append(c);
        }
        var result = text.ToString();
        return result.Length == 0 ? result : char.ToUpperInvariant(result[0]) + result[1..];
    }
}
