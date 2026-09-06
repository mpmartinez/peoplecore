using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Domain.Entities.Payroll;

namespace PeopleCore.Application.Payroll.Services;

/// <summary>
/// Builds the earning and deduction breakdowns a payslip and the payroll register render.
/// <para>
/// Both were previously duplicated across PayrollRunsController and ReportsController, so a
/// new pay component could appear on one and not the other. The payslip prints GrossPay and
/// TotalDeductions as the column totals rather than summing these lines, so the lines have to
/// add up to those figures or the document visibly fails to foot.
/// </para>
/// </summary>
public static class PayrollLineBuilder
{
    public static List<PayrollEarningLineDto> Earnings(PayrollRunEmployee e)
    {
        // RegularPay is already net of absences and tardiness, so the basic figure is restored
        // and the reductions shown beneath it. Presenting them as deductions instead would
        // double-count: they are not part of TotalDeductions.
        decimal basicForPeriod = e.RegularPay + e.AbsenceDeduction + e.TardinessDeduction;

        var lines = new List<PayrollEarningLineDto> { new("Basic Pay", basicForPeriod) };

        if (e.AbsenceDeduction > 0) lines.Add(new("Less: Absences", -e.AbsenceDeduction));
        if (e.TardinessDeduction > 0) lines.Add(new("Less: Tardiness / Undertime", -e.TardinessDeduction));
        if (e.OvertimePay > 0) lines.Add(new("Overtime Pay", e.OvertimePay));
        if (e.HolidayPay > 0) lines.Add(new("Holiday Premium", e.HolidayPay));
        if (e.NightDiffPay > 0) lines.Add(new("Night Shift Differential", e.NightDiffPay));
        if (e.TaxableAllowances > 0) lines.Add(new("Taxable Allowances", e.TaxableAllowances));
        if (e.NonTaxableAllowances > 0) lines.Add(new("Non-Taxable Allowances", e.NonTaxableAllowances, IsTaxable: false));
        if (e.ThirteenthMonth > 0) lines.Add(new("13th Month Pay", e.ThirteenthMonth, IsTaxable: false));

        return lines;
    }

    public static List<PayrollDeductionLineDto> Deductions(PayrollRunEmployee e)
    {
        var lines = new List<PayrollDeductionLineDto>
        {
            new("SSS (Employee)", e.SSSEmployee),
            new("PhilHealth (Employee)", e.PhilHealthEmployee),
            new("Pag-IBIG (Employee)", e.PagIbigEmployee),
            new("Withholding Tax", e.WithholdingTax)
        };

        if (e.LoanDeductions > 0) lines.Add(new("Loan Deduction", e.LoanDeductions));
        if (e.OtherDeductions > 0) lines.Add(new("Other Deductions", e.OtherDeductions));

        lines.Add(new("SSS (Employer)", e.SSSEmployer, IsEmployer: true));
        lines.Add(new("PhilHealth (Employer)", e.PhilHealthEmployer, IsEmployer: true));
        lines.Add(new("Pag-IBIG (Employer)", e.PagIbigEmployer, IsEmployer: true));

        return lines;
    }
}
