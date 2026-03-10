using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Organization.DTOs;
using PeopleCore.Application.Organization.Interfaces;

namespace PeopleCore.API.Controllers.Organization;

[ApiController]
[Route("api/departments")]
[Authorize]
public class DepartmentsController : ControllerBase
{
    private readonly IDepartmentService _service;
    public DepartmentsController(IDepartmentService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery][Range(1, int.MaxValue)] int page = 1,
        [FromQuery][Range(1, 100)] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(await _service.GetAllAsync(page, pageSize, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default)
        => Ok(await _service.GetByIdAsync(id, ct));

    [HttpPost]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> Create([FromBody] CreateDepartmentDto dto, CancellationToken ct = default)
    {
        var result = await _service.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDepartmentDto dto, CancellationToken ct = default)
        => Ok(await _service.UpdateAsync(id, dto, ct));

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        await _service.DeleteAsync(id, ct);
        return NoContent();
    }
}
