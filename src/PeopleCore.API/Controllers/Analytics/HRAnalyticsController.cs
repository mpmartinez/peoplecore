using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using PeopleCore.Application.Analytics.Interfaces;

namespace PeopleCore.API.Controllers.Analytics;

[ApiController]
[Route("api/analytics/hr")]
[Authorize(Roles = "Admin,HRManager")]
public class HRAnalyticsController : ControllerBase
{
    private readonly IHRAnalyticsService _service;
    private readonly IMemoryCache _cache;

    public HRAnalyticsController(IHRAnalyticsService service, IMemoryCache cache)
    {
        _service = service;
        _cache = cache;
    }

    [HttpGet("headcount")]
    public async Task<IActionResult> GetHeadcount(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] Guid? departmentId = null, CancellationToken ct = default)
    {
        var key = $"analytics:hr:headcount:{from}:{to}:{departmentId}";
        if (!_cache.TryGetValue(key, out object? result))
        {
            result = await _service.GetHeadcountAsync(from, to, departmentId, ct);
            _cache.Set(key, result, TimeSpan.FromMinutes(15));
        }
        return Ok(result);
    }

    [HttpGet("turnover")]
    public async Task<IActionResult> GetTurnover(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] string groupBy = "month", CancellationToken ct = default)
    {
        var key = $"analytics:hr:turnover:{from}:{to}:{groupBy}";
        if (!_cache.TryGetValue(key, out object? result))
        {
            result = await _service.GetTurnoverAsync(from, to, groupBy, ct);
            _cache.Set(key, result, TimeSpan.FromMinutes(15));
        }
        return Ok(result);
    }

    [HttpGet("attendance")]
    public async Task<IActionResult> GetAttendance(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] Guid? departmentId = null, CancellationToken ct = default)
    {
        var key = $"analytics:hr:attendance:{from}:{to}:{departmentId}";
        if (!_cache.TryGetValue(key, out object? result))
        {
            result = await _service.GetAttendanceRateAsync(from, to, departmentId, ct);
            _cache.Set(key, result, TimeSpan.FromMinutes(15));
        }
        return Ok(result);
    }

    [HttpGet("leave-utilization")]
    public async Task<IActionResult> GetLeaveUtilization(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] Guid? departmentId = null, CancellationToken ct = default)
    {
        var key = $"analytics:hr:leave-utilization:{from}:{to}:{departmentId}";
        if (!_cache.TryGetValue(key, out object? result))
        {
            result = await _service.GetLeaveUtilizationAsync(from, to, departmentId, ct);
            _cache.Set(key, result, TimeSpan.FromMinutes(15));
        }
        return Ok(result);
    }

    [HttpGet("overtime")]
    public async Task<IActionResult> GetOvertime(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] Guid? departmentId = null, CancellationToken ct = default)
    {
        var key = $"analytics:hr:overtime:{from}:{to}:{departmentId}";
        if (!_cache.TryGetValue(key, out object? result))
        {
            result = await _service.GetOvertimeAsync(from, to, departmentId, ct);
            _cache.Set(key, result, TimeSpan.FromMinutes(15));
        }
        return Ok(result);
    }

    [HttpGet("recruitment-funnel")]
    public async Task<IActionResult> GetRecruitmentFunnel(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        CancellationToken ct = default)
    {
        var key = $"analytics:hr:recruitment-funnel:{from}:{to}";
        if (!_cache.TryGetValue(key, out object? result))
        {
            result = await _service.GetRecruitmentFunnelAsync(from, to, ct);
            _cache.Set(key, result, TimeSpan.FromMinutes(15));
        }
        return Ok(result);
    }

    [HttpGet("performance-distribution")]
    public async Task<IActionResult> GetPerformanceDistribution(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        CancellationToken ct = default)
    {
        var key = $"analytics:hr:performance-distribution:{from}:{to}";
        if (!_cache.TryGetValue(key, out object? result))
        {
            result = await _service.GetPerformanceDistributionAsync(from, to, ct);
            _cache.Set(key, result, TimeSpan.FromMinutes(15));
        }
        return Ok(result);
    }
}
