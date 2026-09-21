using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Common.Time;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Domain.Entities.Attendance;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.Application.Attendance.Services;

/// <summary>
/// Changes to recorded time-ins and time-outs. Three ways in - HR editing a day, an import that
/// replaces days, and an employee's request that an approver decides - and one rule for all of them:
/// a day inside a paid payroll run is settled and cannot change, because the payslip was computed
/// from it. A late correction is settled as an adjustment in a later run instead.
/// </summary>
public class AttendanceCorrectionService : IAttendanceCorrectionService
{
    private const int MaxReasonLength = 500;

    private readonly IAttendanceCorrectionRepository _corrections;
    private readonly IAttendanceRepository _records;
    private readonly IAttendanceService _attendance;
    private readonly IPayrollRunRepository _payrollRuns;
    private readonly TimeProvider _clock;

    public AttendanceCorrectionService(
        IAttendanceCorrectionRepository corrections,
        IAttendanceRepository records,
        IAttendanceService attendance,
        IPayrollRunRepository payrollRuns,
        TimeProvider clock)
    {
        _corrections = corrections;
        _records = records;
        _attendance = attendance;
        _payrollRuns = payrollRuns;
        _clock = clock;
    }

    public async Task<AttendanceCorrectionDto> CorrectAsync(
        CorrectAttendanceDto dto, string actor, AttendanceCorrectionSource source = AttendanceCorrectionSource.HrEdit, CancellationToken ct = default)
    {
        var reason = ValidReason(dto.Reason);
        await EnsureCorrectableAsync(dto.EmployeeId, dto.Date, dto.TimeIn, dto.TimeOut, ct);

        var current = await _records.GetByEmployeeAndDateAsync(dto.EmployeeId, dto.Date, ct);
        var correction = new AttendanceCorrection
        {
            EmployeeId = dto.EmployeeId,
            AttendanceDate = dto.Date,
            PreviousTimeIn = current?.TimeIn,
            PreviousTimeOut = current?.TimeOut,
            NewTimeIn = At(dto.Date, dto.TimeIn),
            NewTimeOut = AttendanceService.TimeOutOn(dto.Date, dto.TimeIn, dto.TimeOut),
            Reason = reason,
            Source = source,
            Status = AttendanceCorrectionStatus.Applied,
            RequestedBy = actor,
        };

        await _attendance.SetDayAsync(dto.EmployeeId, dto.Date, dto.TimeIn, dto.TimeOut, ct);
        await _corrections.AddAsync(correction, ct);
        return await GetAsync(correction.Id, ct) ?? throw new InvalidOperationException("The correction was not saved.");
    }

    public async Task<AttendanceCorrectionDto> RequestAsync(
        Guid employeeId, RequestAttendanceCorrectionDto dto, string actor, CancellationToken ct = default)
    {
        var reason = ValidReason(dto.Reason);
        await EnsureCorrectableAsync(employeeId, dto.Date, dto.TimeIn, dto.TimeOut, ct);

        if (await _corrections.HasPendingForDayAsync(employeeId, dto.Date, ct))
            throw new DomainException($"There is already a correction waiting for approval for {dto.Date:MMM d, yyyy}.");

        var current = await _records.GetByEmployeeAndDateAsync(employeeId, dto.Date, ct);
        var correction = new AttendanceCorrection
        {
            EmployeeId = employeeId,
            AttendanceDate = dto.Date,
            PreviousTimeIn = current?.TimeIn,
            PreviousTimeOut = current?.TimeOut,
            NewTimeIn = At(dto.Date, dto.TimeIn),
            NewTimeOut = AttendanceService.TimeOutOn(dto.Date, dto.TimeIn, dto.TimeOut),
            Reason = reason,
            Source = AttendanceCorrectionSource.EmployeeRequest,
            Status = AttendanceCorrectionStatus.Pending,
            RequestedBy = actor,
        };

        await _corrections.AddAsync(correction, ct);
        return await GetAsync(correction.Id, ct) ?? throw new InvalidOperationException("The request was not saved.");
    }

    public async Task<AttendanceCorrectionDto> ApproveAsync(Guid id, Guid approverEmployeeId, string actor, CancellationToken ct = default)
    {
        var correction = await PendingDecidableByAsync(id, approverEmployeeId, ct);
        TimeOnly? timeIn = correction.NewTimeIn is { } i ? TimeOnly.FromDateTime(i) : null;
        TimeOnly? timeOut = correction.NewTimeOut is { } o ? TimeOnly.FromDateTime(o) : null;

        // Checked again: a payroll run may have been paid since the request was filed.
        await EnsureNotPaidAsync(correction.EmployeeId, correction.AttendanceDate, ct);

        // The day may have changed since the request was filed; the history shows what it was replaced from.
        var current = await _records.GetByEmployeeAndDateAsync(correction.EmployeeId, correction.AttendanceDate, ct);
        correction.PreviousTimeIn = current?.TimeIn;
        correction.PreviousTimeOut = current?.TimeOut;

        await _attendance.SetDayAsync(correction.EmployeeId, correction.AttendanceDate, timeIn, timeOut, ct);

        correction.Status = AttendanceCorrectionStatus.Applied;
        correction.ReviewedBy = actor;
        correction.ReviewedAt = DateTime.UtcNow;
        correction.UpdatedAt = DateTime.UtcNow;
        await _corrections.UpdateAsync(correction, ct);
        return ToDto(correction);
    }

