using Microsoft.AspNetCore.Mvc;
using PeopleCore.API.Authorization;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Application.Payroll.Maternity;

namespace PeopleCore.API.Controllers.Payroll;

/// <summary>
/// The SSS maternity benefit claims: listing them, opening one for an approved maternity leave
/// request, the suggested daily allowance, setting it, and recording the reimbursement or denial.
/// Advancing the benefit goes through <see cref="PayrollRunsController"/>. A thin pass-through to
/// <see cref="IMaternityClaimService"/>: <see cref="Domain.Exceptions.DomainException"/>/<see cref="KeyNotFoundException"/>
/// it throws are mapped to 400/404 by the exception middleware, not here.
/// </summary>
[ApiController]
[Route("api/maternity-claims")]
[RequirePermission(Permissions.PayrollManage)]
public class MaternityClaimsController : ControllerBase
{
    private readonly IMaternityClaimService _service;

    public MaternityClaimsController(IMaternityClaimService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<MaternityClaimsSummaryDto>> List(CancellationToken ct = default)
        => Ok(await _service.ListAsync(ct));

    /// <summary>Approved maternity leave requests that have no claim yet.</summary>
    [HttpGet("eligible")]
    public async Task<ActionResult<IReadOnlyList<EligibleMaternityLeaveDto>>> Eligible(CancellationToken ct = default)
        => Ok(await _service.EligibleAsync(ct));

    /// <summary>The employees whose maternity benefit a payroll can advance: a Draft claim with an allowance.</summary>
    [HttpGet("ready")]
    public async Task<ActionResult<IReadOnlyList<Guid>>> Ready(CancellationToken ct = default)
        => Ok(await _service.ReadyEmployeeIdsAsync(ct));

    [HttpPost("{leaveRequestId:guid}")]
    public async Task<ActionResult<MaternityClaimDto>> Create(Guid leaveRequestId, CancellationToken ct = default)
    {
        var claim = await _service.CreateAsync(leaveRequestId, ct);
        return CreatedAtAction(nameof(List), null, claim);
    }

    [HttpGet("{id:guid}/suggestion")]
    public async Task<ActionResult<SuggestedAllowanceDto>> Suggestion(Guid id, CancellationToken ct = default)
        => Ok(await _service.SuggestAsync(id, ct));

    [HttpPut("{id:guid}/allowance")]
    public async Task<ActionResult<MaternityClaimDto>> SetAllowance(
        Guid id, SetAllowanceRequest request, CancellationToken ct = default)
        => Ok(await _service.SetAllowanceAsync(id, request, ct));

    [HttpPut("{id:guid}/reimburse")]
    public async Task<ActionResult<MaternityClaimDto>> Reimburse(
        Guid id, ReimburseRequest request, CancellationToken ct = default)
        => Ok(await _service.ReimburseAsync(id, request, ct));

    [HttpPut("{id:guid}/deny")]
    public async Task<ActionResult<MaternityClaimDto>> Deny(Guid id, DenyRequest request, CancellationToken ct = default)
        => Ok(await _service.DenyAsync(id, request, ct));

    /// <summary>Marks a Draft claim not SSS-qualified: her leave days are paid as ordinary salary.</summary>
    [HttpPut("{id:guid}/not-qualified")]
    public async Task<ActionResult<MaternityClaimDto>> MarkNotQualified(Guid id, NotQualifiedRequest request,
        CancellationToken ct = default)
        => Ok(await _service.MarkNotQualifiedAsync(id, request, ct));

    /// <summary>Sets a claim marked not SSS-qualified back to Draft, while no paid payroll covered her leave.</summary>
    [HttpPut("{id:guid}/reopen")]
    public async Task<ActionResult<MaternityClaimDto>> Reopen(Guid id, CancellationToken ct = default)
        => Ok(await _service.ReopenAsync(id, ct));

    /// <summary>Voids a Draft claim no unpaid run advances; the note says why.</summary>
    [HttpPut("{id:guid}/void")]
    public async Task<ActionResult<MaternityClaimDto>> Void(Guid id, VoidRequest request, CancellationToken ct = default)
        => Ok(await _service.VoidAsync(id, request, ct));

    /// <summary>Moves a claim whose leave was cancelled or rejected to the employee's refiled maternity leave.</summary>
    [HttpPut("{id:guid}/relink")]
    public async Task<ActionResult<MaternityClaimDto>> Relink(Guid id, RelinkRequest request, CancellationToken ct = default)
        => Ok(await _service.RelinkAsync(id, request, ct));
}
