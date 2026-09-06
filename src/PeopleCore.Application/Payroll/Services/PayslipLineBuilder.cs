using PeopleCore.Application.Payroll.DTOs;

namespace PeopleCore.Application.Payroll.Services;

/// <summary>
/// Builds the earning and deduction breakdowns a rendered payslip PDF displays, from the
/// DTO-shaped <see cref="PayrollRunEmployeeDto"/>. This is <see cref="PayrollLineBuilder"/>'s
/// logic mirrored exactly, not shared with it: PayrollLineBuilder takes the
/// <see cref="Domain.Entities.Payroll.PayrollRunEmployee"/> entity and is byte-verified, so it
/// cannot be changed to also accept the DTO that PayslipDocument (in PeopleCore.Reports, which
/// does not reference the Domain project) actually has in hand.
/// <para>
/// The payslip prints GrossPay and TotalDeductions as the column totals rather than summing
/// these lines, so the lines have to add up to those figures or the document visibly fails to
/// foot.
/// </para>
/// </summary>
public static class PayslipLineBuilder
{
    public static List<PayrollEarningLineDto> Earnings(PayrollRunEmployeeDto e)
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

    public static List<PayrollDeductionLineDto> Deductions(PayrollRunEmployeeDto e)
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
