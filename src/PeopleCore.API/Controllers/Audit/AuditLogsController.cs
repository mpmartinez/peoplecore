using Microsoft.AspNetCore.Mvc;
using PeopleCore.API.Auditing;
using PeopleCore.API.Authorization;
using PeopleCore.Application.Auditing.DTOs;
using PeopleCore.Application.Auditing.Interfaces;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Common.Time;

namespace PeopleCore.API.Controllers.Audit;

/// <summary>
/// The audit trail, read-only. Entries carry the values a change replaced - salaries, government
/// ID numbers - so the whole controller is behind audit.view, which no seeded role but Admin holds.
/// </summary>
[ApiController]
[RequirePermission(Permissions.AuditView)]
[Route("api/audit-logs")]
public class AuditLogsController : ControllerBase
{
    private readonly IAuditLogQueryService _service;
    private readonly TimeProvider _clock;

    public AuditLogsController(IAuditLogQueryService service, TimeProvider clock)
    {
        _service = service;
        _clock = clock;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<AuditLogEntryDto>>> Search([FromQuery] AuditLogFilter filter, CancellationToken ct) =>
        Ok(await _service.SearchAsync(filter, ct));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<AuditLogDetailDto>> Get(long id, CancellationToken ct) =>
        await _service.GetAsync(id, ct) is { } detail ? Ok(detail) : NotFound();

    [HttpGet("facets")]
    public async Task<ActionResult<AuditFacetsDto>> Facets(CancellationToken ct) =>
        Ok(await _service.GetFacetsAsync(ct));

    [HttpGet("export")]
    [AuditAccess("audit-trail.exported", "Exported the audit trail", EntityType = "AuditLog")]
    public async Task<IActionResult> Export([FromQuery] AuditLogFilter filter, CancellationToken ct)
    {
        var csv = await _service.ExportCsvAsync(filter, ct);
        var today = DateOnly.FromDateTime(PhilippineTime.Now(_clock));
        return File(csv, "text/csv", $"audit-trail-{today:yyyy-MM-dd}.csv");
    }
}
