using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Leave.Interfaces;

namespace PeopleCore.API.Controllers.Leave;

[ApiController]
[Route("api/leave-accruals")]
[Authorize]
public class LeaveAccrualsController : ControllerBase
{
    /// <summary>Same roles as <see cref="LeaveController"/>'s leave staff: they read anyone's leave.</summary>
    private const string LeaveStaffRoles = "Admin,HRManager,Manager";

    private readonly ILeaveAccrualService _accrualService;
    private readonly ICurrentUserService _currentUser;

    public LeaveAccrualsController(ILeaveAccrualService accrualService, ICurrentUserService currentUser)
    {
        _accrualService = accrualService;
        _currentUser = currentUser;
    }

    [HttpPost("run-manual")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RunAccrualsAsync([FromQuery] int year, [FromQuery] int month, CancellationToken ct = default)
    {
        await _accrualService.RunAccrualsAsync(year, month, ct);
        return Ok(new { message = $"Accruals processed for {year}-{month:D2}." });
    }

    /// <summary>
    /// An employee's balance line by line, so the same rule as GET api/leave-balances: leave staff
    /// read anyone's, everybody else only their own.
    /// </summary>
    [HttpGet("/api/employees/{employeeId:guid}/accrual-history")]
    public async Task<IActionResult> GetEmployeeAccrualHistoryAsync(Guid employeeId, CancellationToken ct = default)
    {
        var isLeaveStaff = LeaveStaffRoles.Split(',', StringSplitOptions.TrimEntries).Any(_currentUser.IsInRole);
        if (!isLeaveStaff && _currentUser.EmployeeId != employeeId)
            return Forbid();

        return Ok(await _accrualService.GetEmployeeAccrualHistoryAsync(employeeId, ct));
    }
}
