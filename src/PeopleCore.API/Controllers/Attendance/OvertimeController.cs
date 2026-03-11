using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Attendance.Interfaces;

namespace PeopleCore.API.Controllers.Attendance;

[ApiController]
[Route("api/overtime-requests")]
[Authorize]
public class OvertimeController : ControllerBase
{
    private readonly IOvertimeService _service;
    public OvertimeController(IOvertimeService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? employeeId,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(await _service.GetAllAsync(employeeId, status, page, pageSize, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOvertimeRequestDto dto, CancellationToken ct = default)
        => Ok(await _service.CreateAsync(dto, ct));

    [HttpPut("{id:guid}/approve")]
    [Authorize(Roles = "Admin,HRManager,Manager")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ApproveOvertimeDto dto, CancellationToken ct = default)
        => Ok(await _service.ApproveAsync(id, dto, ct));

    [HttpPut("{id:guid}/reject")]
    [Authorize(Roles = "Admin,HRManager,Manager")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectOvertimeDto dto, CancellationToken ct = default)
        => Ok(await _service.RejectAsync(id, dto, ct));
}
