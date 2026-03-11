using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Attendance.Interfaces;

namespace PeopleCore.API.Controllers.Attendance;

[ApiController]
[Route("api/holidays")]
[Authorize]
public class HolidaysController : ControllerBase
{
    private readonly IHolidayService _service;
    public HolidaysController(IHolidayService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetByYear([FromQuery] int? year, CancellationToken ct = default)
        => Ok(await _service.GetByYearAsync(year ?? DateTime.UtcNow.Year, ct));

    [HttpPost]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> Create([FromBody] CreateHolidayDto dto, CancellationToken ct = default)
        => StatusCode(201, await _service.CreateAsync(dto, ct));

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        await _service.DeleteAsync(id, ct);
        return NoContent();
    }
}
