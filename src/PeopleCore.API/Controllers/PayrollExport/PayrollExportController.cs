using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.PayrollIntegration.Interfaces;

namespace PeopleCore.API.Controllers.PayrollExport;

[ApiController]
[Route("api/payroll-export")]
[Authorize(Roles = "Admin,HRManager,PayrollService")]
public class PayrollExportController : ControllerBase
{
    private readonly IPayrollExportService _service;
    public PayrollExportController(IPayrollExportService service) => _service = service;

    [HttpGet("employees")]
    public async Task<IActionResult> GetEmployees(CancellationToken ct)
        => Ok(await _service.GetEmployeeMasterDataAsync(ct));

    [HttpGet("attendance-summary")]
    public async Task<IActionResult> GetAttendanceSummary(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] Guid? employeeId, CancellationToken ct)
        => Ok(await _service.GetAttendanceSummaryAsync(from, to, employeeId, ct));

    [HttpGet("approved-leaves")]
    public async Task<IActionResult> GetApprovedLeaves(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
        => Ok(await _service.GetApprovedLeavesAsync(from, to, ct));

    [HttpGet("approved-overtime")]
    public async Task<IActionResult> GetApprovedOvertime(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
        => Ok(await _service.GetApprovedOvertimeAsync(from, to, ct));

    [HttpGet("status-changes")]
    public async Task<IActionResult> GetStatusChanges(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
        => Ok(await _service.GetStatusChangesAsync(from, to, ct));
}
