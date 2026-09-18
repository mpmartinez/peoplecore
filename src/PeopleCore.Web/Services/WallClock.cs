using System.Globalization;

namespace PeopleCore.Web.Services;

/// <summary>
/// Attendance times arrive as Philippine wall-clock time labelled UTC: <c>2026-03-12T08:07:00Z</c>
/// means 08:07 in Manila. They are shown exactly as written - converting them to the browser's zone
/// would add eight hours for everyone in the Philippines.
/// </summary>
public static class WallClock
{
    /// <summary>The <c>HH:mm</c> of <paramref name="value"/>, or the value unchanged if it is not a date-time.</summary>
    public static string? Time(string? value)
        => value is not null
           && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToString("HH:mm", CultureInfo.InvariantCulture)
            : value;
}
