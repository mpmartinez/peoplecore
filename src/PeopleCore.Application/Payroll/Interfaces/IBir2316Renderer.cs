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
}
