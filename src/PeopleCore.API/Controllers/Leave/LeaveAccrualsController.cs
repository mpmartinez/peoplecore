using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Leave.Interfaces;

namespace PeopleCore.API.Controllers.Leave;

[ApiController]
[Route("api/leave-accruals")]
[Authorize]
public class LeaveAccrualsController : ControllerBase
{
    private readonly ILeaveAccrualService _accrualService;
    private readonly IEmployeeAccessService _access;

    public LeaveAccrualsController(ILeaveAccrualService accrualService, IEmployeeAccessService access)
    {
        _accrualService = accrualService;
        _access = access;
    }

    [HttpPost("run-manual")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RunAccrualsAsync([FromQuery] int year, [FromQuery] int month, CancellationToken ct = default)
    {
        await _accrualService.RunAccrualsAsync(year, month, ct);
        return Ok(new { message = $"Accruals processed for {year}-{month:D2}." });
    }

    /// <summary>
    /// An employee's balance line by line, so the same rule as GET api/leave-balances: HR staff
    /// read anyone's, a Manager their direct reports', everybody else only their own.
    /// </summary>
    [HttpGet("/api/employees/{employeeId:guid}/accrual-history")]
    public async Task<IActionResult> GetEmployeeAccrualHistoryAsync(Guid employeeId, CancellationToken ct = default)
    {
        if (!await _access.CanViewAsync(employeeId, ct))
            return Forbid();

        return Ok(await _accrualService.GetEmployeeAccrualHistoryAsync(employeeId, ct));
    }
}
