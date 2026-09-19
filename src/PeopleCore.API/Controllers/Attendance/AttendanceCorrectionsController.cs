using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.API.Authorization;
using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Domain.Enums;

namespace PeopleCore.API.Controllers.Attendance;

/// <summary>
/// Corrections to recorded time-ins and time-outs. HR edits a day directly; an employee asks for
/// their own day to be fixed and an approver decides, as with leave and overtime. Every employee id
/// is caller-supplied, so each read carries an ownership check through <see cref="IEmployeeAccessService"/>,
/// and a decision needs the right to manage that employee. Days inside a paid payroll run are refused
/// by the service whichever way the change comes in.
/// </summary>
[ApiController]
[Route("api/attendance-corrections")]
[Authorize]
public class AttendanceCorrectionsController : ControllerBase
{
    private readonly IAttendanceCorrectionService _service;
    private readonly ICurrentUserService _currentUser;
    private readonly IEmployeeAccessService _access;

    public AttendanceCorrectionsController(
        IAttendanceCorrectionService service, ICurrentUserService currentUser, IEmployeeAccessService access)
    {
        _service = service;
        _currentUser = currentUser;
        _access = access;
    }

    private string Actor => _currentUser.Email ?? "unknown";

    /// <summary>
    /// Naming an employee needs access to that employee. With none, HR staff get everyone's
    /// corrections, a Manager their direct reports' (the approval queue), and anyone else is refused.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? employeeId, [FromQuery] AttendanceCorrectionStatus? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (employeeId is { } id)
        {
            if (!await _access.CanViewAsync(id, ct))
                return Forbid();
            return Ok(await _service.GetPagedAsync(id, null, status, page, pageSize, ct));
        }

        var scope = _access.GetUnfilteredListScope();
        if (!scope.IsAllowed)
            return Forbid();
        return Ok(await _service.GetPagedAsync(null, scope.ReportingManagerId, status, page, pageSize, ct));
    }

    /// <summary>Every change made to one employee's day, oldest first.</summary>
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory([FromQuery] Guid employeeId, [FromQuery] DateOnly date, CancellationToken ct = default)
    {
        if (!await _access.CanViewAsync(employeeId, ct))
            return Forbid();
        return Ok(await _service.GetHistoryAsync(employeeId, date, ct));
    }

    /// <summary>HR sets a day's times directly. Applied at once and kept in the day's history.</summary>
    [HttpPost]
    [RequirePermission(Permissions.AttendanceManage)]
    public async Task<IActionResult> Correct([FromBody] CorrectAttendanceDto dto, CancellationToken ct = default)
        => Ok(await _service.CorrectAsync(dto, Actor, AttendanceCorrectionSource.HrEdit, ct));

    /// <summary>Self-service: an employee asks for their own day to be corrected. The employee comes from the caller's claim.</summary>
    [HttpPost("requests")]
    public async Task<IActionResult> RequestCorrection([FromBody] RequestAttendanceCorrectionDto dto, CancellationToken ct = default)
    {
        if (_currentUser.EmployeeId is not { } employeeId)
            return Forbid();
        return StatusCode(201, await _service.RequestAsync(employeeId, dto, Actor, ct));
    }

    [HttpPut("{id:guid}/approve")]
    [RequirePermission(Permissions.ApprovalsTeam, Permissions.ApprovalsAll)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct = default)
    {
        if (await DeciderAsync(id, ct) is not { } approver)
            return Forbid();
        return Ok(await _service.ApproveAsync(id, approver, Actor, ct));
    }

    [HttpPut("{id:guid}/reject")]
    [RequirePermission(Permissions.ApprovalsTeam, Permissions.ApprovalsAll)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectAttendanceCorrectionDto dto, CancellationToken ct = default)
    {
        if (await DeciderAsync(id, ct) is not { } rejecter)
            return Forbid();
        return Ok(await _service.RejectAsync(id, rejecter, Actor, dto.Reason, ct));
    }

    /// <summary>
    /// The caller's own employee id, when they may decide on this correction: an approver for everyone,
    /// or a team approver for their direct report. Null when they may not. A missing correction is left
    /// for the service to report as not found.
    /// </summary>
    private async Task<Guid?> DeciderAsync(Guid correctionId, CancellationToken ct)
    {
        if (_currentUser.EmployeeId is not { } caller)
            return null;
        if (await _service.GetAsync(correctionId, ct) is not { } correction)
            return caller;
        return await _access.CanManageAsync(correction.EmployeeId, ct) ? caller : null;
    }
}
