using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Common.Time;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Scheduling.DTOs;
using PeopleCore.Application.Scheduling.Interfaces;
using PeopleCore.Domain.Entities.Attendance;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.Application.Attendance.Services;

public class AttendanceService : IAttendanceService
{
    // The day an employee with no shift assignment is measured against.
    private static readonly TimeOnly DefaultShiftStart = new(8, 0);
    private static readonly TimeOnly DefaultShiftEnd = new(17, 0);

    /// <summary>
    /// How long after a night shift's end its clock-out is still taken as that shift's. Past it,
    /// a punch the next day is a fresh time-in, not a very late time-out.
    /// </summary>
    private static readonly TimeSpan OvernightClockOutWindow = TimeSpan.FromHours(8);

    private readonly IAttendanceRepository _repo;
    private readonly IHolidayService _holidayService;
    private readonly IEmployeeRepository _employeeRepo;
    private readonly IShiftService _shiftService;
    private readonly TimeProvider _clock;

    public AttendanceService(
        IAttendanceRepository repo,
        IHolidayService holidayService,
        IEmployeeRepository employeeRepo,
        IShiftService shiftService,
        TimeProvider clock)
    {
        _repo = repo;
        _holidayService = holidayService;
        _employeeRepo = employeeRepo;
        _shiftService = shiftService;
        _clock = clock;
    }

    /// <summary>
    /// Clocks in at the server's current Philippine time (<see cref="PhilippineTime"/>): the day,
    /// and lateness against the shift start, are read off that wall clock.
    /// </summary>
    public Task<AttendanceRecordDto> TimeInAsync(TimeInRequest request, CancellationToken ct = default)
        => RecordTimeInAsync(request.EmployeeId, PhilippineTime.Now(_clock), ct);

    /// <summary>Clocks out at the server's current Philippine time, as <see cref="TimeInAsync"/>.</summary>
    public Task<AttendanceRecordDto> TimeOutAsync(TimeOutRequest request, CancellationToken ct = default)
        => RecordTimeOutAsync(request.EmployeeId, PhilippineTime.Now(_clock), ct);

    /// <param name="timeIn">Philippine wall-clock time, labelled UTC.</param>
    private async Task<AttendanceRecordDto> RecordTimeInAsync(Guid employeeId, DateTime timeIn, CancellationToken ct)
    {
        var employee = await _employeeRepo.GetByIdAsync(employeeId, ct)
            ?? throw new KeyNotFoundException($"Employee {employeeId} not found.");

        var today = DateOnly.FromDateTime(timeIn);
        var existing = await _repo.GetByEmployeeAndDateAsync(employeeId, today, ct);

        if (existing?.TimeIn is not null)
            throw new DomainException("Employee has already clocked in today.");

        var holidayType = await _holidayService.IsHolidayAsync(today, ct);
        var schedule = await _shiftService.ResolveShiftForDayAsync(employeeId, today, ct);
        var lateMinutes = CalculateLateMinutes(timeIn, ShiftWindow(today, schedule));

        var record = existing ?? new AttendanceRecord
        {
            EmployeeId = employeeId,
            AttendanceDate = today
        };

        record.TimeIn = timeIn;
        record.IsPresent = true;
        record.LateMinutes = lateMinutes;
        record.IsHoliday = holidayType is not null;
        record.HolidayType = holidayType;

        AttendanceRecord saved;
        if (existing is null)
        {
            saved = await _repo.AddAsync(record, ct);
        }
        else
        {
            record.UpdatedAt = DateTime.UtcNow;
            saved = await UpdateAndReturn(record, ct);
        }

        return ToDto(saved, employee.FullName);
    }

