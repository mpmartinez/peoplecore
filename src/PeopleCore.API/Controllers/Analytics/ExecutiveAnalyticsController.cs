using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using PeopleCore.Application.Analytics.DTOs;
using PeopleCore.Application.Analytics.Interfaces;

namespace PeopleCore.API.Controllers.Analytics;

[ApiController]
[Route("api/analytics/executive")]
[Authorize(Roles = "Admin")]
public class ExecutiveAnalyticsController : ControllerBase
{
    private readonly IExecutiveAnalyticsService _service;
    private readonly IMemoryCache _cache;

    public ExecutiveAnalyticsController(IExecutiveAnalyticsService service, IMemoryCache cache)
    {
        _service = service;
        _cache = cache;
    }

    [HttpGet("workforce-summary")]
    public async Task<IActionResult> GetWorkforceSummary(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        CancellationToken ct = default)
    {
        var key = $"analytics:executive:workforce-summary:{from}:{to}";
        if (!_cache.TryGetValue(key, out object? result))
        {
            var data = await _service.GetWorkforceSummaryAsync(from, to, ct);
            result = Wrap(from, to, data);
            _cache.Set(key, result, TimeSpan.FromMinutes(15));
        }
        return Ok(result);
    }

    [HttpGet("hiring-trend")]
    public async Task<IActionResult> GetHiringTrend(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        CancellationToken ct = default)
    {
        var key = $"analytics:executive:hiring-trend:{from}:{to}";
        if (!_cache.TryGetValue(key, out object? result))
        {
            var data = await _service.GetHiringTrendAsync(from, to, ct);
            result = Wrap(from, to, data);
            _cache.Set(key, result, TimeSpan.FromMinutes(15));
        }
        return Ok(result);
    }

    [HttpGet("attrition-rate")]
    public async Task<IActionResult> GetAttritionRate(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] string groupBy = "month", CancellationToken ct = default)
    {
        var key = $"analytics:executive:attrition-rate:{from}:{to}:{groupBy}";
        if (!_cache.TryGetValue(key, out object? result))
        {
            var data = await _service.GetAttritionRateAsync(from, to, groupBy, ct);
            result = Wrap(from, to, data);
            _cache.Set(key, result, TimeSpan.FromMinutes(15));
        }
        return Ok(result);
    }

    [HttpGet("leave-summary")]
    public async Task<IActionResult> GetLeaveSummary(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        CancellationToken ct = default)
    {
        var key = $"analytics:executive:leave-summary:{from}:{to}";
        if (!_cache.TryGetValue(key, out object? result))
        {
            var data = await _service.GetLeaveSummaryAsync(from, to, ct);
            result = Wrap(from, to, data);
            _cache.Set(key, result, TimeSpan.FromMinutes(15));
        }
        return Ok(result);
    }

    [HttpGet("performance-overview")]
    public async Task<IActionResult> GetPerformanceOverview(
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        [FromQuery] Guid? reviewCycleId = null, CancellationToken ct = default)
    {
        var fromDate = from ?? new DateOnly(DateTime.UtcNow.Year, 1, 1);
        var toDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var key = $"analytics:executive:performance-overview:{fromDate}:{toDate}:{reviewCycleId}";
        if (!_cache.TryGetValue(key, out object? result))
        {
            var data = await _service.GetPerformanceOverviewAsync(reviewCycleId, ct);
            result = Wrap(fromDate, toDate, data);
            _cache.Set(key, result, TimeSpan.FromMinutes(15));
        }
        return Ok(result);
    }

    private static AnalyticsResponse<T> Wrap<T>(DateOnly from, DateOnly to, IReadOnlyList<T> data)
        => new(new AnalyticsPeriod(from, to), data, DateTime.UtcNow);

    private static AnalyticsResponse<T> Wrap<T>(DateOnly from, DateOnly to, T singleItem)
        => new(new AnalyticsPeriod(from, to), new[] { singleItem }, DateTime.UtcNow);
}
