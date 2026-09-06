namespace PeopleCore.Domain.Entities.Payroll;

/// <summary>
/// Per-company statutory rate configuration. Company identity lives on Company; only rates
/// live here. Null SSS rates mean "use the official schedule".
/// </summary>
public class PayrollSettings : AuditableEntity
{
    public Guid CompanyId { get; set; }
    public Organization.Company Company { get; set; } = null!;

    public decimal PhilHealthRate { get; set; } = 0.05m;
    public decimal PhilHealthMinShare { get; set; } = 250m;
    public decimal PhilHealthMaxShare { get; set; } = 2_500m;

    public decimal PagIbigEmployeeRate { get; set; } = 0.02m;
    public decimal PagIbigLowEmployeeRate { get; set; } = 0.01m;
    public decimal PagIbigLowRateThreshold { get; set; } = 1_500m;
    public decimal PagIbigEmployerRate { get; set; } = 0.02m;
    public decimal PagIbigMaxFundSalary { get; set; } = 10_000m;

    /// <summary>
    /// DOLE equivalent-monthly-rate factor for deriving the applicable daily rate
    /// (monthly x 12 / factor). 365 covers employees paid on unworked rest days, special days
    /// and regular holidays; 313 and 261 suit six- and five-day schedules.
    /// </summary>
    public decimal DailyRateFactor { get; set; } = 365m;

    public decimal? SSSEmployeeRate { get; set; }
    public decimal? SSSEmployerRate { get; set; }
}
