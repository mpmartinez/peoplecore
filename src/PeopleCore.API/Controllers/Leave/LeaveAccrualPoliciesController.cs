using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Leave.DTOs;
using PeopleCore.Application.Leave.Interfaces;

namespace PeopleCore.API.Controllers.Leave;

[ApiController]
[Route("api/leave-accrual-policies")]
[Authorize(Roles = "Admin,HRManager")]
public class LeaveAccrualPoliciesController : ControllerBase
{
    private readonly ILeaveAccrualService _accrualService;

    public LeaveAccrualPoliciesController(ILeaveAccrualService accrualService)
    {
        _accrualService = accrualService;
    }

    [HttpGet]
    public async Task<IActionResult> GetPoliciesAsync([FromQuery] Guid leaveTypeId, CancellationToken ct = default)
        => Ok(await _accrualService.GetPoliciesAsync(leaveTypeId, ct));

    [HttpGet("{leaveTypeId:guid}/rules")]
    public async Task<IActionResult> GetRules(Guid leaveTypeId, CancellationToken ct = default)
        => Ok(await _accrualService.GetPoliciesAsync(leaveTypeId, ct));

    [HttpPost]
    public async Task<IActionResult> CreatePolicyAsync([FromBody] CreateLeaveAccrualPolicyRequest request, CancellationToken ct = default)
        => StatusCode(201, await _accrualService.CreatePolicyAsync(request, ct));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdatePolicyAsync(Guid id, [FromBody] CreateLeaveAccrualPolicyRequest request, CancellationToken ct = default)
    {
        await _accrualService.UpdatePolicyAsync(id, request, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeletePolicyAsync(Guid id, CancellationToken ct = default)
    {
        await _accrualService.DeletePolicyAsync(id, ct);
        return NoContent();
    }
}
