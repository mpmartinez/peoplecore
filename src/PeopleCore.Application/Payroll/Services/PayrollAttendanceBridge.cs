using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Application.Scheduling.Services;
using PeopleCore.Domain.Entities.Attendance;
using PeopleCore.Domain.Entities.Scheduling;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Interfaces;

namespace PeopleCore.Application.Payroll.Services;

/// <summary>
/// Turns PeopleCore's attendance data into the eight <see cref="PayrollAttendanceInput"/> totals
/// the payroll engine consumes.
/// <para>
/// Everything is loaded once for the whole employee set and indexed in memory, so a 200-employee
/// fortnight is a handful of queries rather than one per employee per day.
/// </para>
/// </summary>
public sealed class PayrollAttendanceBridge : IPayrollAttendanceBridge
{
    private readonly IAttendanceRepository _attendance;
    private readonly ILeaveRequestRepository _leave;
    private readonly IOvertimeRepository _overtime;
    private readonly IHolidayRepository _holidays;
    private readonly IShiftAssignmentRepository _assignments;

    public PayrollAttendanceBridge(
        IAttendanceRepository attendance,
        ILeaveRequestRepository leave,
        IOvertimeRepository overtime,
        IHolidayRepository holidays,
        IShiftAssignmentRepository assignments)
    {
        _attendance = attendance;
        _leave = leave;
        _overtime = overtime;
        _holidays = holidays;
        _assignments = assignments;
    }

    public async Task<AttendanceBridgeResult> BuildAsync(
        IReadOnlyList<Guid> employeeIds, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(employeeIds);

        // The one genuine programming error. Missing data is never an error: an employee with no
        // records simply accumulates zeros.
        if (to < from)
            throw new ArgumentException($"Period end {to:yyyy-MM-dd} precedes start {from:yyyy-MM-dd}.", nameof(to));

        var wanted = employeeIds.Distinct().ToList();
        if (wanted.Count == 0)
            return new AttendanceBridgeResult(new Dictionary<Guid, PayrollAttendanceInput>(), []);

        var wantedSet = wanted.ToHashSet();

        // ---- Load once, for the whole employee set -------------------------------------------
        var records = await _attendance.GetAllByPeriodAsync(from, to, ct);
        var approvedLeave = await _leave.GetApprovedByPeriodAsync(from, to, ct);
        var approvedOvertime = await _overtime.GetApprovedByPeriodAsync(from, to, ct);
        var assignments = await _assignments.GetActiveForPeriodAsync(wanted, from, to, ct);

        // There is no period query for holidays, so ask for every year the period touches - a pay
        // period can straddle a year boundary.
        var holidaysByDate = new Dictionary<DateOnly, HolidayType>();
        for (var year = from.Year; year <= to.Year; year++)
        {
            foreach (var holiday in await _holidays.GetByYearAsync(year, ct))
            {
                // Two holiday rows on the same date is a real DOLE scenario - e.g. a regular
                // holiday that a local government also declares a special non-working day -
                // that DolePremiumRates prices explicitly (DoubleRegularHoliday,
                // DoubleSpecialNonWorking), but that PayrollAttendanceInput's two day-counts
                // cannot express: a date can only land in one bucket here. Rather than let
                // whichever row the database returns last win - a coin flip between a 200% and
                // a 130% day - always keep RegularHoliday, the higher-paying classification, so
                // a duplicate never underpays.
                if (holidaysByDate.TryGetValue(holiday.HolidayDate, out var existing)
                    && existing == HolidayType.RegularHoliday)
                    continue;

                holidaysByDate[holiday.HolidayDate] = holiday.HolidayType;
            }
        }

        // ---- Index by employee ----------------------------------------------------------------
        var recordsByEmployee = records
            .Where(r => wantedSet.Contains(r.EmployeeId))
            .GroupBy(r => r.EmployeeId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(r => r.AttendanceDate)
                      .ToDictionary(d => d.Key, d => (IReadOnlyList<AttendanceRecord>)d.ToList()));

        // Only LeaveType.IsPaid == true suppresses an absence. Approved unpaid leave leaves the
        // day absent, which is correct - unpaid leave is unpaid.
        var paidLeaveDatesByEmployee = new Dictionary<Guid, HashSet<DateOnly>>();
        foreach (var request in approvedLeave)
        {
            if (!wantedSet.Contains(request.EmployeeId)) continue;
            // An unloaded LeaveType navigation (null) is treated as paid, not unpaid: only a
            // LeaveType we can actually see marked IsPaid == false leaves the day absent. Erring
            // this way is deliberate - over-deducting wages on missing data is a compliance
            // problem, while under-deducting is a recoverable business one. Unreachable today
            // because GetApprovedByPeriodAsync always Includes LeaveType, but the guard should
            // fail safe if that ever changes.
            if (request.LeaveType is { IsPaid: false }) continue;

            var dates = paidLeaveDatesByEmployee.TryGetValue(request.EmployeeId, out var existing)
                ? existing
                : paidLeaveDatesByEmployee[request.EmployeeId] = [];

            var start = request.StartDate < from ? from : request.StartDate;
            var end = request.EndDate > to ? to : request.EndDate;
            for (var date = start; date <= end; date = date.AddDays(1))
                dates.Add(date);
        }

        // Approved OvertimeRequest rows are the only source of payable overtime.
        // AttendanceRecord.OvertimeMinutes is read nowhere in this file: staying late without
        // approval creates no payroll liability.
        var overtimeMinutesByEmployee = new Dictionary<Guid, Dictionary<DateOnly, int>>();
        foreach (var request in approvedOvertime)
        {
            if (!wantedSet.Contains(request.EmployeeId)) continue;
            if (request.OvertimeDate < from || request.OvertimeDate > to) continue;

            var perDate = overtimeMinutesByEmployee.TryGetValue(request.EmployeeId, out var existing)
                ? existing
                : overtimeMinutesByEmployee[request.EmployeeId] = [];

            perDate[request.OvertimeDate] = perDate.GetValueOrDefault(request.OvertimeDate) + request.TotalMinutes;
        }

        var assignmentsByEmployee = assignments
            .Where(a => wantedSet.Contains(a.EmployeeId))
            .GroupBy(a => a.EmployeeId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<EmployeeShiftAssignment>)g.ToList());

