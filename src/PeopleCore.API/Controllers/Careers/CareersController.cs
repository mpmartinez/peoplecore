using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Careers.DTOs;
using PeopleCore.Application.Careers.Interfaces;

namespace PeopleCore.API.Controllers.Careers;

[ApiController]
[Route("api/careers")]
[EnableCors("CareersPortal")]
public class CareersController : ControllerBase
{
    private readonly ICareersService _service;

    public CareersController(ICareersService service) => _service = service;

    [HttpGet("jobs")]
    public async Task<IActionResult> GetJobs(CancellationToken ct = default)
        => Ok(await _service.GetOpenJobsAsync(ct));

    [HttpGet("jobs/{id:guid}")]
    public async Task<IActionResult> GetJob(Guid id, CancellationToken ct = default)
    {
        var job = await _service.GetJobAsync(id, ct);
        return job is null ? NotFound() : Ok(job);
    }

    [HttpPost("jobs/{id:guid}/apply")]
    [EnableRateLimiting(RateLimitPolicies.CareersApply)]
    public async Task<IActionResult> Apply(Guid id, [FromBody] JobApplicationRequest request, CancellationToken ct = default)
    {
        var result = await _service.ApplyAsync(id, request, ct);
        return CreatedAtAction(nameof(GetJob), new { id }, result);
    }
}
