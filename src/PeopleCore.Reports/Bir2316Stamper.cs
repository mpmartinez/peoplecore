using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Internal;
using PdfSharp.Pdf.IO;
using PeopleCore.Application.Payroll.DTOs;

namespace PeopleCore.Reports;

/// <summary>
/// Stamps a <see cref="Bir2316Dto"/> onto the Bureau's own BIR Form 2316 PDF rather than
/// redrawing the form from scratch, as <see cref="Bir2316Document"/> (QuestPDF) does today.
/// <para>
/// The official form ships with no AcroForm fields — <c>get_fields()</c> on the source PDF
/// returns none — so it cannot be filled the way a normal PDF form is; every value has to be
/// drawn onto the page at an absolute coordinate with <see cref="XGraphics"/>, the same way you'd
/// line up a typewriter over pre-printed stock. This class only proves that pipeline end to end
/// with the employee's TIN; Task 2 supplies the full coordinate map for every numbered box, and
/// Task 3 retires <see cref="Bir2316Renderer"/> and <see cref="Bir2316Document"/> in favor of it.
/// </para>
/// </summary>
public sealed class Bir2316Stamper
{
    private const string EmbeddedFormResourceName = "PeopleCore.Reports.Forms.bir-2316-2021-encs.pdf";

    /// <summary>
    /// Fixed so <see cref="Stamp"/> is byte-for-byte deterministic for the same input, rather than
    /// varying with the wall clock of whichever machine happens to render it.
    /// </summary>
    private static readonly DateTime FixedTimestamp = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// PDFsharp writes a 16-byte "second" document identifier into the trailer's <c>/ID</c> array
    /// on every <c>Modify</c>-mode open, generated from <c>Guid.NewGuid()</c>. Overwritten with
    /// this fixed value before <c>Save</c> so the trailer does not vary between renders.
    /// </summary>
    private static readonly byte[] FixedSecondDocumentId = new byte[16];

    /// <summary>
    /// Two more sources of non-determinism that PDFsharp does not expose a setting for, so they
    /// are normalized after <c>Save</c> by rewriting the output bytes in place. Both replacements
    /// are exactly as long as the text they replace, so no byte offset elsewhere in the file
    /// (cross-reference table, stream <c>/Length</c> entries) is invalidated by the rewrite:
    /// <list type="bullet">
    /// <item>
    /// Every embedded font is renamed with a 6-uppercase-letter "subset tag" prefix per the PDF
    /// spec (e.g. <c>ABCDEF+Arial</c>), and PDFsharp generates that tag from
    /// <c>Guid.NewGuid()</c> with no way to override it — see
    /// <c>PdfFontDescriptor.CreateEmbeddedFontSubsetName</c>.
    /// </item>
    /// <item>
    /// The XMP metadata packet PDFsharp writes into every saved document embeds a fresh
    /// <c>DocumentID</c>/<c>InstanceID</c> UUID pair from <c>Guid.NewGuid()</c> on every save —
    /// see <c>PdfMetadata.GenerateXmp</c> — independent of the trailer <c>/ID</c> above.
    /// </item>
    /// </list>
    /// </summary>
    private static readonly Regex FontSubsetTagPattern =
        new(@"(?<=/(?:FontName|BaseFont)/)[A-Z]{6}(?=\+[A-Za-z0-9]+)", RegexOptions.Compiled);

    private static readonly Regex XmpUuidPattern =
        new(@"(?<=uuid:)[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
            RegexOptions.Compiled);

    public Bir2316Stamper()
    {
        EmbeddedFontResolver.EnsureRegistered();
    }

    public byte[] Stamp(Bir2316Dto dto)
    {
        using var formStream = OpenEmbeddedForm();
        using var document = PdfReader.Open(formStream, PdfDocumentOpenMode.Modify);

        document.Info.CreationDate = FixedTimestamp;
        document.Info.ModificationDate = FixedTimestamp;
        document.Internals.SecondDocumentID =
            PdfEncoders.RawEncoding.GetString(FixedSecondDocumentId, 0, FixedSecondDocumentId.Length);

        var page = document.Pages[0];
        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 8);

        // Placeholder placement — Task 2 replaces this with the real coordinate map for every
        // numbered box on the form. This proves the stamping pipeline (open -> draw -> save)
        // works end to end for one real value.
        gfx.DrawString(dto.EmployeeTin, font, XBrushes.Black, new XPoint(150, 100));

        using var output = new MemoryStream();
        document.Save(output, closeStream: false);
        return NormalizeNonDeterministicBytes(output.ToArray());
    }

    private static byte[] NormalizeNonDeterministicBytes(byte[] pdfBytes)
    {
        // Latin1 maps every byte to exactly one char and back, so this round-trips binary
        // (compressed stream) content untouched while still letting us regex the readable
        // dictionary text and XMP packet that the two patterns above target.
        var text = Encoding.Latin1.GetString(pdfBytes);
        text = FontSubsetTagPattern.Replace(text, "PCRPTS");
        text = XmpUuidPattern.Replace(text, "00000000-0000-0000-0000-000000000000");
        return Encoding.Latin1.GetBytes(text);
    }

    private static Stream OpenEmbeddedForm()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly.GetManifestResourceStream(EmbeddedFormResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded form resource '{EmbeddedFormResourceName}' was not found in {assembly.FullName}.");
    }
}
