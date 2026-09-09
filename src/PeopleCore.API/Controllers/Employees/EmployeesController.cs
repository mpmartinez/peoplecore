using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Employees.DTOs;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Domain.Enums;

namespace PeopleCore.API.Controllers.Employees;

/// <summary>
/// Employee records and the personal data hanging off them - government IDs, emergency contacts,
/// documents. Every action below takes an employee id straight from the URL, so a role check on
/// its own is not a security boundary here: "Employee" is a role every authenticated user holds,
/// and it says nothing about WHICH employee record the caller is entitled to. The boundary is
/// <see cref="IsSelfOr"/> - the caller must either hold a privileged role or be the employee named
/// in the route. Without it, changing one GUID in the URL reads someone else's date of birth,
/// home address, SSS/TIN numbers or HR documents.
/// </summary>
[ApiController]
[Route("api/employees")]
[Authorize]
public class EmployeesController : ControllerBase
{
    /// <summary>HR staff who administer any employee's record.</summary>
    private const string HrRoles = "Admin,HRManager";

    /// <summary>Payroll staff additionally need the employee record behind a compensation screen.</summary>
    private const string PayrollReadRoles = "Admin,HRManager,PayrollService";

    private readonly IEmployeeService _service;
    private readonly IEmployeeDocumentService _documentService;
    private readonly ICurrentUserService _currentUser;

    public EmployeesController(
        IEmployeeService service,
        IEmployeeDocumentService documentService,
        ICurrentUserService currentUser)
    {
        _service = service;
        _documentService = documentService;
        _currentUser = currentUser;
    }

    /// <summary>
    /// True when the caller holds one of <paramref name="roles"/> (a comma-separated list, same
    /// shape as <see cref="AuthorizeAttribute.Roles"/>) or is themselves the employee named by
    /// <paramref name="employeeId"/>. A caller with no employee_id claim and no privileged role
    /// matches nothing: <see cref="ICurrentUserService.EmployeeId"/> is null there, and a null
    /// never equals the route's <see cref="Guid"/>.
    /// </summary>
    private bool IsSelfOr(string roles, Guid employeeId)
        => roles.Split(',', StringSplitOptions.TrimEntries).Any(_currentUser.IsInRole)
           || _currentUser.EmployeeId == employeeId;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] EmployeeFilterDto filter, CancellationToken ct)
        => Ok(await _service.GetAllAsync(filter, ct));

    /// <summary>
    /// A full employee record - date of birth, personal email, mobile, address. HR and payroll
    /// staff may read anyone's; everybody else may read only their own.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        if (!IsSelfOr(PayrollReadRoles, id))
            return Forbid();

        return Ok(await _service.GetByIdAsync(id, ct));
    }

    [HttpPost]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> Create([FromBody] CreateEmployeeDto dto, CancellationToken ct)
    {
        var result = await _service.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateEmployeeDto dto, CancellationToken ct)
        => Ok(await _service.UpdateAsync(id, dto, ct));

    [HttpPut("{id:guid}/deactivate")]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> Deactivate(Guid id, [FromBody] DeactivateEmployeeRequest request, CancellationToken ct)
    {
        await _service.DeactivateAsync(id, request.SeparationDate, ct);
        return NoContent();
    }

    /// <summary>SSS/TIN/PhilHealth/Pag-IBIG numbers. HR may read anyone's; everybody else only their own.</summary>
    [HttpGet("{id:guid}/government-ids")]
    public async Task<IActionResult> GetGovernmentIds(Guid id, CancellationToken ct)
    {
        if (!IsSelfOr(HrRoles, id))
            return Forbid();

        return Ok(await _service.GetGovernmentIdsAsync(id, ct));
    }

    /// <summary>
    /// Deliberately a bare [Authorize] rather than a role list containing "Employee". The old
    /// "Admin,HRManager,Employee" list let anyone holding the Employee role overwrite ANY
    /// employee's government IDs, while simultaneously locking a Manager out of editing their
    /// own. The ownership check below is the real boundary; the role list was never one.
    /// </summary>
    [HttpPut("{id:guid}/government-ids")]
    public async Task<IActionResult> UpsertGovernmentId(Guid id, [FromBody] UpsertGovernmentIdDto dto, CancellationToken ct)
    {
        if (!IsSelfOr(HrRoles, id))
            return Forbid();

        await _service.UpsertGovernmentIdAsync(id, dto, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/emergency-contacts")]
    public async Task<IActionResult> GetEmergencyContacts(Guid id, CancellationToken ct)
    {
        if (!IsSelfOr(HrRoles, id))
            return Forbid();

        return Ok(await _service.GetEmergencyContactsAsync(id, ct));
    }

    [HttpPost("{id:guid}/emergency-contacts")]
    public async Task<IActionResult> AddEmergencyContact(Guid id, [FromBody] CreateEmergencyContactDto dto, CancellationToken ct)
    {
        if (!IsSelfOr(HrRoles, id))
            return Forbid();

        var result = await _service.AddEmergencyContactAsync(id, dto, ct);
        return CreatedAtAction(nameof(GetEmergencyContacts), new { id }, result);
    }

    [HttpDelete("{id:guid}/emergency-contacts/{contactId:guid}")]
    public async Task<IActionResult> DeleteEmergencyContact(Guid id, Guid contactId, CancellationToken ct)
    {
        if (!IsSelfOr(HrRoles, id))
            return Forbid();

        await _service.DeleteEmergencyContactAsync(id, contactId, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/documents")]
    public async Task<IActionResult> GetDocuments(Guid id, CancellationToken ct)
    {
        if (!IsSelfOr(HrRoles, id))
            return Forbid();

        return Ok(await _documentService.GetDocumentsAsync(id, ct));
    }

    [HttpPost("{id:guid}/documents")]
    public async Task<IActionResult> UploadDocument(Guid id, [FromForm] IFormFile file, [FromQuery] DocumentType documentType, CancellationToken ct)
    {
        if (!IsSelfOr(HrRoles, id))
            return Forbid();

        using var stream = file.OpenReadStream();
        var result = await _documentService.UploadDocumentAsync(id, documentType, file.FileName, stream, file.ContentType, ct);
        return Ok(result);
    }

    /// <summary>
    /// Hands back a presigned storage URL, so the ownership check has to happen HERE - once the
    /// URL is issued it is a bearer token for the file that carries no identity of its own.
    /// </summary>
    [HttpGet("{id:guid}/documents/{documentId:guid}/download")]
    public async Task<IActionResult> GetDownloadUrl(Guid id, Guid documentId, CancellationToken ct)
    {
        if (!IsSelfOr(HrRoles, id))
            return Forbid();

        var url = await _documentService.GetDownloadUrlAsync(id, documentId, ct);
        return Ok(new { url });
    }

    [HttpDelete("{id:guid}/documents/{documentId:guid}")]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> DeleteDocument(Guid id, Guid documentId, CancellationToken ct)
    {
        await _documentService.DeleteDocumentAsync(id, documentId, ct);
        return NoContent();
    }
}

public record DeactivateEmployeeRequest(DateOnly SeparationDate);
