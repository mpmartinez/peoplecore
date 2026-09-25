using PeopleCore.Application.Analytics.DTOs;

namespace PeopleCore.Application.Analytics.Interfaces;

public interface IExecutiveAnalyticsService
{
    Task<WorkforceSummary> GetWorkforceSummaryAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<IReadOnlyList<HiringTrend>> GetHiringTrendAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<IReadOnlyList<AttritionData>> GetAttritionRateAsync(DateOnly from, DateOnly to, string groupBy = "month", CancellationToken ct = default);
    /// <summary>
    /// Leave used, in total and by type. Confidential types are left out of both unless
    /// <paramref name="showConfidentialTypes"/> - the caller holds <c>approvals.all</c>.
    /// </summary>
    Task<LeaveSummary> GetLeaveSummaryAsync(DateOnly from, DateOnly to, bool showConfidentialTypes = false, CancellationToken ct = default);
    Task<IReadOnlyList<PerformanceOverview>> GetPerformanceOverviewAsync(Guid? reviewCycleId = null, CancellationToken ct = default);
}
