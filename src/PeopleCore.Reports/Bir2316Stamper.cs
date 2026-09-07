using System.Reflection;
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
    /// Pinned to a fixed instant rather than the wall clock so that generating the same
    /// certificate twice produces the same <c>/CreationDate</c> and <c>/ModificationDate</c>.
    /// This is a legitimate reproducibility property of a generated document — unlike the
    /// per-save subset-font and XMP identifiers PDFsharp also varies, <see cref="PdfDocumentInformation.CreationDate"/>
    /// and <see cref="PdfDocumentInformation.ModificationDate"/> are public, documented API with
    /// no structural role in the file, so pinning them carries no risk of producing a malformed PDF.
    /// </summary>
    private static readonly DateTime FixedTimestamp = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// PDFsharp writes a 16-byte "second" document identifier into the trailer's <c>/ID</c> array
    /// on every <c>Modify</c>-mode open, generated from <c>Guid.NewGuid()</c>. Overwritten with
    /// this fixed value before <c>Save</c> so the trailer does not vary between renders. Like the
    /// timestamps above, this is a public, documented PDFsharp API — it does not touch the saved
    /// bytes after the fact the way the font-subset-tag and XMP-UUID rewriting used to.
    /// </summary>
    private static readonly byte[] FixedSecondDocumentId = new byte[16];

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
        return output.ToArray();
    }

    private static Stream OpenEmbeddedForm()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly.GetManifestResourceStream(EmbeddedFormResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded form resource '{EmbeddedFormResourceName}' was not found in {assembly.FullName}.");
    }
}
