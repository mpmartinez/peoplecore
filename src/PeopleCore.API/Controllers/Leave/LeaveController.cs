using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.API.Authorization;
using PeopleCore.API.Filters;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Leave.DTOs;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Leave.Services;

namespace PeopleCore.API.Controllers.Leave;

/// <summary>
/// Leave types, requests and balances. The class-level [Authorize] only says the caller is signed
/// in; every employee id below - route, query string or body - is caller-supplied, so each
/// employee-scoped action carries its own ownership check through <see cref="IEmployeeAccessService"/>:
/// HR staff reach everyone's leave, a Manager their direct reports', everybody their own. Filing and
/// cancelling leave is self-service for everyone: the employee is the one in the caller's
/// employee_id claim, never one the caller names.
/// <para>
/// Confidential leave (VAWC) is kept from managers: it is left out of their approval queue, only
/// <c>approvals.all</c> may decide it, anyone else who sees it gets it masked (<see cref="Mask(LeaveRequestDto)"/>),
/// and its balance rows are left out.
/// </para>
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
public class LeaveController : ControllerBase
{
    private readonly ILeaveTypeService _typeService;
    private readonly ILeaveRequestService _requestService;
    private readonly ILeaveBalanceService _balanceService;
    private readonly ILeaveDocumentService _documentService;
    private readonly ICurrentUserService _currentUser;
    private readonly IEmployeeAccessService _access;

    public LeaveController(
        ILeaveTypeService typeService,
        ILeaveRequestService requestService,
        ILeaveBalanceService balanceService,
        ILeaveDocumentService documentService,
        ICurrentUserService currentUser,
        IEmployeeAccessService access)
    {
        _typeService = typeService;
        _requestService = requestService;
        _balanceService = balanceService;
        _documentService = documentService;
        _currentUser = currentUser;
        _access = access;
    }

    // Leave Types
    [HttpGet("leave-types")]
    public async Task<IActionResult> GetLeaveTypes(CancellationToken ct = default)
        => Ok(await _typeService.GetAllAsync(ct));

    [HttpPost("leave-types")]
    [RequirePermission(Permissions.LeaveManage)]
    public async Task<IActionResult> CreateLeaveType([FromBody] CreateLeaveTypeDto dto, CancellationToken ct = default)
        => StatusCode(201, await _typeService.CreateAsync(dto, ct));

    [HttpPut("leave-types/{id:guid}")]
    [RequirePermission(Permissions.LeaveManage)]
    public async Task<IActionResult> UpdateLeaveType(Guid id, [FromBody] CreateLeaveTypeDto dto, CancellationToken ct = default)
        => Ok(await _typeService.UpdateAsync(id, dto, ct));

