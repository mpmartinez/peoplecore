using PeopleCore.Domain.Enums;

namespace PeopleCore.Domain.Entities.Employees;

public class EmployeeDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public DocumentType DocumentType { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public long? FileSizeBytes { get; set; }
    public string? ContentType { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public Guid? UploadedBy { get; set; }
}