        // ---- Walk each employee over each date -------------------------------------------------
        var inputs = new Dictionary<Guid, PayrollAttendanceInput>(wanted.Count);
        var withoutSchedule = new List<Guid>();

        foreach (var employeeId in wanted)
        {
            var employeeRecords = recordsByEmployee.GetValueOrDefault(employeeId);
            var employeePaidLeave = paidLeaveDatesByEmployee.GetValueOrDefault(employeeId);
            var employeeOvertime = overtimeMinutesByEmployee.GetValueOrDefault(employeeId);
            var employeeAssignments = assignmentsByEmployee.GetValueOrDefault(employeeId) ?? [];

            decimal lateMinutes = 0m, undertimeMinutes = 0m, absenceDays = 0m;
            decimal ordinaryOvertimeHours = 0m, restDayOvertimeHours = 0m;
            decimal holidayRegularDays = 0m, holidaySpecialDays = 0m, nightDiffHours = 0m;
            var anyDateScheduled = false;

            for (var date = from; date <= to; date = date.AddDays(1))
            {
                var assignment = PickAssignment(employeeAssignments, date);
                var schedule = ShiftScheduleResolver.Resolve(assignment, date);
                if (schedule is not null) anyDateScheduled = true;

                // ShiftScheduleResolver is now authoritative about which dates a fixed template
                // schedules (via ShiftTemplate.WorkDays), so no Mon-Fri second-guessing is needed
                // here any more.

                // Nothing in the schema enforces one record per employee per day: a date counts as
                // present if ANY record for it is marked IsPresent, and late, undertime and night
                // hours sum across all of them.
                var dateRecords = employeeRecords?.GetValueOrDefault(date);
                var isPresent = false;
                if (dateRecords is not null)
                {
                    foreach (var record in dateRecords)
                    {
                        isPresent |= record.IsPresent;
                        lateMinutes += record.LateMinutes;
                        undertimeMinutes += record.UndertimeMinutes;

                        // A record with no TimeOut contributes zero rather than a guess at when
                        // the shift ended.
                        if (record.TimeIn.HasValue && record.TimeOut.HasValue)
                            nightDiffHours += NightDifferential.Hours(record.TimeIn.Value, record.TimeOut.Value);
                    }
                }

                var isHoliday = holidaysByDate.TryGetValue(date, out var holidayType);

                // The Holiday calendar governs, never AttendanceRecord.IsHoliday - the record
                // carries a denormalised flag that can drift from the calendar.
                if (isPresent && isHoliday)
                {
                    if (holidayType == HolidayType.RegularHoliday) holidayRegularDays += 1m;
                    else holidaySpecialDays += 1m;
                }

                var overtimeMinutes = employeeOvertime?.GetValueOrDefault(date) ?? 0;
                if (overtimeMinutes > 0)
                {
                    // Overtime on a date whose schedule resolves to null counts as ordinary
                    // overtime, not rest-day: it was approved so it is payable, but with no
                    // schedule there is no basis for the rest-day premium.
                    if (schedule is { IsRestDay: true })
                        restDayOvertimeHours += overtimeMinutes / 60m;
                    else
                        ordinaryOvertimeHours += overtimeMinutes / 60m;
                }

                // An absence needs a scheduled working day. A rest day is not one, and a null
                // schedule is no basis to deduct at all. Nor is an unworked REGULAR holiday: the
                // Labor Code entitles the employee to 100% of the daily wage whether or not they
                // work it, and basePeriodPay already pays that - deducting an absence here would
                // claw it straight back. This is deliberately asymmetric: a SPECIAL NON-WORKING
                // day follows "no work, no pay", so it is NOT excluded here and still creates an
                // absence when unworked. That asymmetry looks like an oversight if you don't know
                // the Labor Code distinction, so it is spelled out here rather than left implicit.
                var isUnworkedRegularHoliday = isHoliday && holidayType == HolidayType.RegularHoliday;
                if (schedule is { IsRestDay: false }
                    && !isPresent
                    && employeePaidLeave?.Contains(date) != true
                    && !isUnworkedRegularHoliday)
                {
                    absenceDays += 1m;
                }
            }

            inputs[employeeId] = new PayrollAttendanceInput
            {
                LateMinutes = lateMinutes,
                UndertimeMinutes = undertimeMinutes,
                AbsenceDays = absenceDays,
                OvertimeHours = Math.Round(ordinaryOvertimeHours, 2),
                RestDayOTHours = Math.Round(restDayOvertimeHours, 2),
                HolidayRegularDays = holidayRegularDays,
                HolidaySpecialDays = holidaySpecialDays,
                // NightDifferential.Hours already rounds each record to 2dp before it is summed
                // above, so the period total here is a sum of already-rounded values - rounding
                // it again is a no-op, not a "round once on the period total" step, so it is left
                // out rather than implying behaviour this line does not have.
                NightDiffHours = nightDiffHours
            };

            // No assignment covered any date in the period - or the one that did could not be
            // resolved. Either way the bridge derived no absences for this employee, so the
            // condition is reported rather than left silent.
            if (!anyDateScheduled) withoutSchedule.Add(employeeId);
        }

