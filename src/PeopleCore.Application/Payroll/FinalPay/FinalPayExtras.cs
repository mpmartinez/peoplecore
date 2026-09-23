using PeopleCore.Domain.Entities.Payroll;

namespace PeopleCore.Application.Payroll.FinalPay;

/// <summary>
/// The inputs a final-pay run adds on top of an ordinary period: a short period's working days,
/// the extra final-pay earnings (already computed - see <see cref="FinalPayMath"/>), the HR
/// deductions to withhold, and a settled tax figure.
/// </summary>
/// <param name="WorkingDays">
/// The final period's salary days - every calendar day under a daily-rate factor that pays rest
/// days (365), the scheduled days under one that doesn't (313, 261); never the days attended.
/// Prices the base period pay at the daily rate instead of a full salary share; absences and
/// tardiness still come off it through the attendance input.
/// </param>
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
/// <param name="ContributionsDeductedInMonth">
/// The SSS, PhilHealth and Pag-IBIG shares the separation month's Paid runs already deducted. The
/// final pay takes each share only up to the month's full contribution on the monthly basic -
/// never a semi-monthly half, never past the month. Null means nothing was deducted.
/// </param>
public sealed record FinalPayExtras(
    decimal WorkingDays,
    decimal LeaveConversionNonTaxable,
    decimal LeaveConversionTaxable,
    decimal SeparationPay,
    decimal RetirementPay,
    decimal SeparationAndRetirementNonTaxable,
    IReadOnlyList<(string Label, decimal Amount)> Deductions,
    decimal? WithholdingTaxOverride,
    ContributionShares? ContributionsDeductedInMonth = null);

/// <summary>SSS, PhilHealth and Pag-IBIG contributions, employee and employer shares.</summary>
public sealed record ContributionShares(
    decimal SssEmployee,
    decimal SssEmployer,
    decimal PhilHealthEmployee,
    decimal PhilHealthEmployer,
    decimal PagIbigEmployee,
    decimal PagIbigEmployer)
{
    public static readonly ContributionShares None = new(0m, 0m, 0m, 0m, 0m, 0m);

    /// <summary>The shares the entries deducted, added up.</summary>
    public static ContributionShares Sum(IEnumerable<PayrollRunEmployee> entries)
    {
        var list = entries.ToList();
        return new ContributionShares(
            list.Sum(e => e.SSSEmployee), list.Sum(e => e.SSSEmployer),
            list.Sum(e => e.PhilHealthEmployee), list.Sum(e => e.PhilHealthEmployer),
            list.Sum(e => e.PagIbigEmployee), list.Sum(e => e.PagIbigEmployer));
    }
}
