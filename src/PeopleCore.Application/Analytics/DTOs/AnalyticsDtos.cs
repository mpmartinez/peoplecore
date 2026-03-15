namespace PeopleCore.Application.Analytics.DTOs;

// Common response wrapper
public record AnalyticsResponse<T>(
    AnalyticsPeriod Period,
    IReadOnlyList<T> Data,
    DateTime GeneratedAt);

public record AnalyticsPeriod(DateOnly From, DateOnly To);

// Data point for charts
public record AnalyticsDataPoint(string Label, decimal Value, string? Trend = null);

// HR Analytics
public record HeadcountByDepartment(string Department, int Active, int Inactive, int Total);
public record TurnoverData(string Period, int NewHires, int Separations, decimal TurnoverRate);
public record AttendanceRate(string Department, decimal OnTimeRate, decimal LateRate, decimal AbsentRate);
public record LeaveUtilization(string LeaveType, decimal TotalAllocated, decimal TotalUsed, decimal UtilizationRate);
public record OvertimeData(string Department, decimal TotalHours, decimal AverageHoursPerEmployee);
public record RecruitmentFunnel(string Stage, int Count, decimal ConversionRate);
public record PerformanceDistribution(string ScoreRange, int Count, decimal Percentage);

// Executive Analytics
public record WorkforceSummary(int TotalActive, int TotalInactive, IReadOnlyList<HeadcountByDepartment> ByDepartment);
public record HiringTrend(string Month, int NewHires);
public record AttritionData(string Period, decimal AttritionRate, int Separations, int AverageHeadcount);
public record LeaveSummary(decimal TotalDaysConsumed, decimal AverageDaysPerEmployee, IReadOnlyList<LeaveUtilization> ByType);
public record PerformanceOverview(string Department, decimal AverageScore, string Cycle);
