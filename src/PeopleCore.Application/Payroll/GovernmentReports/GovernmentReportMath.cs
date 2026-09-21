using System.Globalization;
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
    /// The part of a month's 13th month pay within the 90,000 exemption, after the 13th month paid
    /// earlier in the year has used its share, the same way payroll withheld tax on it.
    /// </summary>
    public static decimal NonTaxableThirteenthMonth(decimal thisMonth, decimal paidEarlierInYear)
        => Math.Min(thisMonth, Math.Max(0m, StatutoryCaps.ThirteenthMonthExemption - paidEarlierInYear));

    public static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
}
