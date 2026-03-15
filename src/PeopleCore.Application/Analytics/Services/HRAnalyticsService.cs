using PeopleCore.Application.Analytics.DTOs;
using PeopleCore.Application.Analytics.Interfaces;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Performance.Interfaces;
using PeopleCore.Application.Recruitment.Interfaces;

namespace PeopleCore.Application.Analytics.Services;

public class HRAnalyticsService : IHRAnalyticsService
{
    private readonly IEmployeeRepository _employeeRepo;
    private readonly IAttendanceRepository _attendanceRepo;
    private readonly IOvertimeRepository _overtimeRepo;
    private readonly ILeaveBalanceRepository _leaveBalanceRepo;
    private readonly IApplicantRepository _applicantRepo;
    private readonly IPerformanceReviewRepository _performanceRepo;

    public HRAnalyticsService(
        IEmployeeRepository employeeRepo,
        IAttendanceRepository attendanceRepo,
        IOvertimeRepository overtimeRepo,
        ILeaveBalanceRepository leaveBalanceRepo,
        IApplicantRepository applicantRepo,
        IPerformanceReviewRepository performanceRepo)
    {
        _employeeRepo = employeeRepo;
        _attendanceRepo = attendanceRepo;
        _overtimeRepo = overtimeRepo;
        _leaveBalanceRepo = leaveBalanceRepo;
        _applicantRepo = applicantRepo;
        _performanceRepo = performanceRepo;
    }

    public async Task<AnalyticsResponse<HeadcountByDepartment>> GetHeadcountAsync(
        DateOnly from, DateOnly to, Guid? departmentId = null, CancellationToken ct = default)
    {
        var employees = await _employeeRepo.GetAllAsync(ct);

        // Filter to employees who were active at any point in the period
        var filtered = employees
            .Where(e => e.HireDate <= to && (e.SeparationDate == null || e.SeparationDate >= from));

        if (departmentId.HasValue)
            filtered = filtered.Where(e => e.DepartmentId == departmentId.Value);

        var data = filtered
            .GroupBy(e => e.Department?.Name ?? "Unassigned")
            .Select(g =>
            {
                var active = g.Count(e => e.SeparationDate == null);
                var inactive = g.Count(e => e.SeparationDate != null);
                return new HeadcountByDepartment(g.Key, active, inactive, active + inactive);
            })
            .OrderBy(h => h.Department)
            .ToList();

        return Wrap(from, to, data);
    }

    public async Task<AnalyticsResponse<TurnoverData>> GetTurnoverAsync(
        DateOnly from, DateOnly to, string groupBy = "month", CancellationToken ct = default)
    {
        var employees = await _employeeRepo.GetAllAsync(ct);
        var data = new List<TurnoverData>();

        var current = new DateOnly(from.Year, from.Month, 1);
        var end = new DateOnly(to.Year, to.Month, 1);

        int increment = groupBy == "quarter" ? 3 : 1;

        while (current <= end)
        {
            var periodEnd = current.AddMonths(increment).AddDays(-1);
            if (periodEnd > to) periodEnd = to;

            var activeAtStart = employees.Count(e =>
                e.HireDate <= current &&
                (e.SeparationDate == null || e.SeparationDate > current));

            var newHires = employees.Count(e => e.HireDate >= current && e.HireDate <= periodEnd);
            var separations = employees.Count(e =>
                e.SeparationDate.HasValue &&
                e.SeparationDate.Value >= current &&
                e.SeparationDate.Value <= periodEnd);

            var avgHeadcount = activeAtStart > 0 ? activeAtStart : 1;
            var turnoverRate = Math.Round((decimal)separations / avgHeadcount * 100, 2);

            var label = groupBy == "quarter"
                ? $"{current.Year}-Q{(current.Month - 1) / 3 + 1}"
                : current.ToString("yyyy-MM");

            data.Add(new TurnoverData(label, newHires, separations, turnoverRate));
            current = current.AddMonths(increment);
        }

        return Wrap(from, to, data);
    }

    public async Task<AnalyticsResponse<AttendanceRate>> GetAttendanceRateAsync(
        DateOnly from, DateOnly to, Guid? departmentId = null, CancellationToken ct = default)
    {
        var records = await _attendanceRepo.GetAllByPeriodAsync(from, to, ct);

        var filtered = records.AsEnumerable();
        if (departmentId.HasValue)
            filtered = filtered.Where(r => r.Employee?.DepartmentId == departmentId.Value);

        var data = filtered
            .GroupBy(r => r.Employee?.Department?.Name ?? "Unassigned")
            .Select(g =>
            {
                var total = g.Count();
                var onTime = g.Count(r => r.IsPresent && r.LateMinutes == 0);
                var late = g.Count(r => r.IsPresent && r.LateMinutes > 0);
                var absent = g.Count(r => !r.IsPresent);

                return new AttendanceRate(
                    g.Key,
                    total > 0 ? Math.Round((decimal)onTime / total * 100, 2) : 0m,
                    total > 0 ? Math.Round((decimal)late / total * 100, 2) : 0m,
                    total > 0 ? Math.Round((decimal)absent / total * 100, 2) : 0m);
            })
            .OrderBy(a => a.Department)
            .ToList();

        return Wrap(from, to, data);
    }

