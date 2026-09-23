namespace PeopleCore.Application.Payroll.FinalPay;

/// <summary>
/// The inputs a final-pay run adds on top of an ordinary period: a short period's working days,
/// the extra final-pay earnings (already computed - see <see cref="FinalPayMath"/>), the HR
/// deductions to withhold, and a settled tax figure.
/// </summary>
/// <param name="WorkingDays">Days actually worked in the short final period; prices the base period pay at the daily rate instead of a full salary share.</param>
/// <param name="LeaveConversionNonTaxable">The de minimis part of unused leave converted to cash.</param>
/// <param name="LeaveConversionTaxable">The taxable part of unused leave converted to cash.</param>
/// <param name="SeparationPay">Statutory separation pay, if any.</param>
/// <param name="RetirementPay">Statutory retirement pay, if any.</param>
/// <param name="SeparationAndRetirementNonTaxable">The part of <see cref="SeparationPay"/> plus <see cref="RetirementPay"/> that is non-taxable (e.g. involuntary separation, or RA 7641 retirement).</param>
/// <param name="Deductions">HR's final-pay deductions (unreturned property, unliquidated advances, etc.) - labelled for the payslip, summed into <c>OtherDeductions</c>.</param>
/// <param name="WithholdingTaxOverride">
/// A settled tax figure that replaces both the per-period withholding calculation and the
/// 13th-month excess tax. May be negative (a refund). Null leaves the ordinary calculation in place.
/// </param>
public sealed record FinalPayExtras(
    decimal WorkingDays,
    decimal LeaveConversionNonTaxable,
    decimal LeaveConversionTaxable,
    decimal SeparationPay,
    decimal RetirementPay,
    decimal SeparationAndRetirementNonTaxable,
    IReadOnlyList<(string Label, decimal Amount)> Deductions,
    decimal? WithholdingTaxOverride);
