using System.Globalization;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Payroll;

namespace PeopleCore.Application.Payroll.GovernmentReports;

/// <summary>The figures the reports work out that payroll entries do not store.</summary>
public static class GovernmentReportMath
{
    /// <summary>
    /// The monthly salary credit and EC behind a month's SSS employee share, under Circular
    /// 2024-006: the employee pays 5% of the MSC, and EC is 10.00 below an MSC of 15,000 and 30.00
    /// from there up. Blank when nothing was deducted.
    /// </summary>
    public static (decimal? Msc, decimal? Ec) SssCredit(decimal monthlyEmployeeShare)
    {
        if (monthlyEmployeeShare <= 0m)
            return (null, null);

        decimal msc = Math.Round(monthlyEmployeeShare / 0.05m / 500m, MidpointRounding.AwayFromZero) * 500m;
        return (msc, msc < 15_000m ? 10m : 30m);
    }

    /// <summary>
    /// Whether an employee's SSS cutoffs in a month (the frequency and type of each Paid run whose
    /// period ends in the month and deducted SSS from them) add up to the whole month, so the MSC
    /// can be worked back from the share. Payroll deducts half the month's SSS in each semi-monthly
    /// cutoff and all of it in a monthly run; a single cutoff's half share would understate the MSC
    /// and the EC that goes with it. Cutoffs are counted rather than calendar days checked, because
    /// many companies' cutoffs (26th-10th, 21st-20th) never cover the end of the month they close
    /// in. A final pay completes the month whatever its frequency: it tops the month's SSS up to
    /// the whole month.
    /// </summary>
    public static bool IsFullSssMonth(IEnumerable<(PayFrequency Frequency, PayrollRunType RunType)> cutoffs)
    {
        var list = cutoffs.ToList();
        return list.Any(c => c.RunType == PayrollRunType.FinalPay)
               || list.Any(c => c.Frequency == PayFrequency.Monthly)
               || list.Count(c => c.Frequency == PayFrequency.SemiMonthly) >= 2;
    }

    /// <summary>
    /// Whether the payroll settings override both SSS rates. Working the MSC and EC back from a
    /// share only holds under the statutory schedule, which is what ComputeSSS uses unless both
    /// rates are overridden.
    /// </summary>
    public static bool SssRatesOverridden(PayrollSettings? settings)
        => settings?.SSSEmployeeRate is not null && settings.SSSEmployerRate is not null;

    /// <summary>
    /// The part of a month's 13th month and other benefits (the 13th month plus leave converted
    /// beyond de minimis) within the 90,000 exemption, after what was paid earlier in the year has
    /// used its share, the same way the 2316 splits the year.
    /// </summary>
    public static decimal NonTaxableThirteenthMonth(decimal thisMonth, decimal paidEarlierInYear)
        => Math.Min(thisMonth, Math.Max(0m, StatutoryCaps.ThirteenthMonthExemption - paidEarlierInYear));

    public static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
}