    /// <param name="timeOut">Philippine wall-clock time, labelled UTC.</param>
    private async Task<AttendanceRecordDto> RecordTimeOutAsync(Guid employeeId, DateTime timeOut, CancellationToken ct)
    {
        var employee = await _employeeRepo.GetByIdAsync(employeeId, ct)
            ?? throw new KeyNotFoundException($"Employee {employeeId} not found.");

        var today = DateOnly.FromDateTime(timeOut);
        var record = await _repo.GetByEmployeeAndDateAsync(employeeId, today, ct);

        // A night shift clocks out the morning after it started, so its record is last night's.
        if (record?.TimeIn is null)
            record = await FindOpenOvernightRecordAsync(employeeId, timeOut, ct)
                ?? throw new DomainException("Employee is not clocked in today.");

        var schedule = await _shiftService.ResolveShiftForDayAsync(employeeId, record.AttendanceDate, ct);
        var window = ShiftWindow(record.AttendanceDate, schedule);

        record.TimeOut = timeOut;
        record.UndertimeMinutes = CalculateUndertimeMinutes(timeOut, window);
        record.OvertimeMinutes = CalculateOvertimeMinutes(record.TimeIn!.Value, timeOut, window);
        record.UpdatedAt = DateTime.UtcNow;

        await _repo.UpdateAsync(record, ct);
        return ToDto(record, employee.FullName);
    }

    public async Task<PagedResult<AttendanceRecordDto>> GetAllAsync(
        Guid? employeeId, Guid? reportingManagerId, DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken ct = default)
    {
        var (items, total) = await _repo.GetPagedAsync(employeeId, reportingManagerId, from, to, page, pageSize, ct);
        var dtos = items.Select(r => ToDto(r, r.Employee?.FullName ?? string.Empty)).ToList();
        return PagedResult<AttendanceRecordDto>.Create(dtos, total, page, pageSize);
    }

    public async Task<AttendanceSummaryDto> GetSummaryAsync(
        Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var employee = await _employeeRepo.GetByIdAsync(employeeId, ct)
            ?? throw new KeyNotFoundException($"Employee {employeeId} not found.");

        var records = await _repo.GetByEmployeeAndPeriodAsync(employeeId, from, to, ct);

        return new AttendanceSummaryDto(
            employeeId,
            employee.FullName,
            from,
            to,
            await TotalWorkingDaysAsync(employeeId, from, to, ct),
            records.Count(r => r.IsPresent),
            records.Sum(r => r.LateMinutes),
            records.Sum(r => r.UndertimeMinutes),
            records.Sum(r => r.OvertimeMinutes),
            records.Count(r => r.IsHoliday && r.HolidayType == Domain.Enums.HolidayType.RegularHoliday && r.IsPresent),
            records.Count(r => r.IsHoliday && r.HolidayType == Domain.Enums.HolidayType.SpecialNonWorking && r.IsPresent));
    }

    public async Task<AttendanceImportResultDto> SyncPunchesAsync(
        IReadOnlyList<AttendancePunchDto> punches, CancellationToken ct = default)
    {
        int imported = 0, skipped = 0;
        var errors = new List<string>();

        foreach (var punch in punches)
        {
            try
            {
                var employee = await _employeeRepo.GetByNumberAsync(punch.EmployeeNumber, ct);
                if (employee is null)
                {
                    errors.Add($"Employee '{punch.EmployeeNumber}' not found.");
                    skipped++;
                    continue;
                }

                var date = DateOnly.FromDateTime(punch.PunchTime);
                var existing = await _repo.GetByEmployeeAndDateAsync(employee.Id, date, ct);

                if (existing is null
                    && await FindOpenOvernightRecordAsync(employee.Id, punch.PunchTime, ct) is not null)
                {
                    // The morning punch that ends last night's shift, not the start of today's.
                    await RecordTimeOutAsync(employee.Id, punch.PunchTime, ct);
                }
                else if (existing is null)
                {
                    await RecordTimeInAsync(employee.Id, punch.PunchTime, ct);
                }
                else if (existing.TimeIn is not null && existing.TimeOut is null &&
                         punch.PunchTime > existing.TimeIn)
                {
                    await RecordTimeOutAsync(employee.Id, punch.PunchTime, ct);
                }
                else
                {
                    skipped++;
                    continue;
                }
                imported++;
            }
            catch (Exception ex)
            {
                errors.Add($"Row for '{punch.EmployeeNumber}': {ex.Message}");
                skipped++;
            }
        }

        return new AttendanceImportResultDto(imported, skipped, errors);
    }

