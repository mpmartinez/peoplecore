using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Employees.DTOs;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Enums;

namespace PeopleCore.Application.Employees.Services;

public class EmployeeDocumentService : IEmployeeDocumentService
{
    private readonly IEmployeeRepository _employeeRepo;
    private readonly IStorageService _storage;
    private const string BucketName = "peoplecore-documents";

    public EmployeeDocumentService(IEmployeeRepository employeeRepo, IStorageService storage)
    {
        _employeeRepo = employeeRepo;
        _storage = storage;
    }

    public async Task<IReadOnlyList<EmployeeDocumentDto>> GetDocumentsAsync(Guid employeeId, CancellationToken ct = default)
    {
        var employee = await _employeeRepo.GetByIdAsync(employeeId, ct)
            ?? throw new KeyNotFoundException($"Employee {employeeId} not found.");
        return employee.Documents
            .Select(d => new EmployeeDocumentDto(d.Id, d.DocumentType.ToString(), d.FileName, d.FileSizeBytes, d.UploadedAt))
            .ToList();
    }

    public async Task<EmployeeDocumentDto> UploadDocumentAsync(
        Guid employeeId, DocumentType documentType, string fileName,
        Stream fileStream, string contentType, CancellationToken ct = default)
    {
        var employee = await _employeeRepo.GetByIdAsync(employeeId, ct)
            ?? throw new KeyNotFoundException($"Employee {employeeId} not found.");

        var objectKey = $"employees/{employeeId}/{documentType}/{Guid.NewGuid()}_{fileName}";
        await _storage.UploadAsync(BucketName, objectKey, fileStream, contentType, ct);

        var document = new EmployeeDocument
        {
            EmployeeId = employeeId,
            DocumentType = documentType,
            FileName = fileName,
            StorageKey = objectKey,
            ContentType = contentType,
            UploadedAt = DateTime.UtcNow
        };
        employee.Documents.Add(document);
        await _employeeRepo.UpdateAsync(employee, ct);

        return new EmployeeDocumentDto(document.Id, document.DocumentType.ToString(), document.FileName, document.FileSizeBytes, document.UploadedAt);
    }

    public async Task<string> GetDownloadUrlAsync(Guid employeeId, Guid documentId, CancellationToken ct = default)
    {
        var employee = await _employeeRepo.GetByIdAsync(employeeId, ct)
            ?? throw new KeyNotFoundException($"Employee {employeeId} not found.");
        var doc = employee.Documents.FirstOrDefault(d => d.Id == documentId)
            ?? throw new KeyNotFoundException($"Document {documentId} not found.");
        return await _storage.GetPresignedUrlAsync(BucketName, doc.StorageKey);
    }

    public async Task DeleteDocumentAsync(Guid employeeId, Guid documentId, CancellationToken ct = default)
    {
        var employee = await _employeeRepo.GetByIdAsync(employeeId, ct)
            ?? throw new KeyNotFoundException($"Employee {employeeId} not found.");
        var doc = employee.Documents.FirstOrDefault(d => d.Id == documentId)
            ?? throw new KeyNotFoundException($"Document {documentId} not found.");
        await _storage.DeleteAsync(BucketName, doc.StorageKey, ct);
        employee.Documents.Remove(doc);
        await _employeeRepo.UpdateAsync(employee, ct);
    }
}