    public async Task<AnalyticsResponse<LeaveUtilization>> GetLeaveUtilizationAsync(
        DateOnly from, DateOnly to, Guid? departmentId = null, CancellationToken ct = default)
    {
        var balances = await _leaveBalanceRepo.GetByYearAsync(from.Year, ct);

        var filtered = balances.AsEnumerable();
        if (departmentId.HasValue)
            filtered = filtered.Where(b => b.Employee?.DepartmentId == departmentId.Value);

        var data = filtered
            .GroupBy(b => b.LeaveType?.Name ?? "Unknown")
            .Select(g =>
            {
                var totalAllocated = g.Sum(b => b.TotalDays + b.CarriedOverDays);
                var totalUsed = g.Sum(b => b.UsedDays);
                var utilizationRate = totalAllocated > 0
                    ? Math.Round(totalUsed / totalAllocated * 100, 2)
                    : 0m;

                return new LeaveUtilization(g.Key, totalAllocated, totalUsed, utilizationRate);
            })
            .OrderBy(l => l.LeaveType)
            .ToList();

        return Wrap(from, to, data);
    }

    public async Task<AnalyticsResponse<OvertimeData>> GetOvertimeAsync(
        DateOnly from, DateOnly to, Guid? departmentId = null, CancellationToken ct = default)
    {
        var overtimeRecords = await _overtimeRepo.GetApprovedByPeriodAsync(from, to, ct);

        var filtered = overtimeRecords.AsEnumerable();
        if (departmentId.HasValue)
            filtered = filtered.Where(o => o.Employee?.DepartmentId == departmentId.Value);

        var data = filtered
            .GroupBy(o => o.Employee?.Department?.Name ?? "Unassigned")
            .Select(g =>
            {
                var totalMinutes = g.Sum(o => o.TotalMinutes);
                var totalHours = Math.Round(totalMinutes / 60m, 2);
                var employeeCount = g.Select(o => o.EmployeeId).Distinct().Count();
                var avgHours = employeeCount > 0
                    ? Math.Round(totalHours / employeeCount, 2)
                    : 0m;

                return new OvertimeData(g.Key, totalHours, avgHours);
            })
            .OrderBy(o => o.Department)
            .ToList();

        return Wrap(from, to, data);
    }

    public async Task<AnalyticsResponse<RecruitmentFunnel>> GetRecruitmentFunnelAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var applicants = await _applicantRepo.GetAllAsync(ct);

        // Filter by application date within range
        var filtered = applicants
            .Where(a => DateOnly.FromDateTime(a.AppliedAt) >= from && DateOnly.FromDateTime(a.AppliedAt) <= to)
            .ToList();

        var totalApplicants = filtered.Count;

        var data = filtered
            .GroupBy(a => a.Status.ToString())
            .Select(g =>
            {
                var count = g.Count();
                var conversionRate = totalApplicants > 0
                    ? Math.Round((decimal)count / totalApplicants * 100, 2)
                    : 0m;
                return new RecruitmentFunnel(g.Key, count, conversionRate);
            })
            .OrderBy(r => r.Stage)
            .ToList();

        return Wrap(from, to, data);
    }

    public async Task<AnalyticsResponse<PerformanceDistribution>> GetPerformanceDistributionAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var reviews = await _performanceRepo.GetAllAsync(ct);

        // Filter reviews completed within the period
        var filtered = reviews
            .Where(r => r.CompletedAt.HasValue &&
                        DateOnly.FromDateTime(r.CompletedAt.Value) >= from &&
                        DateOnly.FromDateTime(r.CompletedAt.Value) <= to)
            .Where(r => r.FinalScore.HasValue)
            .ToList();

        var totalScored = filtered.Count;

        var bands = new (string Name, decimal Min, decimal Max)[]
        {
            ("0-1", 0m, 1m),
            ("1-2", 1m, 2m),
            ("2-3", 2m, 3m),
            ("3-4", 3m, 4m),
            ("4-5", 4m, 5m)
        };

        var data = bands.Select(band =>
        {
            var count = band.Max == 5m
                ? filtered.Count(r => r.FinalScore!.Value >= band.Min && r.FinalScore!.Value <= band.Max)
                : filtered.Count(r => r.FinalScore!.Value >= band.Min && r.FinalScore!.Value < band.Max);

            var percentage = totalScored > 0
                ? Math.Round((decimal)count / totalScored * 100, 2)
                : 0m;

            return new PerformanceDistribution(band.Name, count, percentage);
        }).ToList();

        return Wrap(from, to, data);
    }

    private static AnalyticsResponse<T> Wrap<T>(DateOnly from, DateOnly to, List<T> data) =>
        new(new AnalyticsPeriod(from, to), data, DateTime.UtcNow);
}
