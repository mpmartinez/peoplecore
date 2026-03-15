using PeopleCore.Application.Analytics.DTOs;
using PeopleCore.Application.Analytics.Interfaces;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Performance.Interfaces;

namespace PeopleCore.Application.Analytics.Services;

public class ExecutiveAnalyticsService : IExecutiveAnalyticsService
{
    private readonly IEmployeeRepository _employeeRepo;
    private readonly ILeaveBalanceRepository _leaveBalanceRepo;
    private readonly IPerformanceReviewRepository _performanceRepo;
    private readonly IHRAnalyticsService _hrAnalytics;

    public ExecutiveAnalyticsService(
        IEmployeeRepository employeeRepo,
        ILeaveBalanceRepository leaveBalanceRepo,
        IPerformanceReviewRepository performanceRepo,
        IHRAnalyticsService hrAnalytics)
    {
        _employeeRepo = employeeRepo;
        _leaveBalanceRepo = leaveBalanceRepo;
        _performanceRepo = performanceRepo;
        _hrAnalytics = hrAnalytics;
    }

    public async Task<WorkforceSummary> GetWorkforceSummaryAsync(CancellationToken ct = default)
    {
        var employees = await _employeeRepo.GetAllAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var totalActive = employees.Count(e => e.SeparationDate == null);
        var totalInactive = employees.Count(e => e.SeparationDate != null);

        // Get headcount breakdown using a wide date range
        var yearStart = new DateOnly(today.Year, 1, 1);
        var headcountResponse = await _hrAnalytics.GetHeadcountAsync(yearStart, today, ct: ct);

        return new WorkforceSummary(totalActive, totalInactive, headcountResponse.Data);
    }

    public async Task<IReadOnlyList<HiringTrend>> GetHiringTrendAsync(
        int months = 12, CancellationToken ct = default)
    {
        var employees = await _employeeRepo.GetAllAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var result = new List<HiringTrend>();

        for (int i = months - 1; i >= 0; i--)
        {
            var monthStart = new DateOnly(today.Year, today.Month, 1).AddMonths(-i);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);

            var newHires = employees.Count(e => e.HireDate >= monthStart && e.HireDate <= monthEnd);
            result.Add(new HiringTrend(monthStart.ToString("yyyy-MM"), newHires));
        }

        return result;
    }

    public async Task<IReadOnlyList<AttritionData>> GetAttritionRateAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var employees = await _employeeRepo.GetAllAsync(ct);
        var result = new List<AttritionData>();

        var current = new DateOnly(from.Year, from.Month, 1);
        var end = new DateOnly(to.Year, to.Month, 1);

        while (current <= end)
        {
            var monthEnd = current.AddMonths(1).AddDays(-1);

            var activeAtStart = employees.Count(e =>
                e.HireDate <= current &&
                (e.SeparationDate == null || e.SeparationDate > current));
            var activeAtEnd = employees.Count(e =>
                e.HireDate <= monthEnd &&
                (e.SeparationDate == null || e.SeparationDate > monthEnd));
            var avgHeadcount = (activeAtStart + activeAtEnd) / 2;

            var separations = employees.Count(e =>
                e.SeparationDate.HasValue &&
                e.SeparationDate.Value >= current &&
                e.SeparationDate.Value <= monthEnd);

            var attritionRate = avgHeadcount > 0
                ? Math.Round((decimal)separations / avgHeadcount * 100, 2)
                : 0m;

            result.Add(new AttritionData(current.ToString("yyyy-MM"), attritionRate, separations, avgHeadcount));

            current = current.AddMonths(1);
        }

        return result;
    }

    public async Task<LeaveSummary> GetLeaveSummaryAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var leaveResponse = await _hrAnalytics.GetLeaveUtilizationAsync(from, to, ct: ct);
        var byType = leaveResponse.Data;

        var totalDaysConsumed = byType.Sum(l => l.TotalUsed);

        // Estimate average per employee
        var employees = await _employeeRepo.GetAllAsync(ct);
        var activeCount = employees.Count(e => e.SeparationDate == null);
        var avgPerEmployee = activeCount > 0
            ? Math.Round(totalDaysConsumed / activeCount, 2)
            : 0m;

        return new LeaveSummary(totalDaysConsumed, avgPerEmployee, byType);
    }

    public async Task<IReadOnlyList<PerformanceOverview>> GetPerformanceOverviewAsync(
        Guid? reviewCycleId = null, CancellationToken ct = default)
    {
        var reviews = await _performanceRepo.GetAllAsync(ct);

        if (reviewCycleId.HasValue)
            reviews = reviews.Where(r => r.ReviewCycleId == reviewCycleId.Value).ToList();

        var scored = reviews.Where(r => r.FinalScore.HasValue).ToList();

        return scored
            .GroupBy(r => r.Employee?.Department?.Name ?? "Unassigned")
            .Select(g => new PerformanceOverview(
                g.Key,
                Math.Round(g.Average(r => r.FinalScore!.Value), 2),
                g.First().ReviewCycle?.Name ?? "Unknown"))
            .OrderBy(d => d.Department)
            .ToList();
    }
}
