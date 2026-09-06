using PeopleCore.Application.Payroll.DTOs;

namespace PeopleCore.Application.Payroll.Interfaces;

/// <summary>
/// Renders payslip PDFs. Implemented in PeopleCore.Reports, which references Application - so
/// Application declares the contract and never names QuestPDF, keeping the dependency pointing
/// inward.
/// </summary>
public interface IPayslipRenderer
{
    byte[] Render(PayrollRunDto run, PayrollRunEmployeeDto employee, PayslipCompanyDto company);

    /// <summary>Every entry merged into one document, in the order given.</summary>
    byte[] RenderMerged(PayrollRunDto run, IReadOnlyList<PayrollRunEmployeeDto> employees, PayslipCompanyDto company);
}
