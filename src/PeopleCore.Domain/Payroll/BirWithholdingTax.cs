namespace PeopleCore.Domain.Payroll;

/// <summary>
/// BIR TRAIN Law revised withholding tax table (2023 onwards), expressed as annual brackets.
/// <para>
/// This is the single source of the annual bracket table. Both the per-period withholding
/// calculation and BIR Form 2316 Item 24 (Tax Due) resolve through here, so an employee's
/// annual liability and the tax withheld from their payslips can never drift apart because
/// one copy of the table was updated and another was not.
/// </para>
/// </summary>
public static class BirWithholdingTax
{
    // Annual income brackets: over MinAnnual up to MaxAnnual, tax is BaseAnnualTax plus
    // ExcessRate applied to the amount over MinAnnual.
    private static readonly (decimal MinAnnual, decimal MaxAnnual, decimal BaseAnnualTax, decimal ExcessRate)[] Brackets =
    [
        (0m,           250_000m,        0m,          0.00m),
        (250_000m,     400_000m,        0m,          0.15m),
        (400_000m,     800_000m,       22_500m,      0.20m),
        (800_000m,   2_000_000m,      102_500m,      0.25m),
        (2_000_000m, 8_000_000m,      402_500m,      0.30m),
        (8_000_000m, decimal.MaxValue, 2_202_500m,   0.35m)
    ];

    /// <summary>
    /// Annual income tax due on <paramref name="annualTaxableIncome"/>, unrounded.
    /// </summary>
    public static decimal ComputeAnnualTax(decimal annualTaxableIncome)
    {
        decimal annualTax = 0m;
        foreach (var (min, max, baseTax, rate) in Brackets)
        {
            if (annualTaxableIncome > min)
            {
                decimal excess = Math.Min(annualTaxableIncome, max == decimal.MaxValue ? annualTaxableIncome : max) - min;
                annualTax = baseTax + (excess * rate);
            }
        }

        return annualTax;
    }

    /// <summary>
    /// Annual income tax due, rounded to centavos. This is BIR Form 2316 Item 24 (Tax Due):
    /// the liability computed on Item 23, which is deliberately NOT the amount withheld. The
    /// two are equal only when withholding was correct, and that equality is precisely what
    /// qualifies an employee for substituted filing.
    /// </summary>
    public static decimal ComputeAnnualTaxDue(decimal annualTaxableIncome) =>
        Math.Round(ComputeAnnualTax(annualTaxableIncome), 2);
}
