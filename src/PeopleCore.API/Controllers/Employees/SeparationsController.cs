using Microsoft.AspNetCore.Mvc;
using PeopleCore.API.Authorization;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Application.Employees.DTOs;
using PeopleCore.Application.Employees.Interfaces;

namespace PeopleCore.API.Controllers.Employees;

/// <summary>
/// Recording, completing and cancelling separations, and the clearance checklist that gates
/// final pay. A thin pass-through to <see cref="ISeparationService"/>: every validation rule
/// (notice periods, authorized-cause requirements, clearance-item ordering) lives there, and
/// <see cref="Domain.Exceptions.DomainException"/>/<see cref="KeyNotFoundException"/> it throws
/// are mapped to 400/404 by the exception middleware, not here.
/// </summary>
[ApiController]
[Route("api/separations")]
[RequirePermission(Permissions.EmployeesManage)]
public class SeparationsController : ControllerBase
{
    private readonly ISeparationService _service;

    public SeparationsController(ISeparationService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SeparationDto>>> List(CancellationToken ct = default)
        => Ok(await _service.ListAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SeparationDto>> Get(Guid id, CancellationToken ct = default)
    {
        var dto = await _service.GetAsync(id, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPost]
    public async Task<ActionResult<SeparationDto>> Record(RecordSeparationRequest request, CancellationToken ct = default)
    {
        var dto = await _service.RecordAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    [HttpPost("{id:guid}/mark-separated")]
    public async Task<ActionResult<SeparationDto>> MarkSeparated(Guid id, CancellationToken ct = default)
        => Ok(await _service.MarkSeparatedAsync(id, ct));

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct = default)
    {
        await _service.CancelAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/clearance")]
    public async Task<ActionResult<SeparationDto>> AddClearanceItem(
        Guid id, AddClearanceItemRequest request, CancellationToken ct = default)
        => Ok(await _service.AddClearanceItemAsync(id, request.Name, ct));

    [HttpPost("{id:guid}/clearance/{itemId:guid}/clear")]
    public async Task<ActionResult<SeparationDto>> ClearItem(
        Guid id, Guid itemId, ClearItemRequest request, CancellationToken ct = default)
        => Ok(await _service.ClearItemAsync(id, itemId, request.Note, ct));

    [HttpPost("{id:guid}/clearance/{itemId:guid}/undo")]
    public async Task<ActionResult<SeparationDto>> UndoClearItem(Guid id, Guid itemId, CancellationToken ct = default)
        => Ok(await _service.UndoClearItemAsync(id, itemId, ct));

    [HttpDelete("{id:guid}/clearance/{itemId:guid}")]
    public async Task<ActionResult<SeparationDto>> DeleteClearanceItem(Guid id, Guid itemId, CancellationToken ct = default)
        => Ok(await _service.DeleteClearanceItemAsync(id, itemId, ct));
}
