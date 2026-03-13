using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Performance.DTOs;
using PeopleCore.Application.Performance.Interfaces;

namespace PeopleCore.API.Controllers.Performance;

[ApiController]
[Route("api")]
[Authorize]
public class PerformanceController : ControllerBase
{
    private readonly IReviewCycleService _cycleService;
    private readonly IPerformanceReviewService _reviewService;
    private readonly ICurrentUserService _currentUser;

    public PerformanceController(
        IReviewCycleService cycleService,
        IPerformanceReviewService reviewService,
        ICurrentUserService currentUser)
    {
        _cycleService = cycleService;
        _reviewService = reviewService;
        _currentUser = currentUser;
    }

    [HttpGet("review-cycles")]
    public async Task<IActionResult> GetCycles([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await _cycleService.GetAllAsync(page, pageSize, ct));

    [HttpGet("review-cycles/{id:guid}")]
    public async Task<IActionResult> GetCycle(Guid id, CancellationToken ct)
        => Ok(await _cycleService.GetByIdAsync(id, ct));

    [HttpPost("review-cycles")]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> CreateCycle([FromBody] CreateReviewCycleDto dto, CancellationToken ct)
    {
        var result = await _cycleService.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetCycle), new { id = result.Id }, result);
    }

    [HttpPut("review-cycles/{id:guid}/close")]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> CloseCycle(Guid id, CancellationToken ct)
        => Ok(await _cycleService.CloseAsync(id, ct));

    [HttpGet("performance-reviews")]
    public async Task<IActionResult> GetReviews(
        [FromQuery] Guid? employeeId, [FromQuery] Guid? cycleId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await _reviewService.GetAllAsync(employeeId, cycleId, page, pageSize, ct));

    [HttpGet("performance-reviews/{id:guid}")]
    public async Task<IActionResult> GetReview(Guid id, CancellationToken ct)
        => Ok(await _reviewService.GetByIdAsync(id, ct));

    [HttpPost("performance-reviews")]
    [Authorize(Roles = "Admin,HRManager,Manager")]
    public async Task<IActionResult> CreateReview([FromBody] CreatePerformanceReviewDto dto, CancellationToken ct)
    {
        var result = await _reviewService.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetReview), new { id = result.Id }, result);
    }

    [HttpPost("performance-reviews/{id:guid}/self-evaluation")]
    public async Task<IActionResult> SubmitSelfEvaluation(Guid id, [FromBody] SubmitSelfEvaluationDto dto, CancellationToken ct)
    {
        var employeeId = _currentUser.EmployeeId;
        if (employeeId is null)
            return Unauthorized("Employee ID not found in token.");
        return Ok(await _reviewService.SubmitSelfEvaluationAsync(id, employeeId.Value, dto, ct));
    }

    [HttpPost("performance-reviews/{id:guid}/manager-review")]
    [Authorize(Roles = "Admin,HRManager,Manager")]
    public async Task<IActionResult> SubmitManagerReview(Guid id, [FromBody] SubmitManagerReviewDto dto, CancellationToken ct)
    {
        var reviewerId = _currentUser.EmployeeId;
        if (reviewerId is null)
            return Unauthorized("Employee ID not found in token.");
        return Ok(await _reviewService.SubmitManagerReviewAsync(id, reviewerId.Value, dto, ct));
    }
}
