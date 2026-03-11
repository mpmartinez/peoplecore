using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Employees.DTOs;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Domain.Enums;

namespace PeopleCore.API.Controllers.Employees;

[ApiController]
[Route("api/employees")]
[Authorize]
public class EmployeesController : ControllerBase
{
    private readonly IEmployeeService _service;
    private readonly IEmployeeDocumentService _documentService;

    public EmployeesController(IEmployeeService service, IEmployeeDocumentService documentService)
        => (_service, _documentService) = (service, documentService);

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] EmployeeFilterDto filter, CancellationToken ct)
        => Ok(await _service.GetAllAsync(filter, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

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

    [HttpGet("{id:guid}/government-ids")]
    public async Task<IActionResult> GetGovernmentIds(Guid id, CancellationToken ct)
        => Ok(await _service.GetGovernmentIdsAsync(id, ct));

    [HttpPut("{id:guid}/government-ids")]
    [Authorize(Roles = "Admin,HRManager,Employee")]
    public async Task<IActionResult> UpsertGovernmentId(Guid id, [FromBody] UpsertGovernmentIdDto dto, CancellationToken ct)
    {
        await _service.UpsertGovernmentIdAsync(id, dto, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/emergency-contacts")]
    public async Task<IActionResult> GetEmergencyContacts(Guid id, CancellationToken ct)
        => Ok(await _service.GetEmergencyContactsAsync(id, ct));

    [HttpPost("{id:guid}/emergency-contacts")]
    public async Task<IActionResult> AddEmergencyContact(Guid id, [FromBody] CreateEmergencyContactDto dto, CancellationToken ct)
    {
        var result = await _service.AddEmergencyContactAsync(id, dto, ct);
        return CreatedAtAction(nameof(GetEmergencyContacts), new { id }, result);
    }

    [HttpDelete("{id:guid}/emergency-contacts/{contactId:guid}")]
    public async Task<IActionResult> DeleteEmergencyContact(Guid id, Guid contactId, CancellationToken ct)
    {
        await _service.DeleteEmergencyContactAsync(id, contactId, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/documents")]
    public async Task<IActionResult> GetDocuments(Guid id, CancellationToken ct)
        => Ok(await _documentService.GetDocumentsAsync(id, ct));

    [HttpPost("{id:guid}/documents")]
    public async Task<IActionResult> UploadDocument(Guid id, [FromForm] IFormFile file, [FromQuery] DocumentType documentType, CancellationToken ct)
    {
        using var stream = file.OpenReadStream();
        var result = await _documentService.UploadDocumentAsync(id, documentType, file.FileName, stream, file.ContentType, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}/documents/{documentId:guid}/download")]
    public async Task<IActionResult> GetDownloadUrl(Guid id, Guid documentId, CancellationToken ct)
    {
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
