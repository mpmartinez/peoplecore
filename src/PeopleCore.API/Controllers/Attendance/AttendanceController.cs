using CsvHelper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Attendance.Interfaces;

namespace PeopleCore.API.Controllers.Attendance;

[ApiController]
[Route("api/attendance")]
[Authorize]
public class AttendanceController : ControllerBase
{
    private readonly IAttendanceService _service;
    public AttendanceController(IAttendanceService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? employeeId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(await _service.GetAllAsync(employeeId, from, to, page, pageSize, ct));

    [HttpPost("time-in")]
    public async Task<IActionResult> TimeIn([FromBody] TimeInRequest request, CancellationToken ct = default)
        => StatusCode(201, await _service.TimeInAsync(request, ct));

    [HttpPost("time-out")]
    public async Task<IActionResult> TimeOut([FromBody] TimeOutRequest request, CancellationToken ct = default)
        => StatusCode(201, await _service.TimeOutAsync(request, ct));

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(
        [FromQuery] Guid employeeId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken ct = default)
        => Ok(await _service.GetSummaryAsync(employeeId, from, to, ct));

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
