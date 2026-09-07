namespace PeopleCore.Domain.Payroll;

/// <summary>
/// Sanity bounds for caller-supplied payroll input.
/// <para>
/// Deliberately a separate file from <see cref="StatutoryCaps"/> and the other Phase 1 statutory
/// tables. Those carry legislated figures and are byte-verified against their originals; these are
/// engineering limits chosen to catch a fat-fingered extra zero. Mixing them would misrepresent
/// both - a reader would not know which numbers Congress set and which we did.
/// </para>
/// <para>
/// A bound that is too tight fails loudly with a message naming the limit, and is changed here in
/// one place. That is the trade being made against the silent corruption of having no bound.
/// </para>
/// </summary>
public static class PayrollInputLimits
{
    /// <summary>
    /// Ceiling for <c>EmployeeCompensation.BasicSalary</c>, which is a MONTHLY figure regardless of
    /// pay frequency - <c>PayrollComputationService</c> derives both
    /// <c>dailyRate = BasicSalary * 12 / factor</c> and
    /// <c>basePeriodPay = BasicSalary / periodsPerMonth</c> from it. Far above any realistic
    /// Philippine payroll, and still low enough to catch an extra zero on any salary up to a
    /// million.
    /// </summary>
    public const decimal MaxMonthlyBasicSalary = 10_000_000m;

    /// <summary>
    /// Ceiling for the annual figures on Form 2316: twelve times
    /// <see cref="MaxMonthlyBasicSalary"/>, since these cover a whole tax year.
    /// </summary>
    public const decimal MaxAnnualAmount = 120_000_000m;

    public const int MaxDependents = 20;

    /// <summary>Matches <c>EmployeeCompensationConfiguration</c>'s <c>HasMaxLength(8)</c>.</summary>
    public const int MaxTaxCodeLength = 8;

    /// <summary>
    /// Matches <c>CompanyConfiguration</c>'s <c>HasMaxLength(200)</c> on <c>Company.Name</c>.
    /// </summary>
    public const int MaxEmployerNameLength = 200;

    public const int MaxEmployerAddressLength = 200;

    /// <summary>The days a month can physically contain.</summary>
    public const decimal MaxDaysInPeriod = 31m;

    /// <summary>31 x 24 - the hours a month physically contains.</summary>
    public const decimal MaxOvertimeHoursInPeriod = 744m;

    public const int MaxSemiMonthlyPeriodDays = 16;

    public const int MaxMonthlyPeriodDays = 31;
}
