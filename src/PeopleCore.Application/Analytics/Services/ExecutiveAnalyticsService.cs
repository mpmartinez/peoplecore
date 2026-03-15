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

    public async Task<WorkforceSummary> GetWorkforceSummaryAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var employees = await _employeeRepo.GetAllAsync(ct);

        var totalActive = employees.Count(e => e.SeparationDate == null);
        var totalInactive = employees.Count(e => e.SeparationDate != null);

        var headcountResponse = await _hrAnalytics.GetHeadcountAsync(from, to, ct: ct);

        return new WorkforceSummary(totalActive, totalInactive, headcountResponse.Data);
    }

    public async Task<IReadOnlyList<HiringTrend>> GetHiringTrendAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var employees = await _employeeRepo.GetAllAsync(ct);

        var result = new List<HiringTrend>();

        var current = new DateOnly(from.Year, from.Month, 1);
        var end = new DateOnly(to.Year, to.Month, 1);

        while (current <= end)
        {
            var monthEnd = current.AddMonths(1).AddDays(-1);
            if (monthEnd > to) monthEnd = to;

            var newHires = employees.Count(e => e.HireDate >= current && e.HireDate <= monthEnd);
            result.Add(new HiringTrend(current.ToString("yyyy-MM"), newHires));

            current = current.AddMonths(1);
        }

        return result;
    }

    public async Task<IReadOnlyList<AttritionData>> GetAttritionRateAsync(
        DateOnly from, DateOnly to, string groupBy = "month", CancellationToken ct = default)
    {
        var employees = await _employeeRepo.GetAllAsync(ct);
        var result = new List<AttritionData>();

        var current = new DateOnly(from.Year, from.Month, 1);
        var end = new DateOnly(to.Year, to.Month, 1);

        int increment = groupBy switch
        {
            "quarter" => 3,
            "year" => 12,
            _ => 1
        };

        while (current <= end)
        {
            var periodEnd = current.AddMonths(increment).AddDays(-1);
            if (periodEnd > to) periodEnd = to;

            var activeAtStart = employees.Count(e =>
                e.HireDate <= current &&
                (e.SeparationDate == null || e.SeparationDate > current));
            var activeAtEnd = employees.Count(e =>
                e.HireDate <= periodEnd &&
                (e.SeparationDate == null || e.SeparationDate > periodEnd));
            var avgHeadcount = (activeAtStart + activeAtEnd) / 2;

            var separations = employees.Count(e =>
                e.SeparationDate.HasValue &&
                e.SeparationDate.Value >= current &&
                e.SeparationDate.Value <= periodEnd);

            var attritionRate = avgHeadcount > 0
                ? Math.Round((decimal)separations / avgHeadcount * 100, 2)
                : 0m;

            var label = groupBy switch
            {
                "quarter" => $"{current.Year}-Q{(current.Month - 1) / 3 + 1}",
                "year" => current.Year.ToString(),
                _ => current.ToString("yyyy-MM")
            };

            result.Add(new AttritionData(label, attritionRate, separations, avgHeadcount));

            current = current.AddMonths(increment);
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
            .GroupBy(r => (Dept: r.Employee?.Department?.Name ?? "Unassigned", Cycle: r.ReviewCycle?.Name ?? "Unknown"))
            .Select(g => new PerformanceOverview(
                g.Key.Dept,
                Math.Round(g.Average(r => r.FinalScore!.Value), 2),
                g.Key.Cycle))
            .OrderBy(d => d.Department)
            .ThenBy(d => d.Cycle)
            .ToList();
    }
}
