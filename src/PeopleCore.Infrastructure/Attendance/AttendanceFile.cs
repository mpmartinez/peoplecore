using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;
using ExcelDataReader;
using PeopleCore.Application.Attendance.DTOs;

namespace PeopleCore.Infrastructure.Attendance;

/// <summary>The two shapes of attendance file PeopleCore reads.</summary>
public enum AttendanceFileLayout
{
    /// <summary>PeopleCore's own: one row per person per day, <c>employee_number,date,time_in,time_out</c>.</summary>
    DailyInOut,

    /// <summary>A time clock's log (ZKTeco and the like): one row per fingerprint or card scan.</summary>
    ScanLog,
}

/// <summary>
/// What <see cref="AttendanceFile.Parse"/> read. Each punch's <see cref="AttendancePunchDto.EmployeeNumber"/>
/// holds the person's id <b>as the file wrote it</b> - an employee number, or the device's enrolment
/// number - for the import to resolve to an employee.
/// </summary>
public sealed record AttendanceFileParseResult(
    AttendanceFileLayout? Layout,
    IReadOnlyList<AttendancePunchDto> Punches,
    IReadOnlyList<string> Errors);

/// <summary>
/// Reads an attendance file from a client's time clock or from PeopleCore's own template: CSV (comma,
/// semicolon or tab separated), ZKTeco's <c>attlog.dat</c>/<c>.txt</c> download, or an Excel sheet
/// (<c>.xls</c> or <c>.xlsx</c>, first sheet).
/// <para>
/// A scan log is reduced to a day's first scan as the time-in and its last as the time-out. The
/// in/out state the device records is ignored: people press the wrong key often enough that it can't
/// be trusted, and the first and last scan of the day is what payroll wants. A shift that crosses
/// midnight is therefore read as two days.
/// </para>
/// <para>
/// A punch is the <b>wall-clock time at the site, labelled UTC</b>: <c>08:02</c> becomes <c>08:02Z</c>,
/// not shifted by any zone. That is the convention <c>POST api/attendance/sync</c> follows and the one
/// lateness and undertime are computed by (read straight off the time of day), and the only kind
/// Npgsql will write to a <c>timestamp with time zone</c> column.
/// </para>
/// <para>
/// Dates written with slashes are read month first (<c>03/10/2026</c> is 10 March), as ZKTeco's
/// software writes them, unless some date in the file only makes sense day first (<c>25/03/2026</c>).
/// </para>
/// </summary>
public static class AttendanceFile
{
    /// <summary>Header names a time clock uses for the person's enrolment number, most specific first.</summary>
    private static readonly string[] IdColumns =
    [
        "acno", "enrollno", "enrollnumber", "enrolmentno", "userid", "userno", "personid", "personnelid",
        "employeeid", "employeeno", "employeenumber", "empid", "empno", "badgenumber", "badgeno", "cardno", "id", "no",
    ];

    /// <summary>Header names for the moment of the scan, most specific first.</summary>
    private static readonly string[] TimeColumns =
    [
        "datetime", "punchtime", "checktime", "atttime", "attendancetime", "verifytime", "clocktime", "timestamp", "time",
    ];

    private static readonly string[] Delimiters = [",", ";", "\t"];

    private static readonly Regex SlashDate = new(@"^\s*(\d{1,2})[/.-](\d{1,2})[/.-](\d{4})", RegexOptions.Compiled);

    public static AttendanceFileParseResult Parse(Stream stream, string fileName)
    {
        var isExcel = Path.GetExtension(fileName).ToLowerInvariant() is ".xls" or ".xlsx";
        List<TableRow> rows;
        try
        {
            rows = isExcel ? ReadExcel(stream) : ReadText(stream);
        }
        catch (Exception ex) when (ex is ExcelDataReader.Exceptions.ExcelReaderException or InvalidDataException or CsvHelperException)
        {
            return Refused($"The file could not be read as {(isExcel ? "an Excel workbook" : "text")}: {ex.Message}");
        }

        rows = rows.Where(r => r.Cells.Any(c => !string.IsNullOrWhiteSpace(c))).ToList();
        if (rows.Count == 0)
            return Refused("The file is empty.");

        // A spreadsheet export often starts with a title and a date range before the header row.
        foreach (var (row, index) in rows.Take(15).Select((r, i) => (r, i)))
        {
            var body = rows.Skip(index + 1).ToList();

            if (DailyColumns(row.Cells) is { } daily)
                return ParseDaily(body, daily);

            if (ScanColumns(row.Cells.Select(Normalize).ToArray()) is { } scan)
                return ParseScans(body, scan.Id, scan.Date, scan.Time);
        }

        // ZKTeco's attlog download has no header: id, date-time, then device codes.
        if (rows.Take(5).All(r => r.Cells.Length >= 2 && !string.IsNullOrWhiteSpace(r.Cells[0]) && LooksLikeDateTime(r.Cells[1])))
            return ParseScans(rows, id: 0, date: null, time: 1);

        return Refused(
            "The file's columns weren't recognised. Use PeopleCore's layout (employee_number,date,time_in,time_out) " +
            "or a time clock's scan log with each person's number (for example AC-No. or User ID) and the time of each scan.");
    }