    [HttpDelete("leave-types/{id:guid}")]
    [RequirePermission(Permissions.LeaveManage)]
    public async Task<IActionResult> DeleteLeaveType(Guid id, CancellationToken ct = default)
    {
        await _typeService.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>
    /// Adds the Philippine statutory leave types the site lacks, matched by code. Idempotent:
    /// returns <see cref="StatutoryLeaveResultDto"/> naming the codes added and the codes skipped.
    /// </summary>
    [HttpPost("leave-types/statutory")]
    [RequirePermission(Permissions.LeaveManage)]
    public async Task<IActionResult> AddStatutoryLeaveTypes(CancellationToken ct = default)
        => Ok(await _typeService.AddStatutoryAsync(ct));

    // Leave Requests
    /// <summary>
    /// Leave requests, reasons included. Naming an employee needs access to that employee. With no
    /// <paramref name="employeeId"/> HR staff get everyone's, a Manager gets their direct reports'
    /// (the approval queue), and anyone else is refused. The unfiltered list leaves confidential
    /// leave out unless the caller holds <c>approvals.all</c>; a named employee's list keeps it,
    /// masked.
    /// </summary>
    [HttpGet("leave-requests")]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? employeeId, [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (employeeId is { } id)
        {
            if (!await _access.CanViewAsync(id, ct))
                return Forbid();

            return Ok(Mask(await _requestService.GetAllAsync(
                id, null, status, page, pageSize, excludeConfidential: false, ct)));
        }

        var scope = _access.GetUnfilteredListScope();
        if (!scope.IsAllowed)
            return Forbid();

        // In the query itself, so the count and pages match.
        var excludeConfidential = !_currentUser.HasPermission(Permissions.ApprovalsAll);
        return Ok(Mask(await _requestService.GetAllAsync(
            null, scope.ReportingManagerId, status, page, pageSize, excludeConfidential, ct)));
    }

    /// <summary>
    /// What the caller may file today, with what is left of each type. Self-service: the employee is
    /// the one in the caller's employee_id claim, and an account with none is refused. The literal
    /// segment cannot be taken for a request id - every <c>leave-requests/{id}</c> route is <c>:guid</c>.
    /// </summary>
    [HttpGet("leave-requests/options")]
    public async Task<IActionResult> GetFilingOptions(CancellationToken ct = default)
    {
        var employeeId = _currentUser.EmployeeId;
        if (employeeId is null)
            return Forbid();

        return Ok(await _requestService.GetFilingOptionsAsync(employeeId.Value, ct));
    }

    /// <summary>
    /// The owner is only known once the request is loaded, so the check follows the read. A
    /// refused caller gets 403 rather than the request.
    /// </summary>
    [HttpGet("leave-requests/{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default)
    {
        var request = await _requestService.GetByIdAsync(id, ct);
        if (!await _access.CanViewAsync(request.EmployeeId, ct))
            return Forbid();

        return Ok(Mask(request));
    }

    /// <summary>
    /// Self-service only. The body still carries an employee id (the service needs one), but it
    /// must be the caller's own: a mismatch is refused rather than silently rewritten, so a client
    /// filing for the wrong person finds out. HR and managers are no exception - seeing someone's
    /// leave is not a licence to spend their balance.
    /// </summary>
    [HttpPost("leave-requests")]
    public async Task<IActionResult> Create([FromBody] CreateLeaveRequestDto dto, CancellationToken ct = default)
    {
        var employeeId = _currentUser.EmployeeId;
        if (employeeId is null || dto.EmployeeId != employeeId)
            return Forbid();

        return StatusCode(201, await _requestService.CreateAsync(dto, ct));
    }

    /// <summary>
    /// Approves as the employee in the caller's employee_id claim; an account with no employee
    /// record has nobody to record and is refused. HR staff approve anyone's leave, a Manager only
    /// a direct report's, and confidential leave only <c>approvals.all</c>. The caller's own
    /// request is left to the service, which refuses it with its reason.
    /// </summary>
    [HttpPut("leave-requests/{id:guid}/approve")]
    [RequirePermission(Permissions.ApprovalsTeam, Permissions.ApprovalsAll)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct = default)
    {
        var approverId = _currentUser.EmployeeId;
        if (approverId is null || !await MayDecideAsync(id, approverId.Value, ct))
            return Forbid();

        return Ok(await _requestService.ApproveAsync(id, approverId.Value, ct));
    }

    /// <summary>Held to the same rules as <see cref="Approve"/>.</summary>
    [HttpPut("leave-requests/{id:guid}/reject")]
    [RequirePermission(Permissions.ApprovalsTeam, Permissions.ApprovalsAll)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectLeaveDto dto, CancellationToken ct = default)
    {
        var rejecterId = _currentUser.EmployeeId;
        if (rejecterId is null || !await MayDecideAsync(id, rejecterId.Value, ct))
            return Forbid();

        return Ok(await _requestService.RejectAsync(id, rejecterId.Value, dto, ct));
    }

    /// <summary>
    /// True when the decider may manage the request's employee, or the request is the decider's own -
    /// passed through so the service can refuse that with "You cannot approve your own leave request"
    /// instead of a bare 403. Confidential leave needs <c>approvals.all</c>, whoever it belongs to.
    /// </summary>
    private async Task<bool> MayDecideAsync(Guid requestId, Guid deciderId, CancellationToken ct)
    {
        var request = await _requestService.GetByIdAsync(requestId, ct);
        if (request.IsConfidential && !_currentUser.HasPermission(Permissions.ApprovalsAll))
            return false;

        return request.EmployeeId == deciderId || await _access.CanManageAsync(request.EmployeeId, ct);
    }

    /// <summary>
    /// Takes no employee id. It used to read one from the query string and hand it to the
    /// service's "only your own" check, which then compared the request against whoever the caller
    /// claimed to be. The canceller is now the employee in the caller's employee_id claim.
    /// </summary>
    [HttpPut("leave-requests/{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct = default)
    {
        var employeeId = _currentUser.EmployeeId;
        if (employeeId is null)
            return Forbid();

        await _requestService.CancelAsync(id, employeeId.Value, ct);
        return NoContent();
    }

    /// <summary>
    /// The upload's body cap: the 10 MB file plus room for the multipart framing. Anything larger is
    /// never read past this; a file between 10 MB and the cap reaches the service's own check.
    /// </summary>
    private const long MaxDocumentRequestBytes = LeaveDocumentService.MaxSizeBytes + 512 * 1024;

    /// <summary>
    /// Attaches or replaces the request's supporting document (multipart field <c>file</c>).
    /// Self-service: the uploader is the employee in the caller's employee_id claim, and the service
    /// refuses anyone but the request's owner, and any request that is no longer Pending.
    /// </summary>
    [HttpPut("leave-requests/{id:guid}/document")]
    [RequestSizeLimit(MaxDocumentRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxDocumentRequestBytes)]
    [RefuseOversizedForm(LeaveDocumentService.FileRefusal)]
    public async Task<IActionResult> UploadDocument(Guid id, [FromForm] IFormFile file, CancellationToken ct = default)
    {
        var employeeId = _currentUser.EmployeeId;
        if (employeeId is null)
            return Forbid();

        using var stream = file.OpenReadStream();
        var result = await _documentService.UploadAsync(
            id, employeeId.Value, stream, file.FileName, file.ContentType, file.Length, ct);
        return Ok(result);
    }

    /// <summary>
    /// Hands back a presigned storage URL, so who may open it is decided HERE - once issued, the URL
    /// is a bearer token for the file that carries no identity of its own.
    /// </summary>
    [HttpGet("leave-requests/{id:guid}/document")]
    public async Task<IActionResult> GetDocument(Guid id, CancellationToken ct = default)
    {
        var request = await _requestService.GetByIdAsync(id, ct);
        if (!await MayOpenDocumentAsync(request, ct))
            return Forbid();

        var url = await _documentService.GetDownloadUrlAsync(id, ct);
        return Ok(new { url });
    }

    /// <summary>The owner, <c>approvals.all</c>, or a team approver who manages the employee - unless the type is confidential.</summary>
    private async Task<bool> MayOpenDocumentAsync(LeaveRequestDto request, CancellationToken ct)
    {
        if (_currentUser.EmployeeId is { } callerId && callerId == request.EmployeeId)
            return true;

        if (_currentUser.HasPermission(Permissions.ApprovalsAll))
            return true;

        return !request.IsConfidential
               && _currentUser.HasPermission(Permissions.ApprovalsTeam)
               && await _access.CanManageAsync(request.EmployeeId, ct);
    }

    // Leave Balances
    /// <summary>Confidential types' rows are left out unless the caller is the employee or holds <c>approvals.all</c>.</summary>
    [HttpGet("leave-balances/{employeeId:guid}")]
    public async Task<IActionResult> GetBalances(Guid employeeId, [FromQuery] int? year, CancellationToken ct = default)
    {
        if (!await _access.CanViewAsync(employeeId, ct))
            return Forbid();

        var balances = await _balanceService.GetByEmployeeAsync(employeeId, year, ct);
        if (SeesConfidentialOf(employeeId))
            return Ok(balances);

        return Ok(balances.Where(b => !b.IsConfidential).ToList());
    }

    /// <summary>Whether the caller sees an employee's confidential leave as it is: they are that employee, or hold <c>approvals.all</c>.</summary>
    private bool SeesConfidentialOf(Guid employeeId)
        => _currentUser.EmployeeId == employeeId || _currentUser.HasPermission(Permissions.ApprovalsAll);

    /// <summary>
    /// Confidential leave as shown to anyone but its employee and <c>approvals.all</c>: just "Leave"
    /// on those dates - no type, reason, rejection reason (HR may have named the type in it) or
    /// document.
    /// </summary>
    private LeaveRequestDto Mask(LeaveRequestDto dto)
        => !dto.IsConfidential || SeesConfidentialOf(dto.EmployeeId)
            ? dto
            : dto with
            {
                LeaveTypeId = Guid.Empty,
                LeaveTypeName = "Leave",
                Reason = null,
                RejectionReason = null,
                HasDocument = false,
                DocumentFileName = null,
                IsConfidential = false
            };

    private PagedResult<LeaveRequestDto> Mask(PagedResult<LeaveRequestDto> page)
        => PagedResult<LeaveRequestDto>.Create(
            page.Items.Select(Mask).ToList(), page.TotalCount, page.Page, page.PageSize);
}
