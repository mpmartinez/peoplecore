using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Domain.Entities.Attendance;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.Application.Attendance.Services;

public class AttendanceService : IAttendanceService
{
    private static readonly TimeOnly ShiftStart = new(8, 0);
    private static readonly TimeOnly ShiftEnd = new(17, 0);

    private readonly IAttendanceRepository _repo;
    private readonly IHolidayService _holidayService;
    private readonly IEmployeeRepository _employeeRepo;

    public AttendanceService(
        IAttendanceRepository repo,
        IHolidayService holidayService,
        IEmployeeRepository employeeRepo)
    {
        _repo = repo;
        _holidayService = holidayService;
        _employeeRepo = employeeRepo;
    }

    public async Task<AttendanceRecordDto> TimeInAsync(TimeInRequest request, CancellationToken ct = default)
    {
        var employee = await _employeeRepo.GetByIdAsync(request.EmployeeId, ct)
            ?? throw new KeyNotFoundException($"Employee {request.EmployeeId} not found.");

        var today = DateOnly.FromDateTime(request.TimeIn);
        var existing = await _repo.GetByEmployeeAndDateAsync(request.EmployeeId, today, ct);

        if (existing?.TimeIn is not null)
            throw new DomainException("Employee has already clocked in today.");

        var isHoliday = await _holidayService.IsHolidayAsync(today, ct);
        var lateMinutes = CalculateLateMinutes(TimeOnly.FromDateTime(request.TimeIn));

        var record = existing ?? new AttendanceRecord
        {
            EmployeeId = request.EmployeeId,
            AttendanceDate = today
        };

        record.TimeIn = request.TimeIn;
        record.IsPresent = true;
        record.LateMinutes = lateMinutes;
        record.IsHoliday = isHoliday;

        var saved = existing is null
            ? await _repo.AddAsync(record, ct)
            : await UpdateAndReturn(record, ct);

        return ToDto(saved, employee.FullName);
    }

    public async Task<AttendanceRecordDto> TimeOutAsync(TimeOutRequest request, CancellationToken ct = default)
    {
        var employee = await _employeeRepo.GetByIdAsync(request.EmployeeId, ct)
            ?? throw new KeyNotFoundException($"Employee {request.EmployeeId} not found.");

        var today = DateOnly.FromDateTime(request.TimeOut);
        var record = await _repo.GetByEmployeeAndDateAsync(request.EmployeeId, today, ct);

        if (record?.TimeIn is null)
            throw new DomainException("Employee is not clocked in today.");

        record.TimeOut = request.TimeOut;
        record.UndertimeMinutes = CalculateUndertimeMinutes(TimeOnly.FromDateTime(request.TimeOut));
        record.OvertimeMinutes = CalculateOvertimeMinutes(TimeOnly.FromDateTime(request.TimeOut));
        record.UpdatedAt = DateTime.UtcNow;

        await _repo.UpdateAsync(record, ct);
        return ToDto(record, employee.FullName);
    }

    public async Task<PagedResult<AttendanceRecordDto>> GetAllAsync(
        Guid? employeeId, DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken ct = default)
    {
        var (items, total) = await _repo.GetPagedAsync(employeeId, from, to, page, pageSize, ct);
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
            TotalWorkingDays(from, to),
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

                if (existing is null)
                {
                    await TimeInAsync(new TimeInRequest(employee.Id, punch.PunchTime), ct);
                }
                else if (existing.TimeIn is not null && existing.TimeOut is null &&
                         punch.PunchTime > existing.TimeIn)
                {
                    await TimeOutAsync(new TimeOutRequest(employee.Id, punch.PunchTime), ct);
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

    private static int CalculateLateMinutes(TimeOnly timeIn)
    {
        if (timeIn <= ShiftStart) return 0;
        return (int)(timeIn - ShiftStart).TotalMinutes;
    }

    private static int CalculateUndertimeMinutes(TimeOnly timeOut)
    {
        if (timeOut >= ShiftEnd) return 0;
        return (int)(ShiftEnd - timeOut).TotalMinutes;
    }

    private static int CalculateOvertimeMinutes(TimeOnly timeOut)
    {
        if (timeOut <= ShiftEnd) return 0;
        return (int)(timeOut - ShiftEnd).TotalMinutes;
    }

    private static int TotalWorkingDays(DateOnly from, DateOnly to)
    {
        int count = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
            if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday)
                count++;
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
