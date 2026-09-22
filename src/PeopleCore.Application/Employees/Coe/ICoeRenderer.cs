namespace PeopleCore.Application.Employees.Coe;

/// <summary>
/// Renders a Certificate of Employment PDF. Implemented in PeopleCore.Reports, which references
/// Application - so Application declares the contract and never names QuestPDF, keeping the
/// dependency pointing inward (the same split as <c>IPayslipRenderer</c>).
/// </summary>
public interface ICoeRenderer
{
    byte[] Render(CoeContent content);
}
