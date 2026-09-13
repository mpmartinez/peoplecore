using CsvHelper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Common.Interfaces;

namespace PeopleCore.API.Controllers.Attendance;

/// <summary>
/// Time-in, time-out and attendance history. The class-level [Authorize] only says the caller is
/// signed in; every employee id below - query string or body - is caller-supplied, so each
/// employee-scoped action carries its own ownership check. Attendance staff
/// (<see cref="AttendanceStaffRoles"/>) read anyone's attendance; everybody else reads only their
/// own. Clocking in and out is self-service for everyone, including attendance staff: corrections
/// go through <see cref="Sync"/> and <see cref="Import"/>.
/// </summary>
[ApiController]
[Route("api/attendance")]
[Authorize]
public class AttendanceController : ControllerBase
{
    /// <summary>
    /// The roles that need to see any employee's attendance. Managers are organisation-wide for
    /// now: nothing scopes them to their direct reports.
    /// </summary>
    private const string AttendanceStaffRoles = "Admin,HRManager,Manager";

    private readonly IAttendanceService _service;
    private readonly ICurrentUserService _currentUser;

    public AttendanceController(IAttendanceService service, ICurrentUserService currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    private bool IsAttendanceStaff()
        => AttendanceStaffRoles.Split(',', StringSplitOptions.TrimEntries).Any(_currentUser.IsInRole);

    /// <summary>
    /// True when the caller is attendance staff or is themselves <paramref name="employeeId"/>. A
    /// null on either side matches nothing: a caller with no employee_id claim is nobody, and a
    /// missing employee filter means "everyone" - letting null equal null would open both to all
    /// attendance.
    /// </summary>
    private bool IsSelfOrAttendanceStaff(Guid? employeeId)
        => IsAttendanceStaff()
           || (employeeId is not null && _currentUser.EmployeeId == employeeId);

    /// <summary>
    /// With no <paramref name="employeeId"/> this is every employee's attendance, so only
    /// attendance staff may leave it out; everybody else must name themselves.
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
        if (!IsSelfOrAttendanceStaff(employeeId))
            return Forbid();

        return Ok(await _service.GetAllAsync(employeeId, from, to, page, pageSize, ct));
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
        if (!IsSelfOrAttendanceStaff(employeeId))
            return Forbid();

        return Ok(await _service.GetSummaryAsync(employeeId, from, to, ct));
    }

    [HttpPost("sync")]
    [Authorize(Roles = "Admin,HRManager,Service")]
    public async Task<IActionResult> Sync(
        [FromBody] IReadOnlyList<AttendancePunchDto> punches, CancellationToken ct = default)
        => Ok(await _service.SyncPunchesAsync(punches, ct));

    [HttpPost("import")]
    [Authorize(Roles = "Admin,HRManager")]
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
