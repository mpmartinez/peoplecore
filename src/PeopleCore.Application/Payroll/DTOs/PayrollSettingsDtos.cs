namespace PeopleCore.Application.Payroll.DTOs;

/// <summary>Per-company statutory rate configuration. Mirrors PayrollSettings/ContributionRates.</summary>
public record PayrollSettingsDto(
    Guid CompanyId,
    decimal PhilHealthRate,
    decimal PhilHealthMinShare,
    decimal PhilHealthMaxShare,
    decimal PagIbigEmployeeRate,
    decimal PagIbigLowEmployeeRate,
    decimal PagIbigLowRateThreshold,
    decimal PagIbigEmployerRate,
    decimal PagIbigMaxFundSalary,
    decimal DailyRateFactor,
    decimal? SSSEmployeeRate,
    decimal? SSSEmployerRate);
