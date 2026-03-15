using PeopleCore.Application.Analytics.DTOs;

namespace PeopleCore.Application.Analytics.Interfaces;

public interface IExecutiveAnalyticsService
{
    Task<WorkforceSummary> GetWorkforceSummaryAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<IReadOnlyList<HiringTrend>> GetHiringTrendAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<IReadOnlyList<AttritionData>> GetAttritionRateAsync(DateOnly from, DateOnly to, string groupBy = "month", CancellationToken ct = default);
    Task<LeaveSummary> GetLeaveSummaryAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<IReadOnlyList<PerformanceOverview>> GetPerformanceOverviewAsync(Guid? reviewCycleId = null, CancellationToken ct = default);
}
