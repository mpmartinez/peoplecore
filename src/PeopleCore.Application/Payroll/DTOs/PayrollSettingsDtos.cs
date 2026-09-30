namespace PeopleCore.Application.Payroll.DTOs;

/// <summary>Per-company statutory rate configuration. Mirrors PayrollSettings/ContributionRates.</summary>
/// <remarks>
/// The web client keeps a copy, <c>PayrollSettingsDto</c> in <c>src/PeopleCore.Web/Services/ApiClient.cs</c>,
/// and <c>SettingsFields</c> in <c>tests/PeopleCore.Web.Tests/Services/ApiClientMaternityTests.cs</c> lists
/// these members by name and in order. The PUT replaces the whole record, so a member added here and
/// not there is reset to its default by every save from the payroll settings page: add a new setting
/// to both, in the same place.
/// </remarks>
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
    decimal? SSSEmployerRate,
    bool ExemptFromMaternityDifferential = false);
