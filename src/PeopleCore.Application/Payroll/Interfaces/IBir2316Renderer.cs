using PeopleCore.Application.Payroll.DTOs;

namespace PeopleCore.Application.Payroll.Interfaces;

/// <summary>
/// Renders BIR Form 2316 PDFs. Implemented in PeopleCore.Reports, which references Application -
/// so Application declares the contract and never names QuestPDF, keeping the dependency pointing
/// inward. Sibling of <see cref="IPayslipRenderer"/>, split out rather than folded into it because
/// a 2316 is rendered from a single <see cref="Bir2316Dto"/> with no run/employee/company triple
/// and no merge-many use case.
/// </summary>
public interface IBir2316Renderer
{
    byte[] Render(Bir2316Dto dto);

    /// <summary>
    /// Every form merged into one document, in the order given - the 2316 equivalent of
    /// <see cref="IPayslipRenderer.RenderMerged"/>, for GenerateAll's "every employee's 2316 for
    /// the year, in one PDF" use case.
    /// </summary>
    byte[] RenderMerged(IReadOnlyList<Bir2316Dto> forms);
}
