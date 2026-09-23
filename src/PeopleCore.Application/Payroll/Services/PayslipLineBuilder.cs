using PeopleCore.Application.Payroll.DTOs;

namespace PeopleCore.Application.Payroll.Services;

/// <summary>
/// Builds the earning and deduction breakdowns a rendered payslip PDF displays, from the
/// DTO-shaped <see cref="PayrollRunEmployeeDto"/> - the shape PayslipDocument (in
/// PeopleCore.Reports, which does not reference the Domain project) has in hand. It is the one
/// place a payslip's breakdown is built.
/// <para>
/// The payslip prints GrossPay and <see cref="DeductionsTotal"/> as the column totals rather than
/// summing these lines, so the lines have to add up to those figures or the document visibly fails
/// to foot. Gross less those deductions plus any <see cref="TaxRefund"/> is the net pay.
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
        // separation and retirement pay is exempt. Leave beyond de minimis is "other benefits",
        // taxed with the 13th month only past the year's 90,000 exemption - which one payslip
        // can't see - so the leave line is flagged as the 13th month's is.
        if (e.LeaveConversionPay > 0)
            lines.Add(new("Leave Conversion", e.LeaveConversionPay, IsTaxable: false));
        decimal separationAndRetirementNonTaxable = e.FinalPayNonTaxable - e.LeaveConversionNonTaxable;
        bool separationAndRetirementTaxable = separationAndRetirementNonTaxable < e.SeparationPay + e.RetirementPay;
        if (e.SeparationPay > 0) lines.Add(new("Separation Pay", e.SeparationPay, IsTaxable: separationAndRetirementTaxable));
        if (e.RetirementPay > 0) lines.Add(new("Retirement Pay", e.RetirementPay, IsTaxable: separationAndRetirementTaxable));

        return lines;
    }

    /// <summary>
    /// The tax handed back when a final pay settles the year's tax below what was already
    /// withheld (a negative <see cref="PayrollRunEmployeeDto.WithholdingTax"/>); zero otherwise.
    /// The payslip adds it after the deductions, above net pay.
    /// </summary>
    public static decimal TaxRefund(PayrollRunEmployeeDto e) => Math.Max(-e.WithholdingTax, 0m);

    /// <summary>
    /// The deductions actually taken, which is what the payslip prints as its total:
    /// <see cref="PayrollRunEmployeeDto.TotalDeductions"/> nets a refund in, so it is added back
    /// here. Never negative. The stored figures are untouched.
    /// </summary>
    public static decimal DeductionsTotal(PayrollRunEmployeeDto e) => e.TotalDeductions + TaxRefund(e);

    public static List<PayrollDeductionLineDto> Deductions(PayrollRunEmployeeDto e)
    {
        var lines = new List<PayrollDeductionLineDto>
        {
            new("SSS (Employee)", e.SSSEmployee),
            new("PhilHealth (Employee)", e.PhilHealthEmployee),
            new("Pag-IBIG (Employee)", e.PagIbigEmployee),
            // A refund withholds nothing; it is shown on its own (TaxRefund), never as a negative
            // deduction.
            new("Withholding Tax", Math.Max(e.WithholdingTax, 0m))
        };

        if (e.LoanDeductions > 0) lines.Add(new("Loan Deduction", e.LoanDeductions));
        if (e.OtherDeductions > 0) lines.Add(new("Other Deductions", e.OtherDeductions));

        lines.Add(new("SSS (Employer)", e.SSSEmployer, IsEmployer: true));
        lines.Add(new("PhilHealth (Employer)", e.PhilHealthEmployer, IsEmployer: true));
        lines.Add(new("Pag-IBIG (Employer)", e.PagIbigEmployer, IsEmployer: true));

        return lines;
    }
}