    public async Task<AttendanceCorrectionDto> RejectAsync(Guid id, Guid rejecterEmployeeId, string actor, string reason, CancellationToken ct = default)
    {
        var correction = await PendingDecidableByAsync(id, rejecterEmployeeId, ct);

        correction.Status = AttendanceCorrectionStatus.Rejected;
        correction.RejectionReason = ValidReason(reason);
        correction.ReviewedBy = actor;
        correction.ReviewedAt = DateTime.UtcNow;
        correction.UpdatedAt = DateTime.UtcNow;
        await _corrections.UpdateAsync(correction, ct);
        return ToDto(correction);
    }

    public async Task<AttendanceCorrectionDto?> GetAsync(Guid id, CancellationToken ct = default)
        => await _corrections.GetWithEmployeeAsync(id, ct) is { } c ? ToDto(c) : null;

    public async Task<PagedResult<AttendanceCorrectionDto>> GetPagedAsync(
        Guid? employeeId, Guid? reportingManagerId, AttendanceCorrectionStatus? status, int page, int pageSize, CancellationToken ct = default)
    {
        var (items, total) = await _corrections.GetPagedAsync(employeeId, reportingManagerId, status, page, pageSize, ct);
        return PagedResult<AttendanceCorrectionDto>.Create(items.Select(ToDto).ToList(), total, page, pageSize);
    }

    public async Task<IReadOnlyList<AttendanceCorrectionDto>> GetHistoryAsync(Guid employeeId, DateOnly date, CancellationToken ct = default)
        => (await _corrections.GetForDayAsync(employeeId, date, ct)).Select(ToDto).ToList();

    // ── Rules ──────────────────────────────────────────────────────────────────────────────────

    private async Task EnsureCorrectableAsync(Guid employeeId, DateOnly date, TimeOnly? timeIn, TimeOnly? timeOut, CancellationToken ct)
    {
        AttendanceService.EnsureValidTimes(timeIn, timeOut);

        var today = DateOnly.FromDateTime(PhilippineTime.Now(_clock));
        if (date > today)
            throw new DomainException($"{date:MMM d, yyyy} hasn't happened yet, so there is nothing to correct.");

        await EnsureNotPaidAsync(employeeId, date, ct);
    }

    private async Task EnsureNotPaidAsync(Guid employeeId, DateOnly date, CancellationToken ct)
    {
        if (await _payrollRuns.GetPaidRunCoveringAsync(employeeId, date, ct) is { } run)
            throw new DomainException(
                $"{date:MMM d, yyyy} was paid in payroll run {run.RunNumber} ({run.PeriodLabel}), so its attendance can't be changed. " +
                "Settle the difference as an adjustment in a later run.");
    }

    /// <summary>A pending request, decided by someone other than the employee who asked.</summary>
    private async Task<AttendanceCorrection> PendingDecidableByAsync(Guid id, Guid deciderEmployeeId, CancellationToken ct)
    {
        var correction = await _corrections.GetWithEmployeeAsync(id, ct)
            ?? throw new KeyNotFoundException($"Attendance correction {id} not found.");
        if (correction.Status != AttendanceCorrectionStatus.Pending)
            throw new DomainException($"This correction is already {correction.Status.ToString().ToLowerInvariant()}.");
        if (correction.EmployeeId == deciderEmployeeId)
            throw new DomainException("You can't decide on your own attendance correction.");
        return correction;
    }

    private static string ValidReason(string? reason)
    {
        var trimmed = reason?.Trim() ?? "";
        if (trimmed.Length == 0)
            throw new DomainException("Give a reason for the correction.");
        if (trimmed.Length > MaxReasonLength)
            throw new DomainException($"Keep the reason to {MaxReasonLength} characters.");
        return trimmed;
    }

    private static DateTime? At(DateOnly date, TimeOnly? time) => time is { } t ? date.ToDateTime(t, DateTimeKind.Utc) : null;

    private static AttendanceCorrectionDto ToDto(AttendanceCorrection c) => new(
        c.Id, c.EmployeeId, c.Employee is null ? "" : $"{c.Employee.FirstName} {c.Employee.LastName}", c.Employee?.EmployeeNumber ?? "",
        c.AttendanceDate, c.PreviousTimeIn, c.PreviousTimeOut, c.NewTimeIn, c.NewTimeOut,
        c.Reason, c.Source.ToString(), c.Status.ToString(), c.RequestedBy, c.CreatedAt,
        c.ReviewedBy, c.ReviewedAt, c.RejectionReason);
}
