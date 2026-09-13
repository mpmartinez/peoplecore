using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Scheduling.DTOs;
using PeopleCore.Application.Scheduling.Interfaces;

namespace PeopleCore.API.Controllers.Scheduling;

[Authorize]
[ApiController]
[Route("api/shift-assignments")]
public class ShiftAssignmentsController : ControllerBase
{
    /// <summary>
    /// The roles that may read any employee's schedule. Managers are organisation-wide for now:
    /// nothing scopes them to their direct reports.
    /// </summary>
    private const string ScheduleReaderRoles = "Admin,HRManager,Manager";

    private readonly IShiftService _service;
    private readonly ICurrentUserService _currentUser;

    public ShiftAssignmentsController(IShiftService service, ICurrentUserService currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Open to any signed-in user, but the employee id in the route is caller-supplied: schedule
    /// readers see anyone's schedule, everybody else only their own. A caller with no employee_id
    /// claim matches nobody, since a null never equals the route's <see cref="Guid"/>.
    /// </summary>
    [HttpGet("{employeeId:guid}/schedule")]
    [Authorize]
    public async Task<IActionResult> GetSchedule(
        Guid employeeId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken ct = default)
    {
        var isReader = ScheduleReaderRoles.Split(',', StringSplitOptions.TrimEntries).Any(_currentUser.IsInRole);
        if (!isReader && _currentUser.EmployeeId != employeeId)
            return Forbid();

        return Ok(await _service.GetEmployeeScheduleAsync(employeeId, from, to, ct));
    }

    [HttpPost]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> AssignShift([FromBody] AssignShiftRequest request, CancellationToken ct = default)
    {
        await _service.AssignShiftAsync(request, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> RemoveAssignment(Guid id, CancellationToken ct = default)
    {
        await _service.RemoveAssignmentAsync(id, ct);
        return NoContent();
    }
}
