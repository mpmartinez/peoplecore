using System.Text;
using FluentAssertions;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Reports;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class Bir2316StamperTests
{
    [Fact]
    public void Stamp_ProducesTheOfficialPageSize()
    {
        var pdf = new Bir2316Stamper().Stamp(SampleDto());

        using var doc = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.ReadOnly);
        doc.PageCount.Should().Be(1);
        // 612 x 936 pts = 8.5 x 13in, Philippine folio. The QuestPDF version used Legal
        // (8.5 x 14in) and was a full inch too tall, so nothing aligned on official stock.
        doc.Pages[0].Width.Point.Should().BeApproximately(612, 0.5);
        doc.Pages[0].Height.Point.Should().BeApproximately(936, 0.5);
    }

    [Fact]
    public void Stamp_RendersIdenticallyAcrossRuns()
    {
        // Byte identity is deliberately NOT asserted here: PDFsharp legitimately varies the
        // embedded font's subset tag and the XMP metadata's DocumentID/InstanceID UUIDs on every
        // `Save`, and font subset tags are required by the PDF spec to be unique per subset, so
        // forcing them to a fixed value would make the file technically malformed. What actually
        // must not vary is what ends up on the page: if font resolution ever fell back to a
        // system font instead of the embedded TTF, glyph metrics would differ and the stamped
        // text would land at different coordinates. So this compares the *positioned text runs*
        // extracted from the page content stream instead of the raw bytes.
        var dto = SampleDto();

        var a = new Bir2316Stamper().Stamp(dto);
        var b = new Bir2316Stamper().Stamp(dto);

        var runsA = ExtractPositionedTextRuns(a);
        var runsB = ExtractPositionedTextRuns(b);

        runsA.Should().NotBeEmpty();
        runsA.Should().Equal(runsB);
    }

    /// <summary>
    /// Walks the page's content stream operators and records every shown text run together with
    /// the text position in effect when it was shown. This is a simplified reader — it handles
    /// only the operators <see cref="Bir2316Stamper"/> (via <c>XGraphics.DrawString</c>) actually
    /// emits (<c>Tm</c>/<c>Td</c>/<c>TD</c> to position, <c>Tj</c>/<c>TJ</c> to show text) — not a
    /// general-purpose PDF text extractor.
    /// </summary>
    private static List<(string Text, double X, double Y)> ExtractPositionedTextRuns(byte[] pdfBytes)
    {
        using var doc = PdfReader.Open(new MemoryStream(pdfBytes), PdfDocumentOpenMode.ReadOnly);
        var runs = new List<(string Text, double X, double Y)>();

        foreach (var page in doc.Pages)
        {
            var content = ContentReader.ReadContent(page);
            double x = 0, y = 0;

            foreach (var obj in content)
            {
                if (obj is not COperator op) continue;

                switch (op.OpCode.OpCodeName)
                {
                    case OpCodeName.BT:
                        x = 0;
                        y = 0;
                        break;

                    case OpCodeName.Tm when op.Operands.Count == 6:
                        x = NumberValue(op.Operands[4]);
                        y = NumberValue(op.Operands[5]);
                        break;

                    case OpCodeName.Td or OpCodeName.TD when op.Operands.Count == 2:
                        x += NumberValue(op.Operands[0]);
                        y += NumberValue(op.Operands[1]);
                        break;

                    case OpCodeName.Tj when op.Operands.Count == 1 && op.Operands[0] is CString s:
                        runs.Add((s.Value, x, y));
                        break;

                    case OpCodeName.TJ when op.Operands.Count == 1 && op.Operands[0] is CSequence array:
                        var text = new StringBuilder();
                        foreach (var element in array)
                        {
                            if (element is CString elementString) text.Append(elementString.Value);
                        }
                        runs.Add((text.ToString(), x, y));
                        break;
                }
            }
        }

        return runs;
    }

    [Fact]
    public void Stamp_UsesTheEmbeddedFontRatherThanASystemFont()
    {
        // This pins the actual risk the deleted byte-rewriting was papering over: font
        // resolution must not depend on what is installed on the machine. If the resolver ever
        // fell back to a system font, a developer's Windows box (which has "Arial" installed)
        // would render different glyph metrics than a Linux container (which does not) even
        // though both ran the same code. Testing the resolver directly, at the seam where that
        // risk actually lives, is more honest than inferring it from PDF output.
        EmbeddedFontResolver.EnsureRegistered();
        var resolver = new EmbeddedFontResolver();

        var typeface = resolver.ResolveTypeface("Arial", isBold: false, isItalic: false);

        typeface.Should().NotBeNull();
        var fontBytes = resolver.GetFont(typeface!.FaceName);
        fontBytes.Should().NotBeEmpty();
    }

    private static double NumberValue(CObject obj) => obj switch
    {
        CInteger i => i.Value,
        CReal r => r.Value,
        _ => throw new InvalidOperationException($"Expected a numeric content-stream operand, got {obj.GetType().Name}.")
    };

    private static Bir2316Dto SampleDto() => new()
    {
        Year = 2026,
        PeriodFrom = "January",
        PeriodTo = "December",
        EmployeeTin = "123-456-789-000",
        EmployeeLastName = "Dela Cruz",
        EmployeeFirstName = "Juan",
        EmployeeMiddleName = "Protacio",
        RdoCode = "039",
        RegisteredAddress = "123 Rizal St., Makati City",
        RegisteredZipCode = "1200",
        LocalHomeAddress = "123 Rizal St., Makati City",
        LocalZipCode = "1200",
        DateOfBirth = "01/15/1990",
        ContactNumber = "0917-123-4567",
        EmployerTin = "987-654-321-000",
        EmployerName = "M2NET Solutions Inc.",
        EmployerAddress = "456 Ayala Avenue, Makati City",
        EmployerZipCode = "1226",
        IsMainEmployer = true,
    };
}
