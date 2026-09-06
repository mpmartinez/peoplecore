namespace PeopleCore.Domain.Payroll;

/// <summary>
/// Statutory caps that change by legislation rather than by company policy, so they live beside
/// the tax tables rather than in configuration.
/// </summary>
public static class StatutoryCaps
{
    /// <summary>
    /// Thirteenth-month pay and other benefits are exempt up to this amount per year
    /// (TRAIN, RA 10963). Anything above it is taxable compensation and lands in Item 48.
    /// </summary>
    public const decimal ThirteenthMonthExemption = 90_000m;
}
