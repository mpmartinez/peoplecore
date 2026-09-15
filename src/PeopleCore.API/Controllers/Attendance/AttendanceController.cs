using CsvHelper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.API.Authorization;
using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Employees.Interfaces;

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

    [HttpPost("import")]
    [RequirePermission(Permissions.AttendanceManage)]
    public async Task<IActionResult> Import(IFormFile file, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
            return BadRequest("No file provided.");

        using var stream = file.OpenReadStream();
        var punches = ParseCsv(stream);
        return Ok(await _service.SyncPunchesAsync(punches, ct));
    }

    private static List<AttendancePunchDto> ParseCsv(Stream stream)
    {
        // CSV format: employee_number,date,time_in,time_out
        // Example row: EMP-001,2026-03-10,08:02,17:05
        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, System.Globalization.CultureInfo.InvariantCulture);
        var punches = new List<AttendancePunchDto>();
        csv.Read();
        csv.ReadHeader();
        while (csv.Read())
        {
            var empNum = csv.GetField("employee_number");
            var date = csv.GetField("date");
            var timeIn = csv.GetField("time_in");
            var timeOut = csv.GetField("time_out");

            if (string.IsNullOrWhiteSpace(empNum) || string.IsNullOrWhiteSpace(date))
                continue;

            if (!string.IsNullOrWhiteSpace(timeIn))
                punches.Add(new AttendancePunchDto(empNum, DateTime.Parse($"{date} {timeIn}")));
            if (!string.IsNullOrWhiteSpace(timeOut))
                punches.Add(new AttendancePunchDto(empNum, DateTime.Parse($"{date} {timeOut}")));
        }
        return punches;
    }
}