    // ── Reading the file into rows ─────────────────────────────────────────────────────────────

    private sealed record TableRow(int Line, string[] Cells);

    private static List<TableRow> ReadText(Stream stream)
    {
        // UTF-8 with or without a BOM, or UTF-16 as some Windows time clock software saves it.
        using var decoder = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = decoder.ReadToEnd();

        var sample = text.Split('\n').Where(l => l.Trim().Length > 0).Take(5).ToList();
        var delimiter = Delimiters.MaxBy(d => sample.Count == 0 ? 0 : sample.Min(l => l.Split(d).Length)) ?? ",";

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter,
            HasHeaderRecord = false,
            BadDataFound = null,
            MissingFieldFound = null,
            IgnoreBlankLines = false,
            DetectColumnCountChanges = false,
        };

        using var csv = new CsvReader(new StringReader(text), config);
        var rows = new List<TableRow>();
        while (csv.Read())
            rows.Add(new TableRow(csv.Parser.Row, csv.Parser.Record?.Select(c => c.Trim()).ToArray() ?? []));
        return rows;
    }

    private static List<TableRow> ReadExcel(Stream stream)
    {
        // Old .xls workbooks name their text encoding by Windows code page.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        using var buffered = new MemoryStream();
        stream.CopyTo(buffered);
        buffered.Position = 0;

        using var reader = ExcelReaderFactory.CreateReader(buffered);
        var rows = new List<TableRow>();
        var line = 0;
        while (reader.Read())
        {
            line++;
            var cells = new string[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
                cells[i] = CellText(reader.GetValue(i));
            rows.Add(new TableRow(line, cells));
        }
        return rows;
    }

    /// <summary>
    /// A cell as text in the invariant forms the parsers below read. Excel keeps a time-only cell as a
    /// moment on 30 or 31 December 1899, and a date-only cell as midnight.
    /// </summary>
    private static string CellText(object? value) => value switch
    {
        null => "",
        DateTime d when d.Year < 1901 => d.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
        DateTime d when d.TimeOfDay == TimeSpan.Zero => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime d => d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        TimeSpan t => t.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture),
        double n when n == Math.Floor(n) => n.ToString("0", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture).Trim(),
        _ => value.ToString()?.Trim() ?? "",
    };

    // ── Recognising the layout ─────────────────────────────────────────────────────────────────

    private static string Normalize(string cell) =>
        new(cell.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private sealed record DailyIndexes(int EmployeeNumber, int Date, int TimeIn, int TimeOut);

    private static DailyIndexes? DailyColumns(string[] cells)
    {
        var names = cells.Select(c => c.Trim().ToLowerInvariant()).ToList();
        var (number, date, timeIn, timeOut) =
            (names.IndexOf("employee_number"), names.IndexOf("date"), names.IndexOf("time_in"), names.IndexOf("time_out"));
        return number >= 0 && date >= 0 && timeIn >= 0 && timeOut >= 0 ? new DailyIndexes(number, date, timeIn, timeOut) : null;
    }

    private static (int Id, int? Date, int Time)? ScanColumns(string[] names)
    {
        var id = IdColumns.Select(n => Array.IndexOf(names, n)).FirstOrDefault(i => i >= 0, -1);
        if (id < 0) return null;

        var time = TimeColumns.Select(n => Array.IndexOf(names, n)).FirstOrDefault(i => i >= 0 && i != id, -1);
        var date = Array.IndexOf(names, "date");

        if (time >= 0) return (id, date >= 0 && date != time ? date : null, time);
        if (date >= 0 && date != id) return (id, null, date); // a single "Date" column holding the whole moment
        return null;
    }

    // ── PeopleCore's daily layout ──────────────────────────────────────────────────────────────

    private static readonly string[] DailyTimeFormats = ["HH:mm", "HH:mm:ss"];

    private static AttendanceFileParseResult ParseDaily(List<TableRow> rows, DailyIndexes at)
    {
        var punches = new List<AttendancePunchDto>();
        var errors = new List<string>();

        foreach (var row in rows)
        {
            string Cell(int i) => i < row.Cells.Length ? row.Cells[i].Trim() : "";
            var (number, dateText, inText, outText) = (Cell(at.EmployeeNumber), Cell(at.Date), Cell(at.TimeIn), Cell(at.TimeOut));

            if (number.Length == 0 || dateText.Length == 0)
                continue;

            if (!DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                errors.Add($"Row {row.Line}: '{dateText}' is not a date in yyyy-MM-dd form.");
                continue;
            }

            // The whole row is refused if either time is bad: keeping a good time_out after dropping a
            // bad time_in would record the time-out as that day's time-in.
            if (!TryReadDailyTime(inText, out var timeIn))
            {
                errors.Add($"Row {row.Line}: time_in '{inText}' is not a time in HH:mm or HH:mm:ss form.");
                continue;
            }
            if (!TryReadDailyTime(outText, out var timeOut))
            {
                errors.Add($"Row {row.Line}: time_out '{outText}' is not a time in HH:mm or HH:mm:ss form.");
                continue;
            }

            if (timeIn is { } i) punches.Add(new AttendancePunchDto(number, date.ToDateTime(i, DateTimeKind.Utc)));
            if (timeOut is { } o) punches.Add(new AttendancePunchDto(number, date.ToDateTime(o, DateTimeKind.Utc)));
        }

        return new AttendanceFileParseResult(AttendanceFileLayout.DailyInOut, punches, errors);
    }

    /// <summary>A blank field is no punch (and not an error); anything else must be a time.</summary>
    private static bool TryReadDailyTime(string text, out TimeOnly? time)
    {
        time = null;
        if (text.Length == 0) return true;
        if (!TimeOnly.TryParseExact(text, DailyTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return false;
        time = parsed;
        return true;
    }

    // ── A time clock's scan log ────────────────────────────────────────────────────────────────

    private static AttendanceFileParseResult ParseScans(List<TableRow> rows, int id, int? date, int time)
    {
        string Cell(TableRow row, int i) => i < row.Cells.Length ? row.Cells[i].Trim() : "";

        string Moment(TableRow row)
        {
            var timeText = Cell(row, time);
            // A separate date column is joined on unless the time column already carries the date.
            return date is { } d && !LooksLikeDateTime(timeText) ? $"{Cell(row, d)} {timeText}".Trim() : timeText;
        }

        if (DayFirst(rows.Select(Moment)) is not { } dayFirst)
            return Refused("The dates mix day-first and month-first forms (for example 25/03 and 03/25), so none can be trusted.");

        var errors = new List<string>();
        var scans = new List<(string Person, DateTime At)>();
        foreach (var row in rows)
        {
            var person = Cell(row, id);
            var moment = Moment(row);
            if (person.Length == 0 && moment.Length == 0) continue;
            if (person.Length == 0)
            {
                errors.Add($"Row {row.Line}: no person number.");
                continue;
            }
            if (!TryReadMoment(moment, dayFirst, out var at))
            {
                errors.Add($"Row {row.Line}: '{moment}' is not a date and time this import can read.");
                continue;
            }
            scans.Add((person, at));
        }

        // First scan of the day in, last scan out. A lone scan is a time-in with no time-out yet.
        var punches = scans
            .GroupBy(s => (s.Person, Day: DateOnly.FromDateTime(s.At)))
            .OrderBy(g => g.Key.Day).ThenBy(g => g.Key.Person, StringComparer.Ordinal)
            .SelectMany(g =>
            {
                var first = g.Min(s => s.At);
                var last = g.Max(s => s.At);
                return last > first
                    ? new[] { new AttendancePunchDto(g.Key.Person, first), new AttendancePunchDto(g.Key.Person, last) }
                    : [new AttendancePunchDto(g.Key.Person, first)];
            })
            .ToList();

        return new AttendanceFileParseResult(AttendanceFileLayout.ScanLog, punches, errors);
    }

    /// <summary>
    /// Whether the file's slash dates are day first: false (month first) unless some date needs it,
    /// null when the file contradicts itself.
    /// </summary>
    private static bool? DayFirst(IEnumerable<string> moments)
    {
        bool dayFirst = false, monthFirst = false;
        foreach (var match in moments.Select(m => SlashDate.Match(m)).Where(m => m.Success))
        {
            if (int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) > 12) dayFirst = true;
            if (int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) > 12) monthFirst = true;
        }
        return dayFirst && monthFirst ? null : dayFirst;
    }

    private static readonly string[] TimeParts = ["H:mm:ss", "H:mm", "h:mm:ss tt", "h:mm tt", "h:mm:sstt", "h:mmtt"];

    private static readonly string[] IsoMomentFormats =
    [
        .. TimeParts.Select(t => $"yyyy-MM-dd {t}"),
        .. TimeParts.Select(t => $"yyyy/MM/dd {t}"),
        "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm",
    ];

    private static readonly string[] MonthFirstFormats =
        [.. new[] { "M/d/yyyy", "M-d-yyyy", "M.d.yyyy" }.SelectMany(d => TimeParts.Select(t => $"{d} {t}"))];

    private static readonly string[] DayFirstFormats =
        [.. new[] { "d/M/yyyy", "d-M-yyyy", "d.M.yyyy" }.SelectMany(d => TimeParts.Select(t => $"{d} {t}"))];

    private static bool TryReadMoment(string text, bool dayFirst, out DateTime at)
    {
        var cleaned = Regex.Replace(text.Trim(), @"\s+", " ");
        var ok = DateTime.TryParseExact(cleaned, IsoMomentFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            || DateTime.TryParseExact(cleaned, dayFirst ? DayFirstFormats : MonthFirstFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed);
        at = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        return ok;
    }

    private static bool LooksLikeDateTime(string text) =>
        TryReadMoment(text, dayFirst: false, out _) || TryReadMoment(text, dayFirst: true, out _);

    private static AttendanceFileParseResult Refused(string error) => new(null, [], [error]);
}
