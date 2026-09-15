using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.API.Authorization;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Scheduling.DTOs;
using PeopleCore.Application.Scheduling.Interfaces;

namespace PeopleCore.API.Controllers.Scheduling;

[Authorize]
[ApiController]
[Route("api/shift-assignments")]
public class ShiftAssignmentsController : ControllerBase
{
    private readonly IShiftService _service;
    private readonly IEmployeeAccessService _access;

    public ShiftAssignmentsController(IShiftService service, IEmployeeAccessService access)
    {
        _service = service;
        _access = access;
    }

    /// <summary>
    /// Open to any signed-in user, but the employee id in the route is caller-supplied: HR staff see
    /// anyone's schedule, a Manager their direct reports', everybody else only their own.
    /// </summary>
    [HttpGet("{employeeId:guid}/schedule")]
    [Authorize]
    public async Task<IActionResult> GetSchedule(
        Guid employeeId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken ct = default)
    {
        if (!await _access.CanViewAsync(employeeId, ct))
            return Forbid();

        return Ok(await _service.GetEmployeeScheduleAsync(employeeId, from, to, ct));
    }

    [HttpPost]
    [RequirePermission(Permissions.SchedulingManage)]
    public async Task<IActionResult> AssignShift([FromBody] AssignShiftRequest request, CancellationToken ct = default)
    {
        await _service.AssignShiftAsync(request, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.SchedulingManage)]
    public async Task<IActionResult> RemoveAssignment(Guid id, CancellationToken ct = default)
    {
        await _service.RemoveAssignmentAsync(id, ct);
        return NoContent();
    }
}