        return new AttendanceBridgeResult(inputs, withoutSchedule);
    }

    /// <summary>
    /// The assignment in force on <paramref name="date"/>: <c>EffectiveFrom &lt;= date</c> and
    /// <c>EffectiveTo</c> null or <c>&gt;= date</c>, preferring the latest <c>EffectiveFrom</c> so
    /// a re-assignment supersedes the one it replaced. When two candidates share the same latest
    /// <c>EffectiveFrom</c> - the repository provides no tie-break and gives no ordering
    /// guarantee - the one with the later <c>CreatedAt</c> wins, so the choice depends on which
    /// row was created more recently rather than on repository/SQL row order.
    /// </summary>
    private static EmployeeShiftAssignment? PickAssignment(
        IReadOnlyList<EmployeeShiftAssignment> candidates, DateOnly date)
    {
        EmployeeShiftAssignment? best = null;

        foreach (var candidate in candidates)
        {
            if (candidate.EffectiveFrom > date) continue;
            if (candidate.EffectiveTo is { } end && end < date) continue;

            if (best is null
                || candidate.EffectiveFrom > best.EffectiveFrom
                || (candidate.EffectiveFrom == best.EffectiveFrom && candidate.CreatedAt > best.CreatedAt))
                best = candidate;
        }

        return best;
    }
}
