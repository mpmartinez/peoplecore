using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Interfaces;

namespace PeopleCore.API.Controllers.Payroll;

[ApiController]
[Route("api/payroll-runs")]
[Authorize(Roles = "Admin,HRManager,PayrollService")]
public class PayrollRunsController : ControllerBase
{
    private readonly IPayrollRunService _service;

    public PayrollRunsController(IPayrollRunService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await _service.GetPagedAsync(page, pageSize, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default)
    {
        var run = await _service.GetAsync(id, ct);
        return run is null ? NotFound() : Ok(run);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePayrollRunRequest request, CancellationToken ct = default)
        => StatusCode(201, await _service.CreateAsync(request, ct));

    [HttpPut("{id:guid}/compute")]
    public async Task<IActionResult> Compute(Guid id, CancellationToken ct = default)
    {
        await _service.ComputeAsync(id, ct);
        return NoContent();
    }

    [HttpPut("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct = default)
    {
        await _service.ApproveAsync(id, ct);
        return NoContent();
    }

    [HttpPut("{id:guid}/mark-paid")]
    public async Task<IActionResult> MarkPaid(Guid id, CancellationToken ct = default)
    {
        await _service.MarkPaidAsync(id, ct);
        return NoContent();
    }
}
