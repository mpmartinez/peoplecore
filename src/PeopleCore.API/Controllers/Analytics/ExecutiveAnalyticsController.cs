using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Analytics.Interfaces;

namespace PeopleCore.API.Controllers.Analytics;

[ApiController]
[Route("api/analytics/executive")]
[Authorize(Roles = "Admin")]
public class ExecutiveAnalyticsController : ControllerBase
{
    private readonly IExecutiveAnalyticsService _service;

    public ExecutiveAnalyticsController(IExecutiveAnalyticsService service)
    {
        _service = service;
    }

    [HttpGet("workforce-summary")]
    [ResponseCache(Duration = 900)]
    public async Task<IActionResult> GetWorkforceSummary(CancellationToken ct = default)
        => Ok(await _service.GetWorkforceSummaryAsync(ct));

    [HttpGet("hiring-trend")]
    [ResponseCache(Duration = 900)]
    public async Task<IActionResult> GetHiringTrend(
        [FromQuery] int months = 12, CancellationToken ct = default)
        => Ok(await _service.GetHiringTrendAsync(months, ct));

    [HttpGet("attrition-rate")]
    [ResponseCache(Duration = 900)]
    public async Task<IActionResult> GetAttritionRate(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        CancellationToken ct = default)
        => Ok(await _service.GetAttritionRateAsync(from, to, ct));

    [HttpGet("leave-summary")]
    [ResponseCache(Duration = 900)]
    public async Task<IActionResult> GetLeaveSummary(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        CancellationToken ct = default)
        => Ok(await _service.GetLeaveSummaryAsync(from, to, ct));

    [HttpGet("performance-overview")]
    [ResponseCache(Duration = 900)]
    public async Task<IActionResult> GetPerformanceOverview(
        [FromQuery] Guid? reviewCycleId = null, CancellationToken ct = default)
        => Ok(await _service.GetPerformanceOverviewAsync(reviewCycleId, ct));
}