    /// <summary>
    /// Sets a day's times outright - for a correction - and recomputes lateness, undertime and
    /// overtime by the same rules as clocking in and out. Both times null clears the day to absent.
    /// </summary>
    /// <param name="timeIn">Philippine wall-clock time of day.</param>
    public async Task<AttendanceRecordDto> SetDayAsync(
        Guid employeeId, DateOnly date, TimeOnly? timeIn, TimeOnly? timeOut, CancellationToken ct = default)
    {
        var employee = await _employeeRepo.GetByIdAsync(employeeId, ct)
            ?? throw new KeyNotFoundException($"Employee {employeeId} not found.");
        EnsureValidTimes(timeIn, timeOut);

        var existing = await _repo.GetByEmployeeAndDateAsync(employeeId, date, ct);
        var record = existing ?? new AttendanceRecord { EmployeeId = employeeId, AttendanceDate = date };

        var holidayType = await _holidayService.IsHolidayAsync(date, ct);
        var schedule = timeIn is null ? null : await _shiftService.ResolveShiftForDayAsync(employeeId, date, ct);

        record.TimeIn = timeIn is { } i ? date.ToDateTime(i, DateTimeKind.Utc) : null;
        record.TimeOut = TimeOutOn(date, timeIn, timeOut);
        record.IsPresent = timeIn is not null;
        var window = ShiftWindow(date, schedule);
        record.LateMinutes = record.TimeIn is { } late ? CalculateLateMinutes(late, window) : 0;
        record.UndertimeMinutes = record.TimeOut is { } under ? CalculateUndertimeMinutes(under, window) : 0;
        record.OvertimeMinutes = record.TimeIn is { } start && record.TimeOut is { } end
            ? CalculateOvertimeMinutes(start, end, window) : 0;
        record.IsHoliday = holidayType is not null;
        record.HolidayType = holidayType;

        if (existing is null)
            return ToDto(await _repo.AddAsync(record, ct), employee.FullName);

        record.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(record, ct);
        return ToDto(record, employee.FullName);
    }

    /// <summary>
    /// The longest a day can run when its time-out is read as the next morning: a twelve-hour night
    /// shift with four hours of overtime. Anything longer is far likelier a mistyped time.
    /// </summary>
    public static readonly TimeSpan MaxOvernightSpan = TimeSpan.FromHours(16);

    /// <summary>
    /// A time-out needs a time-in before it; a correction that breaks that is refused. A time-out
    /// earlier on the clock than the time-in is a night shift ending the next morning, allowed up to
    /// <see cref="MaxOvernightSpan"/> after the time-in.
    /// </summary>
    public static void EnsureValidTimes(TimeOnly? timeIn, TimeOnly? timeOut)
    {
        if (timeIn is null && timeOut is not null)
            throw new DomainException("A time-out needs a time-in.");
        if (timeIn is { } i && timeOut is { } o && (o == i || (o < i && o - i > MaxOvernightSpan)))
            throw new DomainException(
                "The time-out must be later than the time-in. An earlier time is read as the next morning, " +
                $"up to {MaxOvernightSpan.TotalHours:0} hours after the time-in.");
    }

    /// <summary>
    /// When a day's time-out falls: on the day itself, or the next morning when it is earlier on the
    /// clock than the time-in - a night shift. Philippine wall-clock time, labelled UTC.
    /// </summary>
    public static DateTime? TimeOutOn(DateOnly date, TimeOnly? timeIn, TimeOnly? timeOut)
        => timeOut is { } o
            ? (timeIn is { } i && o < i ? date.AddDays(1) : date).ToDateTime(o, DateTimeKind.Utc)
            : null;

