using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Leave.Interfaces;

namespace PeopleCore.API.Controllers.Leave;

[ApiController]
[Route("api/leave-accruals")]
[Authorize]
public class LeaveAccrualsController : ControllerBase
{
    private readonly ILeaveAccrualService _accrualService;

    public LeaveAccrualsController(ILeaveAccrualService accrualService)
    {
        _accrualService = accrualService;
    }

    [HttpPost("run-manual")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RunAccrualsAsync([FromQuery] int year, [FromQuery] int month, CancellationToken ct = default)
    {
        await _accrualService.RunAccrualsAsync(year, month, ct);
        return Ok(new { message = $"Accruals processed for {year}-{month:D2}." });
    }

    [HttpGet("employees/{employeeId:guid}/history")]
    public async Task<IActionResult> GetEmployeeAccrualHistoryAsync(Guid employeeId, CancellationToken ct = default)
        => Ok(await _accrualService.GetEmployeeAccrualHistoryAsync(employeeId, ct));
}
