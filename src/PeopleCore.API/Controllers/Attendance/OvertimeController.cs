using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Common.Interfaces;

namespace PeopleCore.API.Controllers.Attendance;

/// <summary>
/// Overtime requests. The class-level [Authorize] only says the caller is signed in; every employee
/// id below is caller-supplied, so each employee-scoped action carries its own ownership check.
/// Overtime staff (<see cref="OvertimeStaffRoles"/>) read anyone's requests; everybody else reads
/// only their own. Filing is self-service for everyone. Approving and rejecting are done as the
/// employee in the caller's employee_id claim, and the service then allows only that employee's
/// direct reporting manager.
/// </summary>
[ApiController]
[Route("api/overtime-requests")]
[Authorize]
public class OvertimeController : ControllerBase
{
    /// <summary>
    /// The roles that may open the approval queue and so see any employee's requests. Managers are
    /// organisation-wide for reading; deciding is limited to the direct reporting manager.
    /// </summary>
    private const string OvertimeStaffRoles = "Admin,HRManager,Manager";

    private readonly IOvertimeService _service;
    private readonly ICurrentUserService _currentUser;

    public OvertimeController(IOvertimeService service, ICurrentUserService currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    private bool IsOvertimeStaff()
        => OvertimeStaffRoles.Split(',', StringSplitOptions.TrimEntries).Any(_currentUser.IsInRole);

    /// <summary>
    /// True when the caller is overtime staff or is themselves <paramref name="employeeId"/>. A null
    /// on either side matches nothing: a caller with no employee_id claim is nobody, and a missing
    /// employee filter means "everyone".
    /// </summary>
    private bool IsSelfOrOvertimeStaff(Guid? employeeId)
        => IsOvertimeStaff()
           || (employeeId is not null && _currentUser.EmployeeId == employeeId);

    /// <summary>
    /// With no <paramref name="employeeId"/> this is every employee's overtime, so only overtime
    /// staff may leave it out; everybody else must name themselves.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? employeeId,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!IsSelfOrOvertimeStaff(employeeId))
            return Forbid();

        return Ok(await _service.GetAllAsync(employeeId, status, page, pageSize, ct));
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
    [Authorize(Roles = "Admin,HRManager,Manager")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct = default)
    {
        var approverId = _currentUser.EmployeeId;
        if (approverId is null)
            return Forbid();

        return Ok(await _service.ApproveAsync(id, approverId.Value, ct));
    }

    /// <summary>Held to the same rule as <see cref="Approve"/>: only the direct reporting manager.</summary>
    [HttpPut("{id:guid}/reject")]
    [Authorize(Roles = "Admin,HRManager,Manager")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectOvertimeDto dto, CancellationToken ct = default)
    {
        var rejecterId = _currentUser.EmployeeId;
        if (rejecterId is null)
            return Forbid();

        return Ok(await _service.RejectAsync(id, rejecterId.Value, dto, ct));
    }
}
