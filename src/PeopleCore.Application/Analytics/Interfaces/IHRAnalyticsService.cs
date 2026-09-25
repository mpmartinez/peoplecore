using PeopleCore.Application.Analytics.DTOs;

namespace PeopleCore.Application.Analytics.Interfaces;

public interface IHRAnalyticsService
{
    Task<AnalyticsResponse<HeadcountByDepartment>> GetHeadcountAsync(DateOnly from, DateOnly to, Guid? departmentId = null, CancellationToken ct = default);
    Task<AnalyticsResponse<TurnoverData>> GetTurnoverAsync(DateOnly from, DateOnly to, string groupBy = "month", CancellationToken ct = default);
    Task<AnalyticsResponse<AttendanceRate>> GetAttendanceRateAsync(DateOnly from, DateOnly to, Guid? departmentId = null, CancellationToken ct = default);
    /// <summary>
    /// Leave balances grouped by type. Confidential types (VAWC), and balances whose type is not
    /// loaded, are left out unless <paramref name="showConfidentialTypes"/> - the caller holds
    /// <c>approvals.all</c>, which the controller decides.
    /// </summary>
    Task<AnalyticsResponse<LeaveUtilization>> GetLeaveUtilizationAsync(DateOnly from, DateOnly to, Guid? departmentId = null, bool showConfidentialTypes = false, CancellationToken ct = default);
    Task<AnalyticsResponse<OvertimeData>> GetOvertimeAsync(DateOnly from, DateOnly to, Guid? departmentId = null, CancellationToken ct = default);
    Task<AnalyticsResponse<RecruitmentFunnel>> GetRecruitmentFunnelAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<AnalyticsResponse<PerformanceDistribution>> GetPerformanceDistributionAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}
