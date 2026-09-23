using PeopleCore.Domain.Enums;

namespace PeopleCore.Application.Payroll.FinalPay;

/// <summary>The statutory final-pay figures, with no data access.</summary>
public static class FinalPayMath
{
    /// <summary>RR 11-2018 (as amended): monetized unused vacation leave up to 10 days a year is de minimis.</summary>
    public const decimal DeMinimisVacationDays = 10m;

    /// <summary>RA 7641: 15 days' pay + 5 days of SIL + 1/12 of the 13th month (2.5 days).</summary>
    public const decimal RetirementDaysPerYear = 22.5m;

    /// <summary>Whole years from hire to the last working day; a fraction of at least six months counts as a year.</summary>
    public static int ServiceYears(DateOnly hireDate, DateOnly lastWorkingDay)
    {
        if (lastWorkingDay < hireDate) return 0;
        int years = lastWorkingDay.Year - hireDate.Year;
        if (hireDate.AddYears(years) > lastWorkingDay) years--;
        var afterWholeYears = hireDate.AddYears(years);
        return afterWholeYears.AddMonths(6) <= lastWorkingDay ? years + 1 : years;
    }

    /// <summary>
    /// Labor Code Art. 298-299: a month's pay per year of service for redundancy and labor-saving
    /// devices, half a month for retrenchment, closure not due to losses and disease, never less
    /// than a month's pay; nothing for closure due to serious losses.
    /// </summary>
    public static decimal SeparationPay(AuthorizedCause cause, decimal monthlyBasic, int serviceYears)
    {
        decimal perYear = cause switch
        {
            AuthorizedCause.Redundancy or AuthorizedCause.LaborSavingDevices => 1m,
            AuthorizedCause.Retrenchment or AuthorizedCause.ClosureNotDueToLosses or AuthorizedCause.Disease => 0.5m,
            _ => 0m
        };
        if (perYear == 0m) return 0m;
        return Math.Round(Math.Max(monthlyBasic, monthlyBasic * perYear * serviceYears), 2);
    }

    /// <summary>RA 7641: 60 to 65 years old and at least five years of service.</summary>
    public static bool IsRetirementEligible(DateOnly dateOfBirth, DateOnly lastWorkingDay, int serviceYears)
    {
        int age = lastWorkingDay.Year - dateOfBirth.Year;
        if (dateOfBirth.AddYears(age) > lastWorkingDay) age--;
        return age is >= 60 and <= 65 && serviceYears >= 5;
    }

    public static decimal RetirementPay(decimal dailyRate, int serviceYears)
        => Math.Round(dailyRate * RetirementDaysPerYear * serviceYears, 2);

    /// <summary>
    /// Unused convertible leave at the daily rate, split into the de minimis part (the first ten
    /// vacation-type days) and the taxable rest.
    /// </summary>
    public static (decimal NonTaxable, decimal Taxable) LeaveConversion(
        IEnumerable<(decimal Days, bool CountsAsVacation)> balances, decimal dailyRate)
    {
        var positive = balances.Where(b => b.Days > 0m).ToList();
        decimal vacationDays = positive.Where(b => b.CountsAsVacation).Sum(b => b.Days);
        decimal otherDays = positive.Where(b => !b.CountsAsVacation).Sum(b => b.Days);
        decimal deMinimisDays = Math.Min(vacationDays, DeMinimisVacationDays);
        return (Math.Round(deMinimisDays * dailyRate, 2),
                Math.Round((vacationDays - deMinimisDays + otherDays) * dailyRate, 2));
    }
}
