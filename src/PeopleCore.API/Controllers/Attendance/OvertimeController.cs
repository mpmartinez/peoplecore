using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.API.Authorization;
using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Employees.Interfaces;

namespace PeopleCore.API.Controllers.Attendance;

/// <summary>
/// Overtime requests. The class-level [Authorize] only says the caller is signed in; every employee
/// id below is caller-supplied, so each employee-scoped action carries its own ownership check
/// through <see cref="IEmployeeAccessService"/>: HR staff read everyone's requests, a Manager their
/// direct reports', everybody their own. Filing is self-service for everyone. Approving and
/// rejecting are done as the employee in the caller's employee_id claim, and the service then
/// allows only that employee's direct reporting manager.
/// </summary>
[ApiController]
[Route("api/overtime-requests")]
[Authorize]
public class OvertimeController : ControllerBase
{
    private readonly IOvertimeService _service;
    private readonly ICurrentUserService _currentUser;
    private readonly IEmployeeAccessService _access;

    public OvertimeController(IOvertimeService service, ICurrentUserService currentUser, IEmployeeAccessService access)
    {
        _service = service;
        _currentUser = currentUser;
        _access = access;
    }

    /// <summary>
    /// Naming an employee needs access to that employee. With no <paramref name="employeeId"/> HR
    /// staff get everyone's overtime, a Manager their direct reports' (the approval queue), and
    /// anyone else is refused.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? employeeId,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (employeeId is { } id)
        {
            if (!await _access.CanViewAsync(id, ct))
                return Forbid();

            return Ok(await _service.GetAllAsync(id, null, status, page, pageSize, ct));
        }

        var scope = _access.GetUnfilteredListScope();
        if (!scope.IsAllowed)
            return Forbid();

        return Ok(await _service.GetAllAsync(null, scope.ReportingManagerId, status, page, pageSize, ct));
    }

    /// <summary>
    /// Self-service only. The body still carries an employee id (the service needs one), but it
    /// must be the caller's own: a mismatch is refused rather than silently rewritten. Overtime
    /// staff are no exception.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOvertimeRequestDto dto, CancellationToken ct = default)
    {
        var employeeId = _currentUser.EmployeeId;
        if (employeeId is null || dto.EmployeeId != employeeId)
            return Forbid();

        return StatusCode(201, await _service.CreateAsync(dto, ct));
    }

    /// <summary>
    /// Takes no approver. It used to read one from the body and hand it to the service's "direct
    /// reporting manager only" check, which then compared the employee's manager against whoever
    /// the caller claimed to be. The approver is now the employee in the caller's employee_id claim.
    /// </summary>
    [HttpPut("{id:guid}/approve")]
    [RequirePermission(Permissions.ApprovalsTeam, Permissions.ApprovalsAll)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct = default)
    {
        var approverId = _currentUser.EmployeeId;
        if (approverId is null)
            return Forbid();

        return Ok(await _service.ApproveAsync(id, approverId.Value, ct));
    }

    /// <summary>Held to the same rule as <see cref="Approve"/>: only the direct reporting manager.</summary>
    [HttpPut("{id:guid}/reject")]
    [RequirePermission(Permissions.ApprovalsTeam, Permissions.ApprovalsAll)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectOvertimeDto dto, CancellationToken ct = default)
    {
        var rejecterId = _currentUser.EmployeeId;
        if (rejecterId is null)
            return Forbid();

        return Ok(await _service.RejectAsync(id, rejecterId.Value, dto, ct));
    }
}
