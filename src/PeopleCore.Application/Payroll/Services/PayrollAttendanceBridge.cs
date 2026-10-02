using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Application.Scheduling.Services;
using PeopleCore.Domain.Entities.Attendance;
using PeopleCore.Domain.Entities.Scheduling;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Interfaces;
using PeopleCore.Domain.Payroll;

namespace PeopleCore.Application.Payroll.Services;

/// <summary>
/// Turns PeopleCore's attendance data into the <see cref="PayrollAttendanceInput"/> totals and per-day-type breakdown
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

        // The day-before rule for an unworked regular holiday can reach back before the period
        // (a holiday on its first day is judged by the previous period's last workday), so
        // attendance, leave, schedules and holidays are loaded from this earlier date. Everything
        // before `from` feeds only that rule: the per-date walk below covers `from` to `to`, so
        // nothing earlier adds late, undertime, night hours, overtime, absences or premium days.
        var lookBackFrom = from.AddDays(-DayBeforeLookBackDays);

        // ---- Load once, for the whole employee set -------------------------------------------
        var records = await _attendance.GetAllByPeriodAsync(lookBackFrom, to, ct);
        var approvedLeave = await _leave.GetApprovedByPeriodAsync(lookBackFrom, to, ct);
        var approvedOvertime = await _overtime.GetApprovedByPeriodAsync(from, to, ct);
        var assignments = await _assignments.GetActiveForPeriodAsync(wanted, lookBackFrom, to, ct);

        // There is no period query for holidays, so ask for every year the look-back and the
        // period touch - either can straddle a year boundary.
        //
        // Two holiday rows on one date is a real DOLE scenario - two regular holidays coinciding
        // (a double holiday, 300%), or two special days (150%). A regular holiday that a local
        // government also declares special keeps the regular holiday's pay, the higher of the
        // two, whichever row the database returns first. A special working day is an ordinary
        // working day for pay, so it is not counted at all.
        var holidaysByDate = new Dictionary<DateOnly, (int Regular, int Special)>();
        for (var year = lookBackFrom.Year; year <= to.Year; year++)
        {
            foreach (var holiday in await _holidays.GetByYearAsync(year, ct))
            {
                if (holiday.HolidayType == HolidayType.SpecialWorking) continue;

                var (regular, special) = holidaysByDate.GetValueOrDefault(holiday.HolidayDate);
                holidaysByDate[holiday.HolidayDate] = holiday.HolidayType == HolidayType.RegularHoliday
                    ? (regular + 1, special)
                    : (regular, special + 1);
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

            // Leave before `from` is kept for the day-before rule only; the per-date walk never
            // reads a date outside the period, so it suppresses no absence there.
            var start = request.StartDate < lookBackFrom ? lookBackFrom : request.StartDate;
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
            var premiumDays = new Dictionary<WorkDayType, PremiumDayInput>();
            var anyDateScheduled = false;

            void Add(WorkDayType dayType, decimal days = 0m, decimal hours = 0m,
                     decimal overtimeHours = 0m, decimal nightDiffHours = 0m, decimal unworkedDays = 0m)
            {
                var current = premiumDays.GetValueOrDefault(dayType) ?? new PremiumDayInput(dayType);
                premiumDays[dayType] = current with
                {
                    Days = current.Days + days,
                    Hours = current.Hours + hours,
                    OvertimeHours = current.OvertimeHours + overtimeHours,
                    NightDiffHours = current.NightDiffHours + nightDiffHours,
                    UnworkedDays = current.UnworkedDays + unworkedDays
                };
            }

            for (var date = from; date <= to; date = date.AddDays(1))
            {
                var assignment = ShiftScheduleResolver.PickAssignment(employeeAssignments, date);
                var schedule = ShiftScheduleResolver.Resolve(assignment, date);
                if (schedule is not null) anyDateScheduled = true;

                // ShiftScheduleResolver is now authoritative about which dates a fixed template
                // schedules (via ShiftTemplate.WorkDays), so no Mon-Fri second-guessing is needed
                // here any more.

                // The Holiday calendar governs, never AttendanceRecord.IsHoliday - the record
                // carries a denormalised flag that can drift from the calendar. A date whose
                // schedule resolves to null is not a rest day: with no schedule there is no basis
                // for the rest-day rates.
                var isRestDay = schedule is { IsRestDay: true };
                var holidays = holidaysByDate.GetValueOrDefault(date);
                var dayType = Classify(holidays.Regular, holidays.Special, isRestDay);

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
                            Add(dayType, nightDiffHours: NightDifferential.Hours(record.TimeIn.Value, record.TimeOut.Value));
                    }
                }

                // The day-before rule (Labor Code Art. 94): an unworked regular holiday on a
                // scheduled working day is not paid when the employee was absent without pay on
                // the workday before it. A rest-day holiday is not a holiday pay case here, and a
                // date with no schedule has no basis to deduct, so neither is judged.
                var forfeitsRegularHoliday = holidays.Regular > 0
                    && schedule is { IsRestDay: false }
                    && !isPresent
                    && !QualifiesForUnworkedRegularHoliday(
                        date, employeeAssignments, holidaysByDate, employeeRecords, employeePaidLeave);

                var overtimeHours = (employeeOvertime?.GetValueOrDefault(date) ?? 0) / 60m;
                if (isRestDay)
                {
                    // A rest day has no shift, so all its work is approved overtime: the first
                    // eight hours earn the rest day's own rate, and only the hours past them the
                    // overtime rate. Turning up on a rest day without approval earns nothing, just
                    // as staying late without approval does not.
                    if (overtimeHours > 0m)
                        Add(dayType, hours: Math.Min(overtimeHours, 8m),
                            overtimeHours: Math.Max(0m, overtimeHours - 8m));
                }
                else
                {
                    // A scheduled day: attending a holiday earns that day's premium, and approved
                    // overtime past the shift the overtime rate for that kind of day.
                    if (isPresent && dayType != WorkDayType.Ordinary) Add(dayType, days: 1m);
                    if (overtimeHours > 0m) Add(dayType, overtimeHours: overtimeHours);
                }

                // A double regular holiday is paid 200% whether or not it is worked: the salary
                // already carries the first 100% and the engine adds the second from this count.
                // It needs a schedule (with none there is no basis to pay, just as no absence is
                // derived). On a rest day the day is always counted, whatever overtime or presence
                // there is: work there adds its premium on top of the guarantee (the hours added
                // above are priced as that premium), so working never pays less than staying home.
                // On a working day it is counted only when no record for the date is marked
                // present, since attending is paid through Days at the worked rate instead. Paid
                // leave does not stop the count, as it does not stop the single holiday's pay. A
                // holiday forfeited under the day-before rule counts no guaranteed day: it pays
                // nothing for the day.
                if (schedule is not null
                    && (dayType == WorkDayType.DoubleRegularHolidayOnRestDay
                        || (dayType == WorkDayType.DoubleRegularHoliday && !isPresent && !forfeitsRegularHoliday)))
                {
                    Add(dayType, unworkedDays: 1m);
                }

                // An absence needs a scheduled working day. A rest day is not one, and a null
                // schedule is no basis to deduct at all. Nor is an unworked REGULAR holiday: the
                // Labor Code entitles the employee to 100% of the daily wage whether or not they
                // work it, and basePeriodPay already pays that - deducting an absence here would
                // claw it straight back. The one exception is a holiday forfeited under the
                // day-before rule, which is deducted like any absence. This is deliberately
                // asymmetric: a SPECIAL NON-WORKING day follows "no work, no pay", so it is NOT
                // excluded here and still creates an absence when unworked. That asymmetry looks
                // like an oversight if you don't know the Labor Code distinction, so it is spelled
                // out here rather than left implicit.
                var isPaidRegularHoliday = holidays.Regular > 0 && !forfeitsRegularHoliday;
                if (schedule is { IsRestDay: false }
                    && !isPresent
                    && employeePaidLeave?.Contains(date) != true
                    && !isPaidRegularHoliday)
                {
                    absenceDays += 1m;
                }
            }

            // NightDifferential.Hours already rounds each record to 2dp before it is summed, so
            // night hours are a sum of already-rounded values and are not rounded again here.
            var breakdown = premiumDays.Values
                .Select(d => d with
                {
                    Hours = Math.Round(d.Hours, 2),
                    OvertimeHours = Math.Round(d.OvertimeHours, 2)
                })
                .Where(d => d.Days != 0m || d.Hours != 0m || d.OvertimeHours != 0m || d.NightDiffHours != 0m
                            || d.UnworkedDays != 0m)
                .OrderBy(d => d.DayType)
                .ToList();

            inputs[employeeId] = new PayrollAttendanceInput
            {
                LateMinutes = lateMinutes,
                UndertimeMinutes = undertimeMinutes,
                AbsenceDays = absenceDays,
                PremiumDays = breakdown,

                // Roll-ups of the breakdown, which is what payroll prices from. All rest-day work
                // is approved overtime, so all of it rolls up as rest-day overtime.
                OvertimeHours = breakdown.Where(d => !IsRestDay(d.DayType)).Sum(d => d.OvertimeHours),
                RestDayOTHours = breakdown.Where(d => IsRestDay(d.DayType)).Sum(d => d.Hours + d.OvertimeHours),
                HolidayRegularDays = breakdown.Where(d => IsRegularHoliday(d.DayType)).Sum(d => d.Days),
                HolidaySpecialDays = breakdown.Where(d => IsSpecialDay(d.DayType)).Sum(d => d.Days),
                NightDiffHours = breakdown.Sum(d => d.NightDiffHours)
            };

            // No assignment covered any date in the period - or the one that did could not be
            // resolved. Either way the bridge derived no absences for this employee, so the
            // condition is reported rather than left silent.
            if (!anyDateScheduled) withoutSchedule.Add(employeeId);
        }

        return new AttendanceBridgeResult(inputs, withoutSchedule);
    }

    /// <summary>How far back the day-before rule looks for the qualifying workday.</summary>
    private const int DayBeforeLookBackDays = 14;

    /// <summary>
    /// The day-before rule (Labor Code Art. 94, Omnibus Rules Book III Rule IV Sec. 6): whether an
    /// employee who did not work the regular holiday on <paramref name="holiday"/> is still paid
    /// for it. Walks back one day at a time:
    /// <list type="bullet">
    /// <item>a holiday date (any calendar holiday except a special working day, which
    /// <paramref name="holidaysByDate"/> never holds) is skipped, unless the employee worked it -
    /// a present record then satisfies the rule (Holy Thursday worked, so Good Friday is paid);</item>
    /// <item>a rest day, or a date no assignment covers, is skipped;</item>
    /// <item>the first scheduled working day decides: a present record or approved paid leave
    /// satisfies the rule; approved unpaid leave or no record is absence without pay.</item>
    /// </list>
    /// With no decision after <see cref="DayBeforeLookBackDays"/> days the employee stays entitled.
    /// </summary>
    private static bool QualifiesForUnworkedRegularHoliday(
        DateOnly holiday,
        IReadOnlyList<EmployeeShiftAssignment> assignments,
        IReadOnlyDictionary<DateOnly, (int Regular, int Special)> holidaysByDate,
        IReadOnlyDictionary<DateOnly, IReadOnlyList<AttendanceRecord>>? records,
        IReadOnlySet<DateOnly>? paidLeaveDates)
    {
        bool PresentOn(DateOnly date) => records?.GetValueOrDefault(date)?.Any(r => r.IsPresent) == true;

        for (var day = 1; day <= DayBeforeLookBackDays; day++)
        {
            var date = holiday.AddDays(-day);

            if (holidaysByDate.ContainsKey(date))
            {
                if (PresentOn(date)) return true;
                continue;
            }

            var schedule = ShiftScheduleResolver.Resolve(ShiftScheduleResolver.PickAssignment(assignments, date), date);
            if (schedule is null || schedule.IsRestDay) continue;

            return PresentOn(date) || paidLeaveDates?.Contains(date) == true;
        }

        return true;
    }

    /// <summary>
    /// The DOLE kind of day for a date, from how many regular holidays and special days fall on
    /// it and whether it is the employee's rest day. A regular holiday outranks a special day on
    /// the same date; two of either make a double holiday.
    /// </summary>
    private static WorkDayType Classify(int regularHolidays, int specialDays, bool isRestDay) =>
        (regularHolidays, specialDays, isRestDay) switch
        {
            (>= 2, _, false) => WorkDayType.DoubleRegularHoliday,
            (>= 2, _, true)  => WorkDayType.DoubleRegularHolidayOnRestDay,
            (1, _, false)    => WorkDayType.RegularHoliday,
            (1, _, true)     => WorkDayType.RegularHolidayOnRestDay,
            (_, >= 2, false) => WorkDayType.DoubleSpecialNonWorking,
            (_, >= 2, true)  => WorkDayType.DoubleSpecialNonWorkingOnRestDay,
            (_, 1, false)    => WorkDayType.SpecialNonWorking,
            (_, 1, true)     => WorkDayType.SpecialNonWorkingOnRestDay,
            (_, _, true)     => WorkDayType.RestDay,
            _                => WorkDayType.Ordinary
        };

    private static bool IsRestDay(WorkDayType day) => day is WorkDayType.RestDay
        or WorkDayType.SpecialNonWorkingOnRestDay or WorkDayType.DoubleSpecialNonWorkingOnRestDay
        or WorkDayType.RegularHolidayOnRestDay or WorkDayType.DoubleRegularHolidayOnRestDay;

    private static bool IsRegularHoliday(WorkDayType day) => day is WorkDayType.RegularHoliday
        or WorkDayType.RegularHolidayOnRestDay or WorkDayType.DoubleRegularHoliday
        or WorkDayType.DoubleRegularHolidayOnRestDay;

    private static bool IsSpecialDay(WorkDayType day) => day is WorkDayType.SpecialNonWorking
        or WorkDayType.SpecialNonWorkingOnRestDay or WorkDayType.DoubleSpecialNonWorking
        or WorkDayType.DoubleSpecialNonWorkingOnRestDay;
}
