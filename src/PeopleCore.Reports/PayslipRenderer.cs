using QuestPDF.Fluent;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Interfaces;

namespace PeopleCore.Reports;

/// <summary>
/// Thin wrapper over <see cref="PayslipDocument"/>: the only place in this project that knows
/// about QuestPDF's <c>GeneratePdf</c>/<c>Document.Merge</c> entry points, so PayslipService
/// (Application) never has to.
/// </summary>
public class PayslipRenderer : IPayslipRenderer
{
    public byte[] Render(PayrollRunDto run, PayrollRunEmployeeDto employee, PayslipCompanyDto company)
        => new PayslipDocument(run, employee, company).GeneratePdf();

    public byte[] RenderMerged(PayrollRunDto run, IReadOnlyList<PayrollRunEmployeeDto> employees, PayslipCompanyDto company)
        => QuestPDF.Fluent.Document.Merge(
            employees.Select(employee => new PayslipDocument(run, employee, company))).GeneratePdf();
}