    /// <summary>
    /// Last night's record, still open, when <paramref name="punch"/> is the morning clock-out of
    /// a shift that ran past midnight - and null for anything else.
    /// </summary>
    private async Task<AttendanceRecord?> FindOpenOvernightRecordAsync(
        Guid employeeId, DateTime punch, CancellationToken ct)
    {
        var night = DateOnly.FromDateTime(punch).AddDays(-1);
        var record = await _repo.GetByEmployeeAndDateAsync(employeeId, night, ct);
        if (record is not { TimeIn: not null, TimeOut: null })
            return null;

        var schedule = await _shiftService.ResolveShiftForDayAsync(employeeId, night, ct);
        return ShiftWindow(night, schedule) is { } window
               && DateOnly.FromDateTime(window.End) > night
               && punch <= window.End + OvernightClockOutWindow
            ? record
            : null;
    }

    /// <summary>
    /// When the employee was due to work on <paramref name="date"/>: their shift, ending the next
    /// morning when it runs past midnight, or 08:00-17:00 when no shift is assigned. Null on a
    /// rest day, which has no shift to be late for or to leave early.
    /// </summary>
    private static (DateTime Start, DateTime End)? ShiftWindow(DateOnly date, DailyScheduleDto? schedule)
    {
        if (schedule is { IsRestDay: true })
            return null;

        var start = date.ToDateTime(schedule?.StartTime ?? DefaultShiftStart, DateTimeKind.Utc);
        var end = date.ToDateTime(schedule?.EndTime ?? DefaultShiftEnd, DateTimeKind.Utc);
        if (end <= start)
            end = end.AddDays(1);

        return (start, end);
    }

    private static int CalculateLateMinutes(DateTime timeIn, (DateTime Start, DateTime End)? shift)
        => shift is { } s && timeIn > s.Start ? (int)(timeIn - s.Start).TotalMinutes : 0;

    private static int CalculateUndertimeMinutes(DateTime timeOut, (DateTime Start, DateTime End)? shift)
        => shift is { } s && timeOut < s.End ? (int)(s.End - timeOut).TotalMinutes : 0;

    /// <summary>Time past the shift's end - or, on a rest day, all of the time worked.</summary>
    private static int CalculateOvertimeMinutes(DateTime timeIn, DateTime timeOut, (DateTime Start, DateTime End)? shift)
        => shift is { } s
            ? (timeOut > s.End ? (int)(timeOut - s.End).TotalMinutes : 0)
            : (int)(timeOut - timeIn).TotalMinutes;

    /// <summary>
    /// The days in the range the employee's shift schedules, rest days excluded. A day no shift
    /// assignment covers falls back to Monday to Friday, as the attendance rules fall back to 08:00-17:00.
    /// </summary>
    private async Task<int> TotalWorkingDaysAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        int count = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var schedule = await _shiftService.ResolveShiftForDayAsync(employeeId, d, ct);
            var isWorkingDay = schedule is not null
                ? !schedule.IsRestDay
                : d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
            if (isWorkingDay)
                count++;
        }
        return count;
    }

    private async Task<AttendanceRecord> UpdateAndReturn(AttendanceRecord record, CancellationToken ct)
    {
        await _repo.UpdateAsync(record, ct);
        return record;
    }

    private static AttendanceRecordDto ToDto(AttendanceRecord r, string employeeName) => new(
        r.Id, r.EmployeeId, employeeName, r.AttendanceDate,
        r.TimeIn, r.TimeOut, r.LateMinutes, r.UndertimeMinutes, r.OvertimeMinutes,
        r.IsPresent, r.IsHoliday, r.HolidayType, r.Remarks);
}
