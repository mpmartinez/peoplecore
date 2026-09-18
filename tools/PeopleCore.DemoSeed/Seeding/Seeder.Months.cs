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
            await api.PostCsvAsync($"Import {month:00}/2026 attendance", "api/attendance/import",
                AttendancePlanner.ToCsv(rows), await AdminAsync());
            Count("attendance days", rows.Count);
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

        var id = IdOf(await api.PostAsync(step, "api/overtime-requests", new
        {
            employeeId = EmployeeId(person.Number), overtimeDate = filing.Date,
            startTime = filing.Date.ToDateTime(filing.Start), endTime = filing.Date.ToDateTime(filing.End),
            reason = filing.Reason,
        }, await TokenOfAsync(person.Number)));

        if (filing.Decision == Decision.Approved)
        {
            await api.PutAsync($"{step}: approve", $"api/overtime-requests/{id}/approve", null,
                await TokenOfAsync(person.ManagerNumber!.Value));
            Count("overtime approved");
        }
        else
        {
            Count("overtime pending");
        }
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
