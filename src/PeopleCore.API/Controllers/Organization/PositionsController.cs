using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Organization.DTOs;
using PeopleCore.Application.Organization.Interfaces;

namespace PeopleCore.API.Controllers.Organization;

[ApiController]
[Route("api/positions")]
[Authorize]
public class PositionsController : ControllerBase
{
    private readonly IPositionService _service;
    public PositionsController(IPositionService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? departmentId = null,
        CancellationToken ct = default)
        => Ok(await _service.GetAllAsync(page, pageSize, departmentId, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

    [HttpPost]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> Create([FromBody] CreatePositionDto dto, CancellationToken ct)
    {
        var result = await _service.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePositionDto dto, CancellationToken ct)
        => Ok(await _service.UpdateAsync(id, dto, ct));

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _service.DeleteAsync(id, ct);
        return NoContent();
    }
}
