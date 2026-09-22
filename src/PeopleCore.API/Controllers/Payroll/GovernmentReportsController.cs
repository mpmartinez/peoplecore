using Microsoft.AspNetCore.Mvc;
using PeopleCore.API.Authorization;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Application.Payroll.GovernmentReports;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.API.Controllers.Payroll;

/// <summary>
/// The government remittance reports: the monthly SSS/PhilHealth/Pag-IBIG/1601-C reports, and the
/// annual BIR 1604-C alphalist. An unknown report name is a 404 and a bad or future month/year a
/// 400, both raised by the service and mapped by the exception middleware.
/// </summary>
[ApiController]
[Route("api/reports/government")]
[RequirePermission(Permissions.PayrollManage)]
public class GovernmentReportsController : ControllerBase
{
    private readonly IGovernmentReportService _service;

    public GovernmentReportsController(IGovernmentReportService service) => _service = service;

    [HttpGet("{report}")]
    public async Task<IActionResult> Get(string report, [FromQuery] int year, [FromQuery] int? month,
        [FromQuery] string? format, CancellationToken ct)
    {
        // The alphalist is annual; every other report is for one month.
        var dto = string.Equals(report, "1604c", StringComparison.OrdinalIgnoreCase)
            ? await _service.BuildAnnualAsync(report, year, ct)
            : month is { } m
                ? await _service.BuildAsync(report, year, m, ct)
                : throw new DomainException("Choose a month.");
        return string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)
            ? File(GovernmentReportCsv.Write(dto), GovernmentReportCsv.ContentType, GovernmentReportCsv.FileName(dto))
            : Ok(dto);
    }
}
