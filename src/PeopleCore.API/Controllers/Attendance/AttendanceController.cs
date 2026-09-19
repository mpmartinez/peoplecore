using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.API.Authorization;
using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Infrastructure.Attendance;

namespace PeopleCore.API.Controllers.Attendance;

/// <summary>
/// Time-in, time-out and attendance history. The class-level [Authorize] only says the caller is
/// signed in; every employee id below - query string or body - is caller-supplied, so each
/// employee-scoped action carries its own ownership check through <see cref="IEmployeeAccessService"/>:
/// HR staff read everyone's attendance, a Manager their direct reports', everybody their own.
/// Clocking in and out is self-service for everyone: corrections go through <see cref="Sync"/> and
/// <see cref="Import"/>.
/// </summary>
[ApiController]
[Route("api/attendance")]
[Authorize]
public class AttendanceController : ControllerBase
{
    private readonly IAttendanceService _service;
    private readonly ICurrentUserService _currentUser;
    private readonly IEmployeeAccessService _access;

    public AttendanceController(IAttendanceService service, ICurrentUserService currentUser, IEmployeeAccessService access)
    {
        _service = service;
        _currentUser = currentUser;
        _access = access;
    }

    /// <summary>
    /// Naming an employee needs access to that employee. With no <paramref name="employeeId"/> HR
    /// staff get everyone's attendance, a Manager their direct reports', and anyone else is refused.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? employeeId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (employeeId is { } id)
        {
            if (!await _access.CanViewAsync(id, ct))
                return Forbid();

            return Ok(await _service.GetAllAsync(id, null, from, to, page, pageSize, ct));
        }

        var scope = _access.GetUnfilteredListScope();
        if (!scope.IsAllowed)
            return Forbid();

        return Ok(await _service.GetAllAsync(null, scope.ReportingManagerId, from, to, page, pageSize, ct));
    }

    /// <summary>
    /// Self-service only. The body still carries an employee id (the service needs one), but it
    /// must be the caller's own: a mismatch is refused rather than silently rewritten, so a client
    /// clocking the wrong person finds out.
    /// </summary>
    [HttpPost("time-in")]
    public async Task<IActionResult> TimeIn([FromBody] TimeInRequest request, CancellationToken ct = default)
    {
        var employeeId = _currentUser.EmployeeId;
        if (employeeId is null || request.EmployeeId != employeeId)
            return Forbid();

        return StatusCode(201, await _service.TimeInAsync(request, ct));
    }

    /// <summary>Self-service only, as <see cref="TimeIn"/>.</summary>
    [HttpPost("time-out")]
    public async Task<IActionResult> TimeOut([FromBody] TimeOutRequest request, CancellationToken ct = default)
    {
        var employeeId = _currentUser.EmployeeId;
        if (employeeId is null || request.EmployeeId != employeeId)
            return Forbid();

        return StatusCode(201, await _service.TimeOutAsync(request, ct));
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(
        [FromQuery] Guid employeeId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken ct = default)
    {
        if (!await _access.CanViewAsync(employeeId, ct))
            return Forbid();

        return Ok(await _service.GetSummaryAsync(employeeId, from, to, ct));
    }

    [HttpPost("sync")]
    [RequirePermission(Permissions.AttendanceDeviceSync)]
    public async Task<IActionResult> Sync(
        [FromBody] IReadOnlyList<AttendancePunchDto> punches, CancellationToken ct = default)
        => Ok(await _service.SyncPunchesAsync(punches, ct));

    /// <summary>
    /// Imports a time clock export or PeopleCore's own CSV (see <see cref="AttendanceFile"/> for what
    /// is read). With <paramref name="preview"/> nothing is written: the answer says what the file
    /// holds and which of its people match no employee yet. Otherwise always 200 with the counts: a
    /// row the parser refuses, or a punch for nobody, is reported in <c>errors</c> and counted in
    /// <c>skipped</c>.
    /// </summary>
    [HttpPost("import")]
    [RequirePermission(Permissions.AttendanceManage)]
    [RequestSizeLimit(MaxImportBytes)]
    public async Task<IActionResult> Import(
        IFormFile file, [FromServices] IAttendanceImportService import,
        [FromQuery] bool preview = false, [FromQuery] bool replace = false, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
            return BadRequest("No file provided.");

        AttendanceFileParseResult parsed;
        using (var stream = file.OpenReadStream())
            parsed = AttendanceFile.Parse(stream, file.FileName);

        if (preview)
        {
            var layout = parsed.Layout switch
            {
                AttendanceFileLayout.DailyInOut => "One row per person per day",
                AttendanceFileLayout.ScanLog => "Time clock scans",
                _ => "Not recognised",
            };
            return Ok(await import.PreviewAsync(layout, parsed.Punches, parsed.Errors, ct));
        }

        // With replace, a day already recorded takes the file's times as a logged correction; a day
        // inside a paid payroll run is still refused and reported.
        var options = new AttendanceImportOptions(replace, file.FileName, _currentUser.Email ?? "unknown");
        return Ok(await import.ImportAsync(parsed.Punches, parsed.Errors, options, ct));
    }

    /// <summary>A blank of PeopleCore's own import layout with example rows, as <c>csv</c> or <c>xlsx</c>.</summary>
    [HttpGet("import/template")]
    [RequirePermission(Permissions.AttendanceManage)]
    public IActionResult ImportTemplate([FromQuery] string format = "csv") => format.ToLowerInvariant() switch
    {
        "csv" => File(AttendanceImportTemplate.Csv(), AttendanceImportTemplate.CsvContentType, "attendance-import-template.csv"),
        "xlsx" => File(AttendanceImportTemplate.Xlsx(), AttendanceImportTemplate.XlsxContentType, "attendance-import-template.xlsx"),
        _ => BadRequest("Ask for format=csv or format=xlsx."),
    };

    /// <summary>Links an employee to the number they are enrolled under on the time clock; a blank id unlinks them.</summary>
    [HttpPut("biometric-ids/{employeeId:guid}")]
    [RequirePermission(Permissions.AttendanceManage)]
    public async Task<IActionResult> SetBiometricId(
        Guid employeeId, [FromBody] SetBiometricIdDto body, [FromServices] IAttendanceImportService import, CancellationToken ct = default)
        => Ok(await import.SetBiometricIdAsync(employeeId, body.BiometricId, ct));

    private const long MaxImportBytes = 10 * 1024 * 1024;
}
