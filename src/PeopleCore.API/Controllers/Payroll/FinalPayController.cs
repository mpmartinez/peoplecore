using Microsoft.AspNetCore.Mvc;
using PeopleCore.API.Authorization;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Application.Payroll.FinalPay;

namespace PeopleCore.API.Controllers.Payroll;

/// <summary>
/// A separation's final pay: creating it, changing HR's inputs while it is still a Draft or
/// For-approval run, and reading it back. Approving and paying it go through
/// <see cref="PayrollRunsController"/> like any other run. A thin pass-through to
/// <see cref="IFinalPayService"/>: <see cref="Domain.Exceptions.DomainException"/>/<see cref="KeyNotFoundException"/>
/// it throws are mapped to 400/404 by the exception middleware, not here.
/// </summary>
[ApiController]
[Route("api/separations/{separationId:guid}/final-pay")]
[RequirePermission(Permissions.PayrollManage)]
public class FinalPayController : ControllerBase
{
    private readonly IFinalPayService _service;

    public FinalPayController(IFinalPayService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<FinalPaySummaryDto>> Get(Guid separationId, CancellationToken ct = default)
    {
        var summary = await _service.GetAsync(separationId, ct);
        return summary is null ? NotFound() : Ok(summary);
    }

    [HttpPost]
    public async Task<ActionResult<FinalPaySummaryDto>> Create(
        Guid separationId, FinalPayRequest request, CancellationToken ct = default)
    {
        var summary = await _service.CreateAsync(separationId, request, ct);
        return CreatedAtAction(nameof(Get), new { separationId }, summary);
    }

    [HttpPut]
    public async Task<ActionResult<FinalPaySummaryDto>> Update(
        Guid separationId, FinalPayRequest request, CancellationToken ct = default)
        => Ok(await _service.UpdateAsync(separationId, request, ct));
}
