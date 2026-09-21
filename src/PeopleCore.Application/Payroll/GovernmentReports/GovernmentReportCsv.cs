using System.Text;

namespace PeopleCore.Application.Payroll.GovernmentReports;

/// <summary>A government report as CSV: the employer, a blank line, the table, then any form lines.</summary>
public static class GovernmentReportCsv
{
    public const string ContentType = "text/csv";

    public static string FileName(GovernmentReportDto report) => $"{report.Report}-{report.Year:D4}-{report.Month:D2}.csv";

    public static byte[] Write(GovernmentReportDto report)
    {
        var text = new StringBuilder();
        void Line(params string[] cells) => text.Append(string.Join(",", cells.Select(Quote))).Append("\r\n");

        Line(report.Employer.Name);
        Line(report.Title, report.Basis);
        Line("TIN", report.Employer.Tin);
        Line("Employer number", report.Employer.AgencyNumber);
        Line();
        Line([.. report.Columns]);
        foreach (var row in report.Rows)
            Line([.. row.Cells]);
        Line([.. report.Totals]);

        if (report.Summary.Count > 0)
        {
            Line();
            foreach (var line in report.Summary)
                Line(line.Label, GovernmentReportMath.Money(line.Amount));
        }

        // With a BOM, so Excel opens it as UTF-8.
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }

    private static string Quote(string cell)
        => cell.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{cell.Replace("\"", "\"\"")}\"" : cell;
}
