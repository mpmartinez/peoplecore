using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Scheduling.DTOs;
using PeopleCore.Application.Scheduling.Interfaces;

namespace PeopleCore.API.Controllers.Scheduling;

[ApiController]
[Route("api/shift-assignments")]
public class ShiftAssignmentsController : ControllerBase
{
    private readonly IShiftService _service;
    public ShiftAssignmentsController(IShiftService service) => _service = service;

    [HttpGet("{employeeId:guid}/schedule")]
    [Authorize]
    public async Task<IActionResult> GetSchedule(
        Guid employeeId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken ct = default)
        => Ok(await _service.GetEmployeeScheduleAsync(employeeId, from, to, ct));

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
