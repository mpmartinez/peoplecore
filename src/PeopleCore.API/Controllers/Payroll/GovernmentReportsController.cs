using Microsoft.AspNetCore.Mvc;
using PeopleCore.API.Authorization;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Application.Payroll.GovernmentReports;

namespace PeopleCore.API.Controllers.Payroll;

/// <summary>
/// The monthly government remittance reports. An unknown report name is a 404 and a bad or future
/// month a 400, both raised by the service and mapped by the exception middleware.
/// </summary>
[ApiController]
[Route("api/reports/government")]
[RequirePermission(Permissions.PayrollManage)]
public class GovernmentReportsController : ControllerBase
{
    private readonly IGovernmentReportService _service;

    public GovernmentReportsController(IGovernmentReportService service) => _service = service;

    [HttpGet("{report}")]
    public async Task<IActionResult> Get(string report, [FromQuery] int year, [FromQuery] int month,
        [FromQuery] string? format, CancellationToken ct)
    {
        var dto = await _service.BuildAsync(report, year, month, ct);
        return string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)
            ? File(GovernmentReportCsv.Write(dto), GovernmentReportCsv.ContentType, GovernmentReportCsv.FileName(dto))
            : Ok(dto);
    }
}
