using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Payroll.Interfaces;

namespace PeopleCore.API.Controllers.Payroll;

/// <summary>
/// Payslip downloads. Deliberately carries NO class-level <c>[Authorize(Roles = ...)]</c> - every
/// other payroll controller does, but that would lock employees out of their own payslips.
/// Instead the two HR actions below (<see cref="GetPayslip"/>, <see cref="GetRunPayslips"/>)
/// each carry their own role restriction, and the two self-service actions
/// (<see cref="GetMyPayslip"/>, <see cref="GetMyPayslips"/>) carry a bare <see cref="AuthorizeAttribute"/>
/// so any authenticated employee can reach them - the security boundary for those two is not the
/// role check but the fact that they take no employee id at all (see their doc comments).
/// </summary>
[ApiController]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly IPayslipService _payslips;
    private readonly IPayrollRunService _runs;
    private readonly ICurrentUserService _currentUser;

    public ReportsController(
        IPayslipService payslips,
        IPayrollRunService runs,
        ICurrentUserService currentUser)
    {
        _payslips = payslips;
        _runs = runs;
        _currentUser = currentUser;
    }

    /// <summary>HR/payroll staff pulling a named employee's payslip - the employee id here is a route parameter on purpose, restricted to payroll roles.</summary>
    [HttpGet("payslip/{runId:guid}/{employeeId:guid}")]
    [Authorize(Roles = "Admin,HRManager,PayrollService")]
    public async Task<IActionResult> GetPayslip(Guid runId, Guid employeeId, CancellationToken ct = default)
    {
        var pdf = await _payslips.GenerateAsync(runId, employeeId, ct);
        if (pdf is null)
            return NotFound();

        var run = await _runs.GetAsync(runId, ct);
        var employeeNumber = run?.Employees.FirstOrDefault(e => e.EmployeeId == employeeId)?.EmployeeNumber;
        var fileName = $"Payslip-{run?.RunNumber ?? runId.ToString()}-{employeeNumber ?? employeeId.ToString()}.pdf";

        return File(pdf, "application/pdf", fileName);
    }

    /// <summary>HR/payroll staff pulling the whole run merged into one PDF - restricted to payroll roles.</summary>
    [HttpGet("payslips/{runId:guid}")]
    [Authorize(Roles = "Admin,HRManager,PayrollService")]
    public async Task<IActionResult> GetRunPayslips(Guid runId, CancellationToken ct = default)
    {
        var pdf = await _payslips.GenerateForRunAsync(runId, ct);
        if (pdf is null)
            return NotFound();

        var run = await _runs.GetAsync(runId, ct);
        var fileName = $"Payslips-{run?.RunNumber ?? runId.ToString()}.pdf";

        return File(pdf, "application/pdf", fileName);
    }

    /// <summary>
    /// An employee's own payslip for a run. Bare [Authorize] - no roles - because every
    /// authenticated employee is entitled to their own payslip. The route deliberately takes NO
    /// employee id: the caller is resolved from the employee_id claim ICurrentUserService reads
    /// off the JWT, never from anything the caller supplies. Accepting an employee id here -
    /// even "for convenience" - would let any authenticated user read anyone else's payslip by
    /// editing a GUID in the URL. If a future feature needs a payslip for someone other than the
    /// caller, that belongs on GetPayslip above, behind the payroll-role check, not here.
    /// </summary>
    [HttpGet("my-payslip/{runId:guid}")]
    [Authorize]
    public async Task<IActionResult> GetMyPayslip(Guid runId, CancellationToken ct = default)
    {
        var employeeId = _currentUser.EmployeeId;
        if (employeeId is null)
            return Forbid();

        var pdf = await _payslips.GenerateForSelfServiceAsync(runId, employeeId.Value, ct);
        if (pdf is null)
            return NotFound();

        var run = await _runs.GetAsync(runId, ct);
        var fileName = $"Payslip-{run?.RunNumber ?? runId.ToString()}.pdf";

        return File(pdf, "application/pdf", fileName);
    }

    /// <summary>
    /// The runs the caller appears in, for the ESS payslip list. Same reasoning as
    /// <see cref="GetMyPayslip"/>: no employee id in the route, the caller is resolved from the
    /// employee_id claim, and <see cref="IPayslipService.GetMyPayslipsAsync"/> is scoped to
    /// exactly that id - returning another employee's net pay here would leak compensation just
    /// as surely as serving their PDF would.
    /// </summary>
    [HttpGet("my-payslips")]
    [Authorize]
    public async Task<IActionResult> GetMyPayslips(CancellationToken ct = default)
    {
        var employeeId = _currentUser.EmployeeId;
        if (employeeId is null)
            return Forbid();

        return Ok(await _payslips.GetMyPayslipsAsync(employeeId.Value, ct));
    }
}
