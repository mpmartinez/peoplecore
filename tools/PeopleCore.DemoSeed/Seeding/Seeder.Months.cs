using System.Text.Json.Nodes;
using PeopleCore.DemoSeed.Api;
using PeopleCore.DemoSeed.Plan;

namespace PeopleCore.DemoSeed.Seeding;

public sealed partial class Seeder
{
    private static readonly Dictionary<string, string[]> LeaveReasons = new()
    {
        ["VL"] = ["Family trip to the province", "Personal errands", "Child's school event", "Short vacation"],
        ["SL"] = ["Fever and flu", "Medical check-up", "Dental appointment", "Migraine"],
    };

    private const string RejectionReason = "Too many of the team are already off that week. Please pick other dates.";

    private partial async Task RunMonthAsync(int month)
    {
        var rng = new Random(unchecked(plan.Seed * 31 + month));
        var admin = await AdminAsync();

        await api.PostAsync($"Run {month:00}/2026 leave accruals", $"api/leave-accruals/run-manual?year=2026&month={month}", null, admin);

        foreach (var filing in plan.Leave.Where(f => f.FiledInMonth == month))
            await FileLeaveAsync(filing, rng);

        foreach (var filing in plan.Overtime.Where(o => o.Date.Month == month))
            await FileOvertimeAsync(filing);

        var rows = plan.AttendanceFor(month);
        if (rows.Count > 0)
        {
            var monthStart = new DateOnly(2026, month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var midpoint = new DateOnly(2026, month, 15);

            await SyncAttendanceAsync(rows.Where(r => r.Date <= midpoint).ToList(), monthStart, midpoint);
            await SyncAttendanceAsync(rows.Where(r => r.Date > midpoint).ToList(), midpoint.AddDays(1), monthEnd);
        }

        foreach (var period in plan.PayPeriods.Where(p => p.End.Month == month))
            await RunPayrollAsync(period);

        Say($"{new DateOnly(2026, month, 1):MMMM}: done.");
    }

    private async Task FileLeaveAsync(LeaveFiling filing, Random rng)
    {
        var person = plan.PersonNumber(filing.PersonNumber);
        var step = $"{person.EmployeeNumber} files {filing.TypeCode} for {filing.Start:yyyy-MM-dd}";
        var reasons = LeaveReasons[filing.TypeCode];

        var id = IdOf(await api.PostAsync(step, "api/leave-requests", new
        {
            employeeId = EmployeeId(person.Number), leaveTypeId = _leaveTypeIds[filing.TypeCode],
            startDate = filing.Start, endDate = filing.End, reason = reasons[rng.Next(reasons.Length)],
        }, await TokenOfAsync(person.Number)));

        var approver = await TokenOfAsync(person.ManagerNumber ?? ClientPerson);
        switch (filing.Decision)
        {
            case Decision.Approved:
                await api.PutAsync($"{step}: approve", $"api/leave-requests/{id}/approve", null, approver);
                Count("leave approved");
                break;
            case Decision.Rejected:
                await api.PutAsync($"{step}: reject", $"api/leave-requests/{id}/reject", new { rejectionReason = RejectionReason }, approver);
                Count("leave rejected");
                break;
            default:
                Count("leave pending");
                break;
        }
    }

    private async Task FileOvertimeAsync(OvertimeFiling filing)
    {
        var person = plan.PersonNumber(filing.PersonNumber);
        var step = $"{person.EmployeeNumber} files overtime for {filing.Date:yyyy-MM-dd}";

        // Checked before filing, so a plan that can't be approved never leaves a stray request behind.
        int? approver = filing.Decision != Decision.Approved ? null
            : person.ManagerNumber ?? throw new SeedException(step, "POST", "api/overtime-requests", 0,
                $"{person.EmployeeNumber} has no manager to approve their overtime. Nothing was filed for this day.");

        var id = IdOf(await api.PostAsync(step, "api/overtime-requests", new
        {
            employeeId = EmployeeId(person.Number), overtimeDate = filing.Date,
            startTime = DateTime.SpecifyKind(filing.Date.ToDateTime(filing.Start), DateTimeKind.Utc),
            endTime = DateTime.SpecifyKind(filing.Date.ToDateTime(filing.End), DateTimeKind.Utc),
            reason = filing.Reason,
        }, await TokenOfAsync(person.Number)));

        if (approver is { } managerNumber)
        {
            await api.PutAsync($"{step}: approve", $"api/overtime-requests/{id}/approve", null, await TokenOfAsync(managerNumber));
            Count("overtime approved");
        }
        else
        {
            Count("overtime pending");
        }
    }

    /// <summary>
    /// Sends one half-month of punches through api/attendance/sync (admin token; the admin holds
    /// attendance.device-sync). A skip or an error stops the run: a silently imported gap would
    /// otherwise lead to payroll that is marked paid with false absences.
    /// </summary>
    private async Task SyncAttendanceAsync(IReadOnlyList<AttendanceRow> rows, DateOnly start, DateOnly end)
    {
        if (rows.Count == 0) return;

        var step = $"Attendance {start:yyyy-MM-dd} to {end:yyyy-MM-dd}";
        var result = await api.PostAsync(step, "api/attendance/sync", AttendancePlanner.PunchesFor(rows), await AdminAsync());

        var imported = result?["imported"]?.GetValue<int>() ?? 0;
        var skipped = result?["skipped"]?.GetValue<int>() ?? 0;
        var errors = (result?["errors"] as JsonArray)?.Select(e => e?.ToString() ?? "").ToList() ?? [];

        if (skipped > 0 || errors.Count > 0)
        {
            var detail = errors.Count > 0 ? string.Join(" | ", errors.Take(3)) : "no error detail returned";
            throw new SeedException(step, "POST", "api/attendance/sync", 200,
                $"{imported} imported, {skipped} skipped: {detail}");
        }

        Count("attendance punches", imported);
    }

    private async Task RunPayrollAsync(PayPeriod period)
    {
        var step = $"Payroll {period.Start:yyyy-MM-dd} to {period.End:yyyy-MM-dd}";
        var admin = await AdminAsync();
        var employees = plan.People.Where(p => p.HireDate <= period.End)
            .Select(p => new { employeeId = EmployeeId(p.Number) }).ToList();

        var id = IdOf(await api.PostAsync(step, "api/payroll-runs", new
        {
            periodStart = period.Start, periodEnd = period.End, payDate = period.PayDate,
            frequency = "SemiMonthly", employees,
        }, admin));

        if (period == plan.PayPeriods[^1])
        {
            Count("payroll runs awaiting approval");
            return;
        }

        await api.PutAsync($"{step}: approve", $"api/payroll-runs/{id}/approve", null, admin);
        await api.PutAsync($"{step}: mark paid", $"api/payroll-runs/{id}/mark-paid", null, admin);
        Count("payroll runs paid");
    }
}
