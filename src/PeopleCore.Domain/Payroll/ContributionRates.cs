namespace PeopleCore.Domain.Payroll;

public record ContributionRates
{
    public decimal PhilHealthRate { get; init; } = 0.05m;
    public decimal PhilHealthMinShare { get; init; } = 250m;
    public decimal PhilHealthMaxShare { get; init; } = 2_500m;
    /// <summary>Employee rate above <see cref="PagIbigLowRateThreshold"/>.</summary>
    public decimal PagIbigEmployeeRate { get; init; } = 0.02m;
    /// <summary>Employee rate at or below <see cref="PagIbigLowRateThreshold"/>.</summary>
    public decimal PagIbigLowEmployeeRate { get; init; } = 0.01m;
    /// <summary>Fund salary at or below which the lower employee rate applies.</summary>
    public decimal PagIbigLowRateThreshold { get; init; } = 1_500m;
    /// <summary>Employer rate, which is flat across both tiers.</summary>
    public decimal PagIbigEmployerRate { get; init; } = 0.02m;
    /// <summary>Maximum Fund Salary (MFS): 10,000 since Pag-IBIG Circular 460, Feb 2024.</summary>
    public decimal PagIbigMaxFundSalary { get; init; } = 10_000m;
    public decimal? SSSEmployeeRate { get; init; } = null;   // null = use official table
    public decimal? SSSEmployerRate { get; init; } = null;
}
