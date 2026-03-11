using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Attendance.Interfaces;

namespace PeopleCore.API.Controllers.Attendance;

[ApiController]
[Route("api/attendance")]
[Authorize]
public class AttendanceController : ControllerBase
{
    private readonly IAttendanceService _service;
    public AttendanceController(IAttendanceService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? employeeId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(await _service.GetAllAsync(employeeId, from, to, page, pageSize, ct));

    [HttpPost("time-in")]
    public async Task<IActionResult> TimeIn([FromBody] TimeInRequest request, CancellationToken ct = default)
        => StatusCode(201, await _service.TimeInAsync(request, ct));

    [HttpPost("time-out")]
    public async Task<IActionResult> TimeOut([FromBody] TimeOutRequest request, CancellationToken ct = default)
        => StatusCode(201, await _service.TimeOutAsync(request, ct));

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(
        [FromQuery] Guid employeeId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken ct = default)
        => Ok(await _service.GetSummaryAsync(employeeId, from, to, ct));

    [HttpPost("sync")]
    [Authorize(Roles = "Admin,HRManager,Service")]
    public async Task<IActionResult> Sync(
        [FromBody] IReadOnlyList<AttendancePunchDto> punches, CancellationToken ct = default)
        => Ok(await _service.SyncPunchesAsync(punches, ct));
}
