using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Analytics.Interfaces;

namespace PeopleCore.API.Controllers.Analytics;

[ApiController]
[Route("api/analytics/hr")]
[Authorize(Roles = "Admin,HRManager")]
public class HRAnalyticsController : ControllerBase
{
    private readonly IHRAnalyticsService _service;

    public HRAnalyticsController(IHRAnalyticsService service)
    {
        _service = service;
    }

    [HttpGet("headcount")]
    [ResponseCache(Duration = 900)]
    public async Task<IActionResult> GetHeadcount(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] Guid? departmentId = null, CancellationToken ct = default)
        => Ok(await _service.GetHeadcountAsync(from, to, departmentId, ct));

    [HttpGet("turnover")]
    [ResponseCache(Duration = 900)]
    public async Task<IActionResult> GetTurnover(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] string groupBy = "month", CancellationToken ct = default)
        => Ok(await _service.GetTurnoverAsync(from, to, groupBy, ct));

    [HttpGet("attendance")]
    [ResponseCache(Duration = 900)]
    public async Task<IActionResult> GetAttendance(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] Guid? departmentId = null, CancellationToken ct = default)
        => Ok(await _service.GetAttendanceRateAsync(from, to, departmentId, ct));

    [HttpGet("leave-utilization")]
    [ResponseCache(Duration = 900)]
    public async Task<IActionResult> GetLeaveUtilization(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] Guid? departmentId = null, CancellationToken ct = default)
        => Ok(await _service.GetLeaveUtilizationAsync(from, to, departmentId, ct));

    [HttpGet("overtime")]
    [ResponseCache(Duration = 900)]
    public async Task<IActionResult> GetOvertime(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] Guid? departmentId = null, CancellationToken ct = default)
        => Ok(await _service.GetOvertimeAsync(from, to, departmentId, ct));

    [HttpGet("recruitment-funnel")]
    [ResponseCache(Duration = 900)]
    public async Task<IActionResult> GetRecruitmentFunnel(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        CancellationToken ct = default)
        => Ok(await _service.GetRecruitmentFunnelAsync(from, to, ct));

    [HttpGet("performance-distribution")]
    [ResponseCache(Duration = 900)]
    public async Task<IActionResult> GetPerformanceDistribution(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        CancellationToken ct = default)
        => Ok(await _service.GetPerformanceDistributionAsync(from, to, ct));
}
