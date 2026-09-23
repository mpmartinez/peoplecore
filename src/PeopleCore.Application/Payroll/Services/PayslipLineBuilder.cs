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
        if (e.HolidayPay > 0) lines.Add(new("Holiday / Rest Day Premium", e.HolidayPay));
        if (e.NightDiffPay > 0) lines.Add(new("Night Shift Differential", e.NightDiffPay));
        if (e.TaxableAllowances > 0) lines.Add(new("Taxable Allowances", e.TaxableAllowances));
        if (e.NonTaxableAllowances > 0) lines.Add(new("Non-Taxable Allowances", e.NonTaxableAllowances, IsTaxable: false));
        if (e.ThirteenthMonth > 0) lines.Add(new("13th Month Pay", e.ThirteenthMonth, IsTaxable: false));

        // Final-pay earnings, zero on a regular run. A line is flagged non-taxable only when all
        // of it is: FinalPayNonTaxable covers leave conversion's de minimis part plus whatever of
        // separation and retirement pay is exempt.
        if (e.LeaveConversionPay > 0)
            lines.Add(new("Leave Conversion", e.LeaveConversionPay, IsTaxable: e.LeaveConversionNonTaxable < e.LeaveConversionPay));
        decimal separationAndRetirementNonTaxable = e.FinalPayNonTaxable - e.LeaveConversionNonTaxable;
        bool separationAndRetirementTaxable = separationAndRetirementNonTaxable < e.SeparationPay + e.RetirementPay;
        if (e.SeparationPay > 0) lines.Add(new("Separation Pay", e.SeparationPay, IsTaxable: separationAndRetirementTaxable));
        if (e.RetirementPay > 0) lines.Add(new("Retirement Pay", e.RetirementPay, IsTaxable: separationAndRetirementTaxable));

        return lines;
    }

    public static List<PayrollDeductionLineDto> Deductions(PayrollRunEmployeeDto e)
    {
        var lines = new List<PayrollDeductionLineDto>
        {
            new("SSS (Employee)", e.SSSEmployee),
            new("PhilHealth (Employee)", e.PhilHealthEmployee),
            new("Pag-IBIG (Employee)", e.PagIbigEmployee),
            // A final pay settles the year's tax, which can come out as a refund. It stays in this
            // column, so the lines still add up to TotalDeductions and gross less that to net pay,
            // but as a credit named for what it is rather than a negative "Withholding Tax".
            e.WithholdingTax < 0
                ? new("Less: Tax refund", e.WithholdingTax)
                : new("Withholding Tax", e.WithholdingTax)
        };

        if (e.LoanDeductions > 0) lines.Add(new("Loan Deduction", e.LoanDeductions));
        if (e.OtherDeductions > 0) lines.Add(new("Other Deductions", e.OtherDeductions));

        lines.Add(new("SSS (Employer)", e.SSSEmployer, IsEmployer: true));
        lines.Add(new("PhilHealth (Employer)", e.PhilHealthEmployer, IsEmployer: true));
        lines.Add(new("Pag-IBIG (Employer)", e.PagIbigEmployer, IsEmployer: true));

        return lines;
    }
}
