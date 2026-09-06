using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Interfaces;

namespace PeopleCore.API.Controllers.Payroll;

[ApiController]
[Route("api/payroll-settings")]
[Authorize(Roles = "Admin,HRManager,PayrollService")]
public class PayrollSettingsController : ControllerBase
{
    private readonly IPayrollSettingsService _service;

    public PayrollSettingsController(IPayrollSettingsService service)
    {
        _service = service;
    }

    [HttpGet("{companyId:guid}")]
    public async Task<IActionResult> GetByCompany(Guid companyId, CancellationToken ct = default)
        => Ok(await _service.GetAsync(companyId, ct));

    [HttpPut("{companyId:guid}")]
    public async Task<IActionResult> Update(Guid companyId, [FromBody] PayrollSettingsDto dto, CancellationToken ct = default)
    {
        await _service.UpdateAsync(companyId, dto, ct);
        return NoContent();
    }
}
