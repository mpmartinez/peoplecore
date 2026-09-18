using System.Globalization;
using CsvHelper;
using PeopleCore.Application.Attendance.DTOs;

namespace PeopleCore.Infrastructure.Attendance;

/// <summary>
/// What <see cref="AttendanceCsv.Parse"/> read: the punches from every good row, and one message
/// for each row it refused. A refused row contributes no punches at all.
/// </summary>
public sealed record AttendanceCsvParseResult(
    IReadOnlyList<AttendancePunchDto> Punches,
    IReadOnlyList<string> Errors);

/// <summary>
/// Reads the attendance import file: a header <c>employee_number,date,time_in,time_out</c>, then
/// rows such as <c>EMP-001,2026-03-10,08:02,17:05</c>.
/// <para>
/// A punch is the <b>wall-clock time at the site, labelled UTC</b> - <c>08:02</c> becomes
/// <c>08:02Z</c>, not shifted by any zone. That is the convention <c>POST api/attendance/sync</c>
/// already follows and the one <see cref="Application.Attendance.Services.AttendanceService"/>
/// computes lateness and undertime by (it reads the time of day straight off the value). It is
/// also the only kind Npgsql will write to a <c>timestamp with time zone</c> column: an
/// Unspecified punch fails the save, which is how every imported row used to come back "skipped".
/// </para>
/// <para>
/// Dates and times are read with the invariant culture in fixed forms (<c>yyyy-MM-dd</c>,
/// <c>HH:mm</c> or <c>HH:mm:ss</c>), so the server's culture cannot change what a row means.
/// A row without an employee number or a date is passed over silently, as blank and trailing
/// lines are; a row whose date or time cannot be read is refused with a message naming its line.
/// </para>
/// </summary>
public static class AttendanceCsv
{
    private const string EmployeeNumberColumn = "employee_number";
    private const string DateColumn = "date";
    private const string TimeInColumn = "time_in";
    private const string TimeOutColumn = "time_out";

    private static readonly string[] Columns = [EmployeeNumberColumn, DateColumn, TimeInColumn, TimeOutColumn];
    private static readonly string[] TimeFormats = ["HH:mm", "HH:mm:ss"];

    public static AttendanceCsvParseResult Parse(Stream stream)
    {
        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        var punches = new List<AttendancePunchDto>();
        var errors = new List<string>();

        if (!csv.Read() || !csv.ReadHeader())
            return new AttendanceCsvParseResult(punches, ["The file is empty."]);

        var header = csv.HeaderRecord ?? [];
        var missing = Columns.Where(c => !header.Contains(c, StringComparer.Ordinal)).ToList();
        if (missing.Count > 0)
        {
            errors.Add($"The header is missing {string.Join(", ", missing)}; expected {string.Join(",", Columns)}.");
            return new AttendanceCsvParseResult(punches, errors);
        }

        while (csv.Read())
        {
            var line = csv.Parser.Row;
            var employeeNumber = csv.GetField(EmployeeNumberColumn)?.Trim();
            var dateText = csv.GetField(DateColumn)?.Trim();
            var timeInText = csv.GetField(TimeInColumn)?.Trim();
            var timeOutText = csv.GetField(TimeOutColumn)?.Trim();

            if (string.IsNullOrEmpty(employeeNumber) || string.IsNullOrEmpty(dateText))
                continue;

            if (!DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                errors.Add($"Row {line}: '{dateText}' is not a date in yyyy-MM-dd form.");
                continue;
            }

            // The whole row is refused if either time is bad: keeping a good time_out after
            // dropping a bad time_in would record the time-out as that day's time-in.
            if (!TryReadTime(timeInText, out var timeIn))
            {
                errors.Add($"Row {line}: {TimeInColumn} '{timeInText}' is not a time in HH:mm or HH:mm:ss form.");
                continue;
            }
            if (!TryReadTime(timeOutText, out var timeOut))
            {
                errors.Add($"Row {line}: {TimeOutColumn} '{timeOutText}' is not a time in HH:mm or HH:mm:ss form.");
                continue;
            }

            if (timeIn is { } inTime)
                punches.Add(new AttendancePunchDto(employeeNumber, date.ToDateTime(inTime, DateTimeKind.Utc)));
            if (timeOut is { } outTime)
                punches.Add(new AttendancePunchDto(employeeNumber, date.ToDateTime(outTime, DateTimeKind.Utc)));
        }

        return new AttendanceCsvParseResult(punches, errors);
    }

    /// <summary>A blank field is no punch (and not an error); anything else must be a time.</summary>
    private static bool TryReadTime(string? text, out TimeOnly? time)
    {
        time = null;
        if (string.IsNullOrEmpty(text))
            return true;

        if (!TimeOnly.TryParseExact(text, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return false;

        time = parsed;
        return true;
    }
}
