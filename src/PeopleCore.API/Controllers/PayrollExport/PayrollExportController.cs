using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.API.Authorization;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.PayrollIntegration.Interfaces;

namespace PeopleCore.API.Controllers.PayrollExport;

[ApiController]
[Route("api/payroll-export")]
[RequirePermission(Permissions.PayrollManage)]
public class PayrollExportController : ControllerBase
{
    private readonly IPayrollExportService _service;
    private readonly ICurrentUserService _currentUser;

    public PayrollExportController(IPayrollExportService service, ICurrentUserService currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    [HttpGet("employees")]
    public async Task<IActionResult> GetEmployees(CancellationToken ct)
        => Ok(await _service.GetEmployeeMasterDataAsync(ct));

    [HttpGet("attendance-summary")]
    public async Task<IActionResult> GetAttendanceSummary(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] Guid? employeeId, CancellationToken ct)
        => Ok(await _service.GetAttendanceSummaryAsync(from, to, employeeId, ct));

    /// <summary>Confidential types (VAWC) go out as "Leave" with no code unless the caller also holds <c>approvals.all</c>.</summary>
    [HttpGet("approved-leaves")]
    public async Task<IActionResult> GetApprovedLeaves(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
        => Ok(await _service.GetApprovedLeavesAsync(
            from, to, showConfidentialTypes: _currentUser.HasPermission(Permissions.ApprovalsAll), ct));

    [HttpGet("approved-overtime")]
    public async Task<IActionResult> GetApprovedOvertime(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
        => Ok(await _service.GetApprovedOvertimeAsync(from, to, ct));

    [HttpGet("status-changes")]
    public async Task<IActionResult> GetStatusChanges(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
        => Ok(await _service.GetStatusChangesAsync(from, to, ct));
}
