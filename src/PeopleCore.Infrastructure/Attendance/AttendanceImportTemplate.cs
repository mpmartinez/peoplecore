using System.IO.Compression;
using System.Security;
using System.Text;

namespace PeopleCore.Infrastructure.Attendance;

/// <summary>
/// A blank of PeopleCore's own attendance layout, as CSV or Excel, for HR to fill in by hand when a
/// time clock's export isn't available. Both read back through <see cref="AttendanceFile"/>, the
/// example rows included, so a template filled in and uploaded unchanged imports exactly those rows.
/// </summary>
public static class AttendanceImportTemplate
{
    public const string CsvContentType = "text/csv";
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static readonly string[][] Rows =
    [
        ["employee_number", "date", "time_in", "time_out"],
        ["EMP-001", "2026-03-10", "08:00", "17:00"],
        ["EMP-001", "2026-03-11", "07:55", "17:30"],
        ["EMP-002", "2026-03-10", "08:10", ""],
    ];

    public static byte[] Csv()
    {
        var text = string.Join("\r\n", Rows.Select(r => string.Join(",", r))) + "\r\n";
        // With a BOM, so Excel opens it as UTF-8.
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)];
    }

    /// <summary>
    /// One sheet, every cell text so Excel keeps <c>2026-03-10</c> and <c>08:00</c> as written. A
    /// date or time typed over them becomes a real Excel date, which the import reads as well.
    /// </summary>
    public static byte[] Xlsx()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string xml)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
                writer.Write("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
                writer.Write(xml);
            }

            Add("[Content_Types].xml",
                """<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>""");
            Add("_rels/.rels",
                """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
            Add("xl/workbook.xml",
                """<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Attendance" sheetId="1" r:id="rId1"/></sheets></workbook>""");
            Add("xl/_rels/workbook.xml.rels",
                """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>""");
            // Style 1 is the bold header; style 2 is "@" (text) so typed values stay as written.
            Add("xl/styles.xml",
                """<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="3"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="49" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1" applyNumberFormat="1"/><xf numFmtId="49" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>""");

            var sheet = new StringBuilder(
                """<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><cols><col min="1" max="1" width="18" customWidth="1" style="2"/><col min="2" max="2" width="14" customWidth="1" style="2"/><col min="3" max="4" width="11" customWidth="1" style="2"/></cols><sheetData>""");
            for (var r = 0; r < Rows.Length; r++)
            {
                sheet.Append($"<row r=\"{r + 1}\">");
                for (var c = 0; c < Rows[r].Length; c++)
                {
                    if (Rows[r][c].Length == 0) continue;
                    sheet.Append($"<c r=\"{(char)('A' + c)}{r + 1}\" s=\"{(r == 0 ? 1 : 2)}\" t=\"inlineStr\"><is><t>{SecurityElement.Escape(Rows[r][c])}</t></is></c>");
                }
                sheet.Append("</row>");
            }
            sheet.Append("</sheetData></worksheet>");
            Add("xl/worksheets/sheet1.xml", sheet.ToString());
        }
        return buffer.ToArray();
    }
}
