using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Leave.DTOs;
using PeopleCore.Application.Leave.Interfaces;

namespace PeopleCore.API.Controllers.Leave;

/// <summary>
/// Leave types, requests and balances. The class-level [Authorize] only says the caller is signed
/// in; every employee id below - route, query string or body - is caller-supplied, so each
/// employee-scoped action carries its own ownership check through <see cref="IEmployeeAccessService"/>:
/// HR staff reach everyone's leave, a Manager their direct reports', everybody their own. Filing and
/// cancelling leave is self-service for everyone: the employee is the one in the caller's
/// employee_id claim, never one the caller names.
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
public class LeaveController : ControllerBase
{
    private readonly ILeaveTypeService _typeService;
    private readonly ILeaveRequestService _requestService;
    private readonly ILeaveBalanceService _balanceService;
    private readonly ICurrentUserService _currentUser;
    private readonly IEmployeeAccessService _access;

    public LeaveController(
        ILeaveTypeService typeService,
        ILeaveRequestService requestService,
        ILeaveBalanceService balanceService,
        ICurrentUserService currentUser,
        IEmployeeAccessService access)
    {
        _typeService = typeService;
        _requestService = requestService;
        _balanceService = balanceService;
        _currentUser = currentUser;
        _access = access;
    }

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
    /// Leave requests, reasons included. Naming an employee needs access to that employee. With no
    /// <paramref name="employeeId"/> HR staff get everyone's, a Manager gets their direct reports'
    /// (the approval queue), and anyone else is refused.
    /// </summary>
    [HttpGet("leave-requests")]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? employeeId, [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (employeeId is { } id)
        {
            if (!await _access.CanViewAsync(id, ct))
                return Forbid();

            return Ok(await _requestService.GetAllAsync(id, null, status, page, pageSize, ct));
        }

        var scope = _access.GetUnfilteredListScope();
        if (!scope.IsAllowed)
            return Forbid();

        return Ok(await _requestService.GetAllAsync(null, scope.ReportingManagerId, status, page, pageSize, ct));
    }

    /// <summary>
    /// The owner is only known once the request is loaded, so the check follows the read. A
    /// refused caller gets 403 rather than the request.
    /// </summary>
    [HttpGet("leave-requests/{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default)
    {
        var request = await _requestService.GetByIdAsync(id, ct);
        if (!await _access.CanViewAsync(request.EmployeeId, ct))
            return Forbid();

        return Ok(request);
    }

    /// <summary>
    /// Self-service only. The body still carries an employee id (the service needs one), but it
    /// must be the caller's own: a mismatch is refused rather than silently rewritten, so a client
    /// filing for the wrong person finds out. HR and managers are no exception - seeing someone's
    /// leave is not a licence to spend their balance.
    /// </summary>
    [HttpPost("leave-requests")]
    public async Task<IActionResult> Create([FromBody] CreateLeaveRequestDto dto, CancellationToken ct = default)
    {
        var employeeId = _currentUser.EmployeeId;
        if (employeeId is null || dto.EmployeeId != employeeId)
            return Forbid();

        return StatusCode(201, await _requestService.CreateAsync(dto, ct));
    }

    /// <summary>
    /// Approves as the employee in the caller's employee_id claim; an account with no employee
    /// record has nobody to record and is refused. HR staff approve anyone's leave, a Manager only
    /// a direct report's. The caller's own request is left to the service, which refuses it with
    /// its reason.
    /// </summary>
    [HttpPut("leave-requests/{id:guid}/approve")]
    [Authorize(Roles = "Admin,HRManager,Manager")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct = default)
    {
        var approverId = _currentUser.EmployeeId;
        if (approverId is null || !await MayDecideAsync(id, approverId.Value, ct))
            return Forbid();

        return Ok(await _requestService.ApproveAsync(id, approverId.Value, ct));
    }

    /// <summary>Held to the same rules as <see cref="Approve"/>.</summary>
    [HttpPut("leave-requests/{id:guid}/reject")]
    [Authorize(Roles = "Admin,HRManager,Manager")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectLeaveDto dto, CancellationToken ct = default)
    {
        var rejecterId = _currentUser.EmployeeId;
        if (rejecterId is null || !await MayDecideAsync(id, rejecterId.Value, ct))
            return Forbid();

        return Ok(await _requestService.RejectAsync(id, rejecterId.Value, dto, ct));
    }

    /// <summary>
    /// True when the decider may manage the request's employee, or the request is the decider's own -
    /// passed through so the service can refuse that with "You cannot approve your own leave request"
    /// instead of a bare 403.
    /// </summary>
    private async Task<bool> MayDecideAsync(Guid requestId, Guid deciderId, CancellationToken ct)
    {
        var request = await _requestService.GetByIdAsync(requestId, ct);
        return request.EmployeeId == deciderId || await _access.CanManageAsync(request.EmployeeId, ct);
    }

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
        if (!await _access.CanViewAsync(employeeId, ct))
            return Forbid();

        return Ok(await _balanceService.GetByEmployeeAsync(employeeId, year, ct));
    }
}
