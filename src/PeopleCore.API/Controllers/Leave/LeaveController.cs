using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Leave.DTOs;
using PeopleCore.Application.Leave.Interfaces;

namespace PeopleCore.API.Controllers.Leave;

/// <summary>
/// Leave types, requests and balances. The class-level [Authorize] only says the caller is signed
/// in; every employee id below - route, query string or body - is caller-supplied, so each
/// employee-scoped action carries its own ownership check. Leave staff (<see cref="LeaveStaffRoles"/>)
/// read anyone's leave; everybody else reads only their own. Filing and cancelling leave is
/// self-service for everyone, including leave staff: the employee is the one in the caller's
/// employee_id claim, never one the caller names.
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
public class LeaveController : ControllerBase
{
    /// <summary>
    /// The roles that approve and reject leave, and so need to see any employee's requests and
    /// balances. Managers are organisation-wide for now: nothing scopes them to their direct reports.
    /// </summary>
    private const string LeaveStaffRoles = "Admin,HRManager,Manager";

    private readonly ILeaveTypeService _typeService;
    private readonly ILeaveRequestService _requestService;
    private readonly ILeaveBalanceService _balanceService;
    private readonly ICurrentUserService _currentUser;

    public LeaveController(
        ILeaveTypeService typeService,
        ILeaveRequestService requestService,
        ILeaveBalanceService balanceService,
        ICurrentUserService currentUser)
    {
        _typeService = typeService;
        _requestService = requestService;
        _balanceService = balanceService;
        _currentUser = currentUser;
    }

    private bool IsLeaveStaff()
        => LeaveStaffRoles.Split(',', StringSplitOptions.TrimEntries).Any(_currentUser.IsInRole);

    /// <summary>
    /// True when the caller is leave staff or is themselves <paramref name="employeeId"/>. A null
    /// on either side matches nothing: a caller with no employee_id claim is nobody, and a missing
    /// employee filter means "everyone" - letting null equal null would open both to all leave.
    /// </summary>
    private bool IsSelfOrLeaveStaff(Guid? employeeId)
        => IsLeaveStaff()
           || (employeeId is not null && _currentUser.EmployeeId == employeeId);

    // Leave Types
    [HttpGet("leave-types")]
    public async Task<IActionResult> GetLeaveTypes(CancellationToken ct = default)
        => Ok(await _typeService.GetAllAsync(ct));

    [HttpPost("leave-types")]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> CreateLeaveType([FromBody] CreateLeaveTypeDto dto, CancellationToken ct = default)
        => StatusCode(201, await _typeService.CreateAsync(dto, ct));

    [HttpPut("leave-types/{id:guid}")]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> UpdateLeaveType(Guid id, [FromBody] CreateLeaveTypeDto dto, CancellationToken ct = default)
        => Ok(await _typeService.UpdateAsync(id, dto, ct));

    [HttpDelete("leave-types/{id:guid}")]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> DeleteLeaveType(Guid id, CancellationToken ct = default)
    {
        await _typeService.DeleteAsync(id, ct);
        return NoContent();
    }

    // Leave Requests
    /// <summary>
    /// Leave requests, reasons included. With no <paramref name="employeeId"/> this is every
    /// employee's, so only leave staff may leave it out; everybody else must name themselves.
    /// </summary>
    [HttpGet("leave-requests")]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? employeeId, [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!IsSelfOrLeaveStaff(employeeId))
            return Forbid();

        return Ok(await _requestService.GetAllAsync(employeeId, status, page, pageSize, ct));
    }

    /// <summary>
    /// The owner is only known once the request is loaded, so the check follows the read. A
    /// refused caller gets 403 rather than the request.
    /// </summary>
    [HttpGet("leave-requests/{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default)
    {
        var request = await _requestService.GetByIdAsync(id, ct);
        if (!IsSelfOrLeaveStaff(request.EmployeeId))
            return Forbid();

        return Ok(request);
    }

    /// <summary>
    /// Self-service only. The body still carries an employee id (the service needs one), but it
    /// must be the caller's own: a mismatch is refused rather than silently rewritten, so a client
    /// filing for the wrong person finds out. Leave staff are no exception - seeing someone's leave
    /// is not a licence to spend their balance.
    /// </summary>
    [HttpPost("leave-requests")]
    public async Task<IActionResult> Create([FromBody] CreateLeaveRequestDto dto, CancellationToken ct = default)
    {
        var employeeId = _currentUser.EmployeeId;
        if (employeeId is null || dto.EmployeeId != employeeId)
            return Forbid();

        return StatusCode(201, await _requestService.CreateAsync(dto, ct));
    }

    [HttpPut("leave-requests/{id:guid}/approve")]
    [Authorize(Roles = "Admin,HRManager,Manager")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ApproveLeaveDto dto, CancellationToken ct = default)
        => Ok(await _requestService.ApproveAsync(id, dto, ct));

    [HttpPut("leave-requests/{id:guid}/reject")]
    [Authorize(Roles = "Admin,HRManager,Manager")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectLeaveDto dto, CancellationToken ct = default)
        => Ok(await _requestService.RejectAsync(id, dto, ct));

    /// <summary>
    /// Takes no employee id. It used to read one from the query string and hand it to the
    /// service's "only your own" check, which then compared the request against whoever the caller
    /// claimed to be. The canceller is now the employee in the caller's employee_id claim.
    /// </summary>
    [HttpPut("leave-requests/{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct = default)
    {
        var employeeId = _currentUser.EmployeeId;
        if (employeeId is null)
            return Forbid();

        await _requestService.CancelAsync(id, employeeId.Value, ct);
        return NoContent();
    }

    // Leave Balances
    [HttpGet("leave-balances/{employeeId:guid}")]
    public async Task<IActionResult> GetBalances(Guid employeeId, [FromQuery] int? year, CancellationToken ct = default)
    {
        if (!IsSelfOrLeaveStaff(employeeId))
            return Forbid();

        return Ok(await _balanceService.GetByEmployeeAsync(employeeId, year, ct));
    }
}
