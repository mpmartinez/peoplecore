using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Interfaces;

namespace PeopleCore.Reports;

/// <summary>
/// Thin wrapper over <see cref="Bir2316Stamper"/>: the only place in this project that knows the
/// 2316 is rendered by stamping the official form rather than drawing a reproduction, so
/// Bir2316Service (Application) never has to. Mirrors <see cref="PayslipRenderer"/>, except a 2316
/// has no QuestPDF document to merge with <c>Document.Merge</c> — <see cref="RenderMerged"/>
/// stamps each certificate on its own and then concatenates the resulting single-page PDFs with
/// PDFsharp directly.
/// </summary>
public class Bir2316Renderer : IBir2316Renderer
{
    public byte[] Render(Bir2316Dto dto) => new Bir2316Stamper().Stamp(dto);

    public byte[] RenderMerged(IReadOnlyList<Bir2316Dto> forms)
    {
        // PayslipRenderer.RenderMerged has no empty-list guard of its own — QuestPDF's
        // Document.Merge over an empty sequence still "succeeds" with an empty byte array, and it
        // is PayslipService.GenerateForRunAsync, upstream, that refuses to call it with no
        // employees so that bug can never reach a caller. Bir2316Controller.GenerateAll applies
        // the identical guard before calling this method. Stamping here still throws rather than
        // silently handing back a zero-page PDF: RenderMerged has no caller-supplied "no run"
        // case to fall back to the way the payslip flow does, so an empty list reaching this
        // method at all is a caller bug, not a legitimate empty result to paper over.
        if (forms.Count == 0)
            throw new ArgumentException("Cannot render a merged 2316 document from an empty list of forms.", nameof(forms));

        var stamper = new Bir2316Stamper();
        using var merged = new PdfDocument();

        foreach (var dto in forms)
        {
            var stamped = stamper.Stamp(dto);
            using var single = PdfReader.Open(new MemoryStream(stamped), PdfDocumentOpenMode.Import);
            foreach (var page in single.Pages)
            {
                merged.AddPage(page);
            }
        }

        using var output = new MemoryStream();
        merged.Save(output, closeStream: false);
        return output.ToArray();
    }
}
