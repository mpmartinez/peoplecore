using System.Globalization;
using System.Text;
using PeopleCore.Application.Payroll.GovernmentReports;

namespace PeopleCore.Application.Payroll.OpeningBalances;

/// <summary>
/// The opening-balance CSV: the template (just the header row) and reading a filled-in file into
/// rows. Reading only parses cells; whether a row's figures are acceptable is the service's
/// validation, the same the form gets.
/// </summary>
/// <remarks>
/// The header is row 1, so the first data row is row 2. Dates are <c>yyyy-MM-dd</c>; numbers use a
/// dot and no thousands separator; a blank numeric cell is 0. A line whose cells are all blank is
/// skipped but still counted, so row numbers match the spreadsheet's.
/// </remarks>
public static class OpeningBalanceCsv
{
    public const string ContentType = "text/csv";
    public const string TemplateFileName = "opening-balances-template.csv";

    /// <summary>The largest file the import reads.</summary>
    public const long MaxFileBytes = 2 * 1024 * 1024;

    /// <summary>The refusal for no file, or one over <see cref="MaxFileBytes"/>.</summary>
    public const string FileRefusal = "Choose a CSV file of at most 2 MB.";

    public const string DateFormat = "yyyy-MM-dd";

    /// <summary>The template's columns, in order.</summary>
    public static IReadOnlyList<string> Columns { get; } =
    [
        "EmployeeNumber", "Year", "ThroughDate", "BasicSalary", "ThirteenthMonthPaid", "OtherBenefitsPaid",
        "OtherTaxablePay", "DeMinimis", "OtherNonTaxable", "EmployeeContributions", "TaxWithheld", "DeMinimisLeaveDays",
    ];

    /// <summary>The columns from BasicSalary on, each read as a number where blank is 0.</summary>
    private const int FirstNumberColumn = 3;

    /// <summary>The template: the header row, with a BOM so Excel opens it as UTF-8.</summary>
    public static byte[] Template()
    {
        var header = string.Join(",", Columns.Select(GovernmentReportCsv.Quote)) + "\r\n";
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(header)];
    }

    /// <summary>
    /// The file's data rows, or the one problem with the whole file: a first row that isn't the
    /// template's header, or no data rows.
    /// </summary>
    internal static OpeningBalanceCsvFile Read(string text)
    {
        var records = Records(text);
        if (records.Count == 0 || records.All(IsBlank))
            return OpeningBalanceCsvFile.Refused("The file has no rows.");
        if (!IsHeader(records[0]))
            return OpeningBalanceCsvFile.Refused("The first row must be the template's header.");

        var rows = new List<OpeningBalanceCsvRow>();
        for (var i = 1; i < records.Count; i++)
        {
            if (!IsBlank(records[i]))
                rows.Add(ReadRow(i + 1, records[i]));
        }
        return rows.Count == 0
            ? OpeningBalanceCsvFile.Refused("The file has no rows.")
            : new OpeningBalanceCsvFile(null, rows);
    }

    /// <summary>
    /// A value from the file, safe to show back: through the same formula-injection neutraliser the
    /// CSV exports use, so a page that exports the problems can't be made to run it.
    /// </summary>
    internal static string Echo(string value) => GovernmentReportCsv.Neutralize(value);

    private static bool IsBlank(List<string> record) => record.All(string.IsNullOrWhiteSpace);

    /// <summary>The template's header, ignoring case, spacing and blank cells after it.</summary>
    private static bool IsHeader(List<string> record) =>
        record.Count >= Columns.Count
        && record.Zip(Columns).All(p => string.Equals(p.First.Trim(), p.Second, StringComparison.OrdinalIgnoreCase))
        && record.Skip(Columns.Count).All(string.IsNullOrWhiteSpace);

    private static OpeningBalanceCsvRow ReadRow(int row, List<string> record)
    {
        var problems = new List<string>();
        // More cells than the header usually means an unquoted thousands separator split a number,
        // which would shift every later column; nothing after the employee number can be trusted.
        if (record.Count > Columns.Count && record.Skip(Columns.Count).Any(c => !string.IsNullOrWhiteSpace(c)))
        {
            problems.Add($"The row has {record.Count} cells; the header has {Columns.Count}.");
            return new OpeningBalanceCsvRow(row, Cell(record, 0), null, null, new decimal[Columns.Count - FirstNumberColumn],
                problems);
        }

        int? year = null;
        var yearCell = Cell(record, 1);
        if (yearCell.Length == 0)
            problems.Add("Enter a year.");
        else if (int.TryParse(yearCell, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedYear))
            year = parsedYear;
        else
            problems.Add("Enter Year as a number.");

        DateOnly? throughDate = null;
        if (DateOnly.TryParseExact(Cell(record, 2), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var parsedDate))
            throughDate = parsedDate;
        else
            problems.Add("Enter a date as yyyy-MM-dd.");

        var numbers = new decimal[Columns.Count - FirstNumberColumn];
        for (var column = FirstNumberColumn; column < Columns.Count; column++)
        {
            var cell = Cell(record, column);
            if (cell.Length == 0)
                continue;
            if (decimal.TryParse(cell, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var number))
                numbers[column - FirstNumberColumn] = number;
            else
                problems.Add($"Enter {Columns[column]} as a number.");
        }

        return new OpeningBalanceCsvRow(row, Cell(record, 0), year, throughDate, numbers, problems);
    }

    /// <summary>The trimmed cell, or blank past the end of a short row.</summary>
    private static string Cell(List<string> record, int column) => column < record.Count ? record[column].Trim() : "";

    /// <summary>
    /// The file's records per RFC 4180: cells split on commas, a quoted cell may hold commas,
    /// doubled quotes and line breaks; records end at CRLF, LF or CR. A final line break doesn't
    /// start another record.
    /// </summary>
    private static List<List<string>> Records(string text)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        var any = false; // whether the current record has begun

        void EndCell()
        {
            record.Add(cell.ToString());
            cell.Clear();
        }

        void EndRecord()
        {
            EndCell();
            records.Add(record);
            record = [];
            any = false;
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c != '"')
                    cell.Append(c);
                else if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else
                    quoted = false;
                continue;
            }

            switch (c)
            {
                case '"':
                    quoted = true;
                    any = true;
                    break;
                case ',':
                    EndCell();
                    any = true;
                    break;
                case '\r':
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                        i++;
                    EndRecord();
                    break;
                case '\n':
                    EndRecord();
                    break;
                default:
                    cell.Append(c);
                    any = true;
                    break;
            }
        }
        if (any || cell.Length > 0)
            EndRecord();
        return records;
    }
}

/// <summary>A read file: its data rows, or the one <paramref name="Problem"/> that refuses it whole.</summary>
internal sealed record OpeningBalanceCsvFile(string? Problem, IReadOnlyList<OpeningBalanceCsvRow> Rows)
{
    public static OpeningBalanceCsvFile Refused(string problem) => new(problem, []);
}

/// <summary>
/// A data row as read: its row number, the trimmed employee number, the year and through date when
/// they parsed, the eight amounts then the leave days (0 where blank), and the cells that didn't parse.
/// </summary>
internal sealed record OpeningBalanceCsvRow(int Row, string EmployeeNumber, int? Year, DateOnly? ThroughDate,
    decimal[] Numbers, IReadOnlyList<string> Problems);
