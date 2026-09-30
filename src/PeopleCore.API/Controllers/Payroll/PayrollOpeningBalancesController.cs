using Microsoft.AspNetCore.Mvc;
using PeopleCore.API.Authorization;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Application.Payroll.OpeningBalances;

namespace PeopleCore.API.Controllers.Payroll;

/// <summary>
/// Payroll opening balances: what each employee was paid in a year before PeopleCore. A thin
/// pass-through to <see cref="IPayrollOpeningBalanceService"/>: <see cref="Domain.Exceptions.DomainException"/>/<see cref="KeyNotFoundException"/>
/// it throws are mapped to 400/404 by the exception middleware, not here.
/// </summary>
[ApiController]
[Route("api/payroll-opening-balances")]
[RequirePermission(Permissions.PayrollManage)]
public class PayrollOpeningBalancesController : ControllerBase
{
    private readonly IPayrollOpeningBalanceService _service;

    public PayrollOpeningBalancesController(IPayrollOpeningBalanceService service) => _service = service;

    /// <summary>The year's opening balances, each with its double-count warnings.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OpeningBalanceDto>>> List([FromQuery] int year, CancellationToken ct = default)
        => Ok(await _service.ListAsync(year, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OpeningBalanceDto>> Get(Guid id, CancellationToken ct = default)
        => Ok(await _service.GetAsync(id, ct));

    /// <summary>Records an employee's opening balance for a year; the answer carries the edit warning too.</summary>
    [HttpPost]
    public async Task<ActionResult<OpeningBalanceDto>> Create(OpeningBalanceRequest request, CancellationToken ct = default)
    {
        var balance = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = balance.Id }, balance);
    }

    /// <summary>Changes an opening balance's through date and figures; the answer carries the edit warning too.</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<OpeningBalanceDto>> Update(Guid id, OpeningBalanceRequest request,
        CancellationToken ct = default)
        => Ok(await _service.UpdateAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        await _service.DeleteAsync(id, ct);
        return NoContent();
    }
}
