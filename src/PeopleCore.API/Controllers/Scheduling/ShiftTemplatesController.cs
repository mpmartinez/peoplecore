using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Scheduling.DTOs;
using PeopleCore.Application.Scheduling.Interfaces;

namespace PeopleCore.API.Controllers.Scheduling;

[ApiController]
[Route("api/shift-templates")]
[Authorize(Roles = "Admin,HRManager")]
public class ShiftTemplatesController : ControllerBase
{
    private readonly IShiftService _service;
    public ShiftTemplatesController(IShiftService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct = default)
        => Ok(await _service.GetShiftTemplatesAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateShiftTemplateRequest request, CancellationToken ct = default)
    {
        var result = await _service.CreateShiftTemplateAsync(request, ct);
        return CreatedAtAction(nameof(GetAll), result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CreateShiftTemplateRequest request, CancellationToken ct = default)
    {
        await _service.UpdateShiftTemplateAsync(id, request, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        await _service.DeleteShiftTemplateAsync(id, ct);
        return NoContent();
    }
}
