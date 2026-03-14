using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Scheduling.DTOs;
using PeopleCore.Application.Scheduling.Interfaces;

namespace PeopleCore.API.Controllers.Scheduling;

[ApiController]
[Route("api/rotating-patterns")]
[Authorize(Roles = "Admin,HRManager")]
public class RotatingPatternsController : ControllerBase
{
    private readonly IShiftService _service;
    public RotatingPatternsController(IShiftService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct = default)
        => Ok(await _service.GetRotatingPatternsAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRotatingPatternRequest request, CancellationToken ct = default)
    {
        var result = await _service.CreateRotatingPatternAsync(request, ct);
        return CreatedAtAction(nameof(GetAll), null, result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        await _service.DeleteRotatingPatternAsync(id, ct);
        return NoContent();
    }
}
