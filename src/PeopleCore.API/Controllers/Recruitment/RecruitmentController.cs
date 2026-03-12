using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Recruitment.DTOs;
using PeopleCore.Application.Recruitment.Interfaces;

namespace PeopleCore.API.Controllers.Recruitment;

[ApiController]
[Route("api")]
[Authorize]
public class RecruitmentController : ControllerBase
{
    private readonly IJobPostingService _jobService;
    private readonly IApplicantService _applicantService;
    private readonly IInterviewService _interviewService;

    public RecruitmentController(
        IJobPostingService jobService,
        IApplicantService applicantService,
        IInterviewService interviewService)
    {
        _jobService = jobService;
        _applicantService = applicantService;
        _interviewService = interviewService;
    }

    // --- Job Postings ---

    [HttpGet("job-postings")]
    public async Task<IActionResult> GetPostings(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(await _jobService.GetAllAsync(status, page, pageSize, ct));

    [HttpGet("job-postings/{id:guid}")]
    public async Task<IActionResult> GetPosting(Guid id, CancellationToken ct)
        => Ok(await _jobService.GetByIdAsync(id, ct));

    [HttpPost("job-postings")]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> CreatePosting([FromBody] CreateJobPostingDto dto, CancellationToken ct)
    {
        var result = await _jobService.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetPosting), new { id = result.Id }, result);
    }

    [HttpPut("job-postings/{id:guid}")]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> UpdatePosting(Guid id, [FromBody] UpdateJobPostingDto dto, CancellationToken ct)
        => Ok(await _jobService.UpdateAsync(id, dto, ct));

    [HttpPut("job-postings/{id:guid}/publish")]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> PublishPosting(Guid id, CancellationToken ct)
        => Ok(await _jobService.PublishAsync(id, ct));

    [HttpPut("job-postings/{id:guid}/close")]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> ClosePosting(Guid id, CancellationToken ct)
        => Ok(await _jobService.CloseAsync(id, ct));

    // --- Applicants ---

    [HttpGet("applicants")]
    public async Task<IActionResult> GetApplicants(
        [FromQuery] Guid? jobPostingId,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(await _applicantService.GetAllAsync(jobPostingId, status, page, pageSize, ct));

    [HttpGet("applicants/{id:guid}")]
    public async Task<IActionResult> GetApplicant(Guid id, CancellationToken ct)
        => Ok(await _applicantService.GetByIdAsync(id, ct));

    [HttpPost("applicants")]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> CreateApplicant([FromBody] CreateApplicantDto dto, CancellationToken ct)
    {
        var result = await _applicantService.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetApplicant), new { id = result.Id }, result);
    }

    [HttpPut("applicants/{id:guid}/status")]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> UpdateApplicantStatus(Guid id, [FromBody] UpdateApplicantStatusDto dto, CancellationToken ct)
        => Ok(await _applicantService.UpdateStatusAsync(id, dto, ct));

    [HttpPost("applicants/{id:guid}/convert-to-employee")]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> ConvertToEmployee(Guid id, [FromBody] ConvertToEmployeeDto dto, CancellationToken ct)
        => Ok(await _applicantService.ConvertToEmployeeAsync(id, dto, ct));

    // --- Interviews ---

    [HttpGet("applicants/{applicantId:guid}/interviews")]
    public async Task<IActionResult> GetInterviews(Guid applicantId, CancellationToken ct)
        => Ok(await _interviewService.GetByApplicantAsync(applicantId, ct));

    [HttpGet("interviews/{id:guid}")]
    public async Task<IActionResult> GetInterview(Guid id, CancellationToken ct)
        => Ok(await _interviewService.GetByIdAsync(id, ct));

    [HttpPost("interviews")]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> CreateInterview([FromBody] CreateInterviewStageDto dto, CancellationToken ct)
    {
        var result = await _interviewService.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetInterview), new { id = result.Id }, result);
    }

    [HttpPut("interviews/{id:guid}")]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> UpdateInterview(Guid id, [FromBody] UpdateInterviewStageDto dto, CancellationToken ct)
        => Ok(await _interviewService.UpdateAsync(id, dto, ct));

    [HttpDelete("interviews/{id:guid}")]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> DeleteInterview(Guid id, CancellationToken ct)
    {
        await _interviewService.DeleteAsync(id, ct);
        return NoContent();
    }
}
