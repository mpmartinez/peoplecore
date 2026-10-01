using System.Globalization;
using System.Text;

namespace PeopleCore.Application.Payroll.GovernmentReports;

/// <summary>
/// A government report as CSV: the employer, a blank line, the table (or, for the 1604-C alphalist,
/// each of its sections' own table), then any form lines.
/// </summary>
public static class GovernmentReportCsv
{
    public const string ContentType = "text/csv";

    public static string FileName(GovernmentReportDto report) => report.IsAnnual
        ? $"{report.Report}-{report.Year:D4}.csv"
        : $"{report.Report}-{report.Year:D4}-{report.Month:D2}.csv";

    public static byte[] Write(GovernmentReportDto report)
    {
        var text = new StringBuilder();
        void Line(params string[] cells) => text.Append(string.Join(",", cells.Select(Quote))).Append("\r\n");

        Line(report.Employer.Name);
        Line(report.Title, report.Basis);
        Line("TIN", report.Employer.Tin);
        Line("Employer number", report.Employer.AgencyNumber);

        if (report.Columns.Count > 0)
        {
            Line();
            Line([.. report.Columns]);
            foreach (var row in report.Rows)
                Line([.. row.Cells]);
            Line([.. report.Totals]);
        }

        if (report.Summary.Count > 0)
        {
            Line();
            foreach (var line in report.Summary)
                Line(line.Label, GovernmentReportMath.Money(line.Amount));
        }

        foreach (var section in report.Sections)
        {
            Line();
            Line(section.Title);
            if (section.Rows.Count == 0)
            {
                Line(section.EmptyMessage);
                continue;
            }
            Line([.. section.Columns]);
            foreach (var row in section.Rows)
                Line([.. row.Cells]);
            Line([.. section.Totals]);
        }

        // With a BOM, so Excel opens it as UTF-8.
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }

    /// <summary>A CSV cell: <see cref="Neutralize"/>d, then quoted per RFC 4180 when it needs to be.</summary>
    internal static string Quote(string cell)
    {
        var neutralized = Neutralize(cell);

        // Apply RFC 4180 quoting for commas, quotes, CR, LF.
        return neutralized.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{neutralized.Replace("\"", "\"\"")}\"" : neutralized;
    }

    /// <summary>
    /// Prevents formula injection: a value starting with =, +, -, @, \t or \r is prefixed with ',
    /// so a spreadsheet shows it as text instead of running it. Exception: negative amounts
    /// (starting with -) that parse as decimal are kept numeric (e.g., money like -50.00).
    /// </summary>
    internal static string Neutralize(string cell)
    {
        if (cell.Length == 0 || "=+-@\t\r".IndexOf(cell[0]) < 0)
            return cell;
        if (cell[0] == '-' && decimal.TryParse(cell, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
            return cell; // Keep negative numbers numeric (money).
        return "'" + cell;
    }
}
