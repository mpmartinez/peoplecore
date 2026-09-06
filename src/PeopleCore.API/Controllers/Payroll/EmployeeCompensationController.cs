using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Interfaces;

namespace PeopleCore.API.Controllers.Payroll;

/// <summary>
/// Compensation is the tightest surface in the application: this controller exposes a single
/// employee's compensation record by id only. It must never gain an endpoint that joins
/// compensation onto an employee list - that would leak BasicSalary/PayFrequency/TaxCode into a
/// bulk HR-facing response. See EmployeeCompensationDto's own remarks.
/// </summary>
[ApiController]
[Route("api/employee-compensation")]
[Authorize(Roles = "Admin,HRManager,PayrollService")]
public class EmployeeCompensationController : ControllerBase
{
    private readonly IEmployeeCompensationService _service;

    public EmployeeCompensationController(IEmployeeCompensationService service)
    {
        _service = service;
    }

    [HttpGet("{employeeId:guid}")]
    public async Task<IActionResult> GetByEmployee(Guid employeeId, CancellationToken ct = default)
    {
        var compensation = await _service.GetByEmployeeAsync(employeeId, ct);
        return compensation is null ? NotFound() : Ok(compensation);
    }

    [HttpPut("{employeeId:guid}")]
    public async Task<IActionResult> Upsert(Guid employeeId, [FromBody] UpsertCompensationRequest request, CancellationToken ct = default)
        => Ok(await _service.UpsertAsync(employeeId, request, ct));
}
