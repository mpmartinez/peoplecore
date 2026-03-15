using PeopleCore.Application.Analytics.DTOs;

namespace PeopleCore.Application.Analytics.Interfaces;

public interface IHRAnalyticsService
{
    Task<AnalyticsResponse<HeadcountByDepartment>> GetHeadcountAsync(DateOnly from, DateOnly to, Guid? departmentId = null, CancellationToken ct = default);
    Task<AnalyticsResponse<TurnoverData>> GetTurnoverAsync(DateOnly from, DateOnly to, string groupBy = "month", CancellationToken ct = default);
    Task<AnalyticsResponse<AttendanceRate>> GetAttendanceRateAsync(DateOnly from, DateOnly to, Guid? departmentId = null, CancellationToken ct = default);
    Task<AnalyticsResponse<LeaveUtilization>> GetLeaveUtilizationAsync(DateOnly from, DateOnly to, Guid? departmentId = null, CancellationToken ct = default);
    Task<AnalyticsResponse<OvertimeData>> GetOvertimeAsync(DateOnly from, DateOnly to, Guid? departmentId = null, CancellationToken ct = default);
    Task<AnalyticsResponse<RecruitmentFunnel>> GetRecruitmentFunnelAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<AnalyticsResponse<PerformanceDistribution>> GetPerformanceDistributionAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}
