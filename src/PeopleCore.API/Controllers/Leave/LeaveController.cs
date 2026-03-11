using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Leave.DTOs;
using PeopleCore.Application.Leave.Interfaces;

namespace PeopleCore.API.Controllers.Leave;

[ApiController]
[Route("api")]
[Authorize]
public class LeaveController : ControllerBase
{
    private readonly ILeaveTypeService _typeService;
    private readonly ILeaveRequestService _requestService;
    private readonly ILeaveBalanceService _balanceService;

    public LeaveController(
        ILeaveTypeService typeService,
        ILeaveRequestService requestService,
        ILeaveBalanceService balanceService)
    {
        _typeService = typeService;
        _requestService = requestService;
        _balanceService = balanceService;
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
    [HttpGet("leave-requests")]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? employeeId, [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(await _requestService.GetAllAsync(employeeId, status, page, pageSize, ct));

    [HttpGet("leave-requests/{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default)
        => Ok(await _requestService.GetByIdAsync(id, ct));

    [HttpPost("leave-requests")]
    public async Task<IActionResult> Create([FromBody] CreateLeaveRequestDto dto, CancellationToken ct = default)
        => StatusCode(201, await _requestService.CreateAsync(dto, ct));

    [HttpPut("leave-requests/{id:guid}/approve")]
    [Authorize(Roles = "Admin,HRManager,Manager")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ApproveLeaveDto dto, CancellationToken ct = default)
        => Ok(await _requestService.ApproveAsync(id, dto, ct));

    [HttpPut("leave-requests/{id:guid}/reject")]
    [Authorize(Roles = "Admin,HRManager,Manager")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectLeaveDto dto, CancellationToken ct = default)
        => Ok(await _requestService.RejectAsync(id, dto, ct));

    [HttpPut("leave-requests/{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, [FromQuery] Guid employeeId, CancellationToken ct = default)
    {
        await _requestService.CancelAsync(id, employeeId, ct);
        return NoContent();
    }

    // Leave Balances
    [HttpGet("leave-balances/{employeeId:guid}")]
    public async Task<IActionResult> GetBalances(Guid employeeId, [FromQuery] int? year, CancellationToken ct = default)
        => Ok(await _balanceService.GetByEmployeeAsync(employeeId, year, ct));
}
