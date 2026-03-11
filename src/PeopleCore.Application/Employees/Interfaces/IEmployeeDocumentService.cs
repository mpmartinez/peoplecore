using PeopleCore.Application.Employees.DTOs;
using PeopleCore.Domain.Enums;

namespace PeopleCore.Application.Employees.Interfaces;

public interface IEmployeeDocumentService
{
    Task<IReadOnlyList<EmployeeDocumentDto>> GetDocumentsAsync(Guid employeeId, CancellationToken ct = default);
    Task<EmployeeDocumentDto> UploadDocumentAsync(Guid employeeId, DocumentType documentType, string fileName, Stream fileStream, string contentType, CancellationToken ct = default);
    Task<string> GetDownloadUrlAsync(Guid employeeId, Guid documentId, CancellationToken ct = default);
    Task DeleteDocumentAsync(Guid employeeId, Guid documentId, CancellationToken ct = default);
}
