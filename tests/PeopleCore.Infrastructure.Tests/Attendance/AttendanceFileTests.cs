using System.IO.Compression;
using System.Security;
using System.Text;
using FluentAssertions;
using PeopleCore.Infrastructure.Attendance;

namespace PeopleCore.Infrastructure.Tests.Attendance;

/// <summary>
/// Reading a client's time clock export. ZKTeco and its software write one row per scan; the import
/// keeps each person's first scan of the day as the time-in and the last as the time-out.
/// </summary>
public class AttendanceFileTests
{
    private static AttendanceFileParseResult Parse(string text, string fileName = "export.csv") =>
        AttendanceFile.Parse(new MemoryStream(Encoding.UTF8.GetBytes(text)), fileName);

    private static DateTime At(int day, int hour, int minute, int second = 0) =>
        new(2026, 3, day, hour, minute, second, DateTimeKind.Utc);

    [Fact]
    public void A_ZKTime_export_keeps_each_days_first_and_last_scan_and_skips_its_title_rows()
    {
        var result = Parse(
            "Attendance Record Report\n" +
            "\n" +
            "AC-No.,No.,Name,Time,State\n" +
            "1023,1,Juan Cruz,3/10/2026 8:02 AM,C/In\n" +
            "1023,2,Juan Cruz,3/10/2026 12:01 PM,C/Out\n" +
            "1023,3,Juan Cruz,3/10/2026 1:00 PM,C/In\n" +
            "1023,4,Juan Cruz,3/10/2026 5:05 PM,C/Out\n" +
            "88,5,Ana Reyes,3/10/2026 7:58 AM,C/In\n");

        result.Layout.Should().Be(AttendanceFileLayout.ScanLog);
        result.Errors.Should().BeEmpty();
        result.Punches.Select(p => (p.EmployeeNumber, p.PunchTime)).Should().Equal(
            ("1023", At(10, 8, 2)),
            ("1023", At(10, 17, 5)),
            ("88", At(10, 7, 58)));
    }

    [Fact]
    public void A_headerless_attlog_download_is_read_as_a_scan_log()
    {
        var result = Parse(
            "    1023\t2026-03-10 08:02:14\t1\t0\t1\t0\n" +
            "    1023\t2026-03-10 17:01:40\t1\t1\t1\t0\n",
            "1_attlog.dat");

        result.Layout.Should().Be(AttendanceFileLayout.ScanLog);
        result.Punches.Select(p => (p.EmployeeNumber, p.PunchTime)).Should().Equal(
            ("1023", At(10, 8, 2, 14)),
            ("1023", At(10, 17, 1, 40)));
    }

    [Fact]
    public void Separate_date_and_time_columns_are_joined()
    {
        var result = Parse(
            "Person ID,First Name,Date,Time,Punch State\n" +
            "7,Ana,2026-03-10,07:58:01,Check In\n" +
            "7,Ana,2026-03-10,17:00:30,Check Out\n");

        result.Punches.Select(p => p.PunchTime).Should().Equal(At(10, 7, 58, 1), At(10, 17, 0, 30));
    }

    [Fact]
    public void A_lone_scan_is_a_time_in_with_no_time_out()
    {
        var result = Parse("User ID,Date/Time\n5,2026-03-10 08:00\n");

        result.Punches.Should().ContainSingle().Which.PunchTime.Should().Be(At(10, 8, 0));
    }

    [Fact]
    public void Slash_dates_are_month_first_unless_one_can_only_be_day_first()
    {
        Parse("User ID,Time\n5,03/10/2026 08:00\n").Punches.Single().PunchTime.Should().Be(At(10, 8, 0));

        Parse("User ID,Time\n5,10/03/2026 08:00\n5,25/03/2026 08:00\n")
            .Punches.Select(p => p.PunchTime).Should().Equal(At(10, 8, 0), At(25, 8, 0));
    }

    [Fact]
    public void A_file_mixing_day_first_and_month_first_dates_is_refused()
    {
        var result = Parse("User ID,Time\n5,25/03/2026 08:00\n5,03/25/2026 08:00\n");

        result.Punches.Should().BeEmpty();
        result.Errors.Should().ContainSingle().Which.Should().Contain("day-first and month-first");
    }

    [Fact]
    public void Semicolon_separated_files_are_read()
    {
        Parse("AC-No.;Time\n1023;2026-03-10 08:02:00\n").Punches.Should().ContainSingle()
            .Which.EmployeeNumber.Should().Be("1023");
    }

    [Fact]
    public void A_row_whose_time_cannot_be_read_is_reported_and_the_rest_kept()
    {
        var result = Parse("User ID,Time\n5,yesterday morning\n6,2026-03-10 08:00\n");

        result.Errors.Should().ContainSingle().Which.Should().StartWith("Row 2:").And.Contain("yesterday morning");
        result.Punches.Should().ContainSingle().Which.EmployeeNumber.Should().Be("6");
    }

    [Fact]
    public void An_xlsx_workbook_is_read_like_a_CSV()
    {
        var workbook = Xlsx(
            ["Attendance Record Report"],
            ["AC-No.", "Name", "Time"],
            ["1023", "Juan Cruz", "3/10/2026 8:02 AM"],
            ["1023", "Juan Cruz", "3/10/2026 5:05 PM"]);

        var result = AttendanceFile.Parse(new MemoryStream(workbook), "export.xlsx");

        result.Layout.Should().Be(AttendanceFileLayout.ScanLog);
        result.Errors.Should().BeEmpty();
        result.Punches.Select(p => p.PunchTime).Should().Equal(At(10, 8, 2), At(10, 17, 5));
    }

    [Fact]
    public void A_file_that_is_not_a_workbook_is_refused_rather_than_thrown()
    {
        var result = AttendanceFile.Parse(new MemoryStream(Encoding.UTF8.GetBytes("not a workbook")), "export.xlsx");

        result.Punches.Should().BeEmpty();
        result.Errors.Should().ContainSingle().Which.Should().Contain("Excel");
    }

    /// <summary>The smallest .xlsx ExcelDataReader accepts: one sheet of inline-string cells.</summary>
    private static byte[] Xlsx(params string[][] rows)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string xml)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write(xml);
            }

            Add("[Content_Types].xml",
                """<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>""");
            Add("_rels/.rels",
                """<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
            Add("xl/workbook.xml",
                """<?xml version="1.0" encoding="UTF-8"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Sheet1" sheetId="1" r:id="rId1"/></sheets></workbook>""");
            Add("xl/_rels/workbook.xml.rels",
                """<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>""");

            var sheet = new StringBuilder("""<?xml version="1.0" encoding="UTF-8"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");
            for (var r = 0; r < rows.Length; r++)
            {
                sheet.Append($"<row r=\"{r + 1}\">");
                for (var c = 0; c < rows[r].Length; c++)
                    sheet.Append($"<c r=\"{(char)('A' + c)}{r + 1}\" t=\"inlineStr\"><is><t>{SecurityElement.Escape(rows[r][c])}</t></is></c>");
                sheet.Append("</row>");
            }
            sheet.Append("</sheetData></worksheet>");
            Add("xl/worksheets/sheet1.xml", sheet.ToString());
        }
        return buffer.ToArray();
    }
}
