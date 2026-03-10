# Phase 3: Employee Module

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Employee CRUD, Government IDs, Emergency Contacts, Document uploads via MinIO.

**Prereq:** Phase 2 complete.

---

### Task 12: Employee — DTOs, Interfaces, and Unit Tests

**Files:**
- Create: `src/PeopleCore.Application/Employees/DTOs/EmployeeDtos.cs`
- Create: `src/PeopleCore.Application/Employees/Interfaces/IEmployeeRepository.cs`
- Create: `src/PeopleCore.Application/Employees/Interfaces/IEmployeeService.cs`
- Create: `src/PeopleCore.Application/Employees/Interfaces/IEmployeeDocumentService.cs`
- Create: `tests/PeopleCore.Application.Tests/Employees/EmployeeServiceTests.cs`

**Step 1: Create DTOs**

```csharp
// src/PeopleCore.Application/Employees/DTOs/EmployeeDtos.cs
using PeopleCore.Domain.Enums;

namespace PeopleCore.Application.Employees.DTOs;

public record EmployeeDto(
    Guid Id,
    string EmployeeNumber,
    string FirstName,
    string? MiddleName,
    string LastName,
    string FullName,
    DateOnly DateOfBirth,
    string Gender,
    string? CivilStatus,
    string WorkEmail,
    string? MobileNumber,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid? PositionId,
    string? PositionTitle,
    Guid? ReportingManagerId,
    string? ReportingManagerName,
    EmploymentStatus EmploymentStatus,
    string EmploymentType,
    DateOnly HireDate,
    DateOnly? RegularizationDate,
    bool IsActive,
    bool Is13thMonthEligible);

public record CreateEmployeeDto(
    string EmployeeNumber,
    string FirstName,
    string? MiddleName,
    string LastName,
    DateOnly DateOfBirth,
    string Gender,
    string WorkEmail,
    string? MobileNumber,
    Guid? DepartmentId,
    Guid? PositionId,
    Guid? ReportingManagerId,
    EmploymentStatus EmploymentStatus,
    string EmploymentType,
    DateOnly HireDate);

public record UpdateEmployeeDto(
    string FirstName,
    string? MiddleName,
    string LastName,
    string? CivilStatus,
    string? PersonalEmail,
    string? MobileNumber,
    string? Address,
    Guid? DepartmentId,
    Guid? PositionId,
    Guid? TeamId,
    Guid? ReportingManagerId,
    EmploymentStatus EmploymentStatus,
    DateOnly? RegularizationDate,
    bool Is13thMonthEligible);

public record EmployeeFilterDto(
    string? Search,
    Guid? DepartmentId,
    EmploymentStatus? Status,
    bool? IsActive,
    int Page = 1,
    int PageSize = 20);

public record GovernmentIdDto(Guid Id, GovernmentIdType IdType, string IdNumber);
public record UpsertGovernmentIdDto(GovernmentIdType IdType, string IdNumber);

public record EmergencyContactDto(Guid Id, string Name, string Relationship, string Phone, string? Address);
public record CreateEmergencyContactDto(string Name, string Relationship, string Phone, string? Address);

public record EmployeeDocumentDto(Guid Id, string DocumentType, string FileName, long? FileSizeBytes, DateTime UploadedAt);
```

**Step 2: Create service interfaces**

```csharp
// src/PeopleCore.Application/Employees/Interfaces/IEmployeeService.cs
using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Employees.DTOs;

namespace PeopleCore.Application.Employees.Interfaces;

public interface IEmployeeService
{
    Task<PagedResult<EmployeeDto>> GetAllAsync(EmployeeFilterDto filter, CancellationToken ct = default);
    Task<EmployeeDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<EmployeeDto> CreateAsync(CreateEmployeeDto dto, CancellationToken ct = default);
    Task<EmployeeDto> UpdateAsync(Guid id, UpdateEmployeeDto dto, CancellationToken ct = default);
    Task DeactivateAsync(Guid id, DateOnly separationDate, CancellationToken ct = default);
    Task<IReadOnlyList<GovernmentIdDto>> GetGovernmentIdsAsync(Guid employeeId, CancellationToken ct = default);
    Task UpsertGovernmentIdAsync(Guid employeeId, UpsertGovernmentIdDto dto, CancellationToken ct = default);
    Task<IReadOnlyList<EmergencyContactDto>> GetEmergencyContactsAsync(Guid employeeId, CancellationToken ct = default);
    Task<EmergencyContactDto> AddEmergencyContactAsync(Guid employeeId, CreateEmergencyContactDto dto, CancellationToken ct = default);
    Task DeleteEmergencyContactAsync(Guid employeeId, Guid contactId, CancellationToken ct = default);
}

// src/PeopleCore.Application/Employees/Interfaces/IEmployeeDocumentService.cs
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

// src/PeopleCore.Application/Employees/Interfaces/IEmployeeRepository.cs
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Employees.DTOs;
using PeopleCore.Domain.Entities.Employees;

namespace PeopleCore.Application.Employees.Interfaces;

public interface IEmployeeRepository : IRepository<Employee>
{
    Task<(IReadOnlyList<Employee> Items, int TotalCount)> GetPagedAsync(EmployeeFilterDto filter, CancellationToken ct = default);
    Task<bool> EmployeeNumberExistsAsync(string employeeNumber, CancellationToken ct = default);
}
```

**Step 3: Write failing tests**

```csharp
// tests/PeopleCore.Application.Tests/Employees/EmployeeServiceTests.cs
using FluentAssertions;
using Moq;
using PeopleCore.Application.Employees.DTOs;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Employees.Services;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Employees;

public class EmployeeServiceTests
{
    private readonly Mock<IEmployeeRepository> _repo = new();
    private readonly EmployeeService _sut;

    public EmployeeServiceTests()
    {
        _sut = new EmployeeService(_repo.Object);
    }

    [Fact]
    public async Task GetByIdAsync_WhenEmployeeNotFound_ThrowsKeyNotFoundException()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default))
             .ReturnsAsync((Employee?)null);

        var act = () => _sut.GetByIdAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<KeyNotFoundException>()
                 .WithMessage("*not found*");
    }

    [Fact]
    public async Task CreateAsync_WhenEmployeeNumberAlreadyExists_ThrowsDomainException()
    {
        _repo.Setup(r => r.EmployeeNumberExistsAsync("EMP-001", default)).ReturnsAsync(true);

        var dto = new CreateEmployeeDto("EMP-001", "Juan", null, "dela Cruz",
            new DateOnly(1990, 1, 1), "Male", "juan@company.com", null,
            null, null, null, EmploymentStatus.Probationary, "FullTime",
            new DateOnly(2024, 1, 1));

        var act = () => _sut.CreateAsync(dto);

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("*already exists*");
    }

    [Fact]
    public async Task DeactivateAsync_WhenEmployeeAlreadyInactive_ThrowsDomainException()
    {
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeNumber = "EMP-001",
            FirstName = "Juan",
            LastName = "dela Cruz",
            DateOfBirth = new DateOnly(1990, 1, 1),
            Gender = "Male",
            WorkEmail = "juan@company.com",
            EmploymentStatus = EmploymentStatus.Regular,
            EmploymentType = "FullTime",
            HireDate = new DateOnly(2020, 1, 1),
            IsActive = false
        };
        _repo.Setup(r => r.GetByIdAsync(employee.Id, default)).ReturnsAsync(employee);

        var act = () => _sut.DeactivateAsync(employee.Id, new DateOnly(2025, 1, 1));

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("*already inactive*");
    }

    [Fact]
    public async Task CreateAsync_WithValidData_ReturnsEmployeeDto()
    {
        _repo.Setup(r => r.EmployeeNumberExistsAsync("EMP-001", default)).ReturnsAsync(false);
        var dto = new CreateEmployeeDto("EMP-001", "Juan", null, "dela Cruz",
            new DateOnly(1990, 1, 1), "Male", "juan@company.com", null,
            null, null, null, EmploymentStatus.Probationary, "FullTime",
            new DateOnly(2024, 1, 1));

        _repo.Setup(r => r.AddAsync(It.IsAny<Employee>(), default))
             .ReturnsAsync((Employee e, CancellationToken _) => e);

        var result = await _sut.CreateAsync(dto);

        result.EmployeeNumber.Should().Be("EMP-001");
        result.FirstName.Should().Be("Juan");
        result.EmploymentStatus.Should().Be(EmploymentStatus.Probationary);
        result.IsActive.Should().BeTrue();
    }
}
```

**Step 4: Run failing tests**

```bash
dotnet test tests/PeopleCore.Application.Tests/PeopleCore.Application.Tests.csproj --filter "EmployeeServiceTests"
```
Expected: FAIL — `EmployeeService` does not exist.

**Step 5: Implement EmployeeService**

```csharp
// src/PeopleCore.Application/Employees/Services/EmployeeService.cs
using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Employees.DTOs;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.Application.Employees.Services;

public class EmployeeService : IEmployeeService
{
    private readonly IEmployeeRepository _repo;

    public EmployeeService(IEmployeeRepository repo) => _repo = repo;

    public async Task<PagedResult<EmployeeDto>> GetAllAsync(EmployeeFilterDto filter, CancellationToken ct = default)
    {
        var (items, total) = await _repo.GetPagedAsync(filter, ct);
        return PagedResult<EmployeeDto>.Create(items.Select(ToDto).ToList(), total, filter.Page, filter.PageSize);
    }

    public async Task<EmployeeDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var employee = await _repo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Employee {id} not found.");
        return ToDto(employee);
    }

    public async Task<EmployeeDto> CreateAsync(CreateEmployeeDto dto, CancellationToken ct = default)
    {
        if (await _repo.EmployeeNumberExistsAsync(dto.EmployeeNumber, ct))
            throw new DomainException($"Employee number '{dto.EmployeeNumber}' already exists.");

        var employee = new Employee
        {
            EmployeeNumber = dto.EmployeeNumber,
            FirstName = dto.FirstName,
            MiddleName = dto.MiddleName,
            LastName = dto.LastName,
            DateOfBirth = dto.DateOfBirth,
            Gender = dto.Gender,
            WorkEmail = dto.WorkEmail,
            MobileNumber = dto.MobileNumber,
            DepartmentId = dto.DepartmentId,
            PositionId = dto.PositionId,
            ReportingManagerId = dto.ReportingManagerId,
            EmploymentStatus = dto.EmploymentStatus,
            EmploymentType = dto.EmploymentType,
            HireDate = dto.HireDate,
            IsActive = true,
            Is13thMonthEligible = true
        };

        var created = await _repo.AddAsync(employee, ct);
        return ToDto(created);
    }

    public async Task<EmployeeDto> UpdateAsync(Guid id, UpdateEmployeeDto dto, CancellationToken ct = default)
    {
        var employee = await _repo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Employee {id} not found.");

        employee.FirstName = dto.FirstName;
        employee.MiddleName = dto.MiddleName;
        employee.LastName = dto.LastName;
        employee.CivilStatus = dto.CivilStatus;
        employee.PersonalEmail = dto.PersonalEmail;
        employee.MobileNumber = dto.MobileNumber;
        employee.Address = dto.Address;
        employee.DepartmentId = dto.DepartmentId;
        employee.PositionId = dto.PositionId;
        employee.TeamId = dto.TeamId;
        employee.ReportingManagerId = dto.ReportingManagerId;
        employee.EmploymentStatus = dto.EmploymentStatus;
        employee.RegularizationDate = dto.RegularizationDate;
        employee.Is13thMonthEligible = dto.Is13thMonthEligible;
        employee.UpdatedAt = DateTime.UtcNow;

        await _repo.UpdateAsync(employee, ct);
        return ToDto(employee);
    }

    public async Task DeactivateAsync(Guid id, DateOnly separationDate, CancellationToken ct = default)
    {
        var employee = await _repo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Employee {id} not found.");
        if (!employee.IsActive)
            throw new DomainException("Employee is already inactive.");

        employee.IsActive = false;
        employee.SeparationDate = separationDate;
        employee.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(employee, ct);
    }

    public async Task<IReadOnlyList<GovernmentIdDto>> GetGovernmentIdsAsync(Guid employeeId, CancellationToken ct = default)
    {
        var employee = await _repo.GetByIdAsync(employeeId, ct)
            ?? throw new KeyNotFoundException($"Employee {employeeId} not found.");
        return employee.GovernmentIds.Select(g => new GovernmentIdDto(g.Id, g.IdType, g.IdNumber)).ToList();
    }

    public async Task UpsertGovernmentIdAsync(Guid employeeId, UpsertGovernmentIdDto dto, CancellationToken ct = default)
    {
        var employee = await _repo.GetByIdAsync(employeeId, ct)
            ?? throw new KeyNotFoundException($"Employee {employeeId} not found.");

        var existing = employee.GovernmentIds.FirstOrDefault(g => g.IdType == dto.IdType);
        if (existing is not null)
        {
            existing.IdNumber = dto.IdNumber;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            employee.GovernmentIds.Add(new EmployeeGovernmentId
            {
                EmployeeId = employeeId,
                IdType = dto.IdType,
                IdNumber = dto.IdNumber
            });
        }
        await _repo.UpdateAsync(employee, ct);
    }

    public async Task<IReadOnlyList<EmergencyContactDto>> GetEmergencyContactsAsync(Guid employeeId, CancellationToken ct = default)
    {
        var employee = await _repo.GetByIdAsync(employeeId, ct)
            ?? throw new KeyNotFoundException($"Employee {employeeId} not found.");
        return employee.EmergencyContacts
            .Select(c => new EmergencyContactDto(c.Id, c.Name, c.Relationship, c.Phone, c.Address))
            .ToList();
    }

    public async Task<EmergencyContactDto> AddEmergencyContactAsync(Guid employeeId, CreateEmergencyContactDto dto, CancellationToken ct = default)
    {
        var employee = await _repo.GetByIdAsync(employeeId, ct)
            ?? throw new KeyNotFoundException($"Employee {employeeId} not found.");
        var contact = new EmergencyContact
        {
            EmployeeId = employeeId,
            Name = dto.Name,
            Relationship = dto.Relationship,
            Phone = dto.Phone,
            Address = dto.Address
        };
        employee.EmergencyContacts.Add(contact);
        await _repo.UpdateAsync(employee, ct);
        return new EmergencyContactDto(contact.Id, contact.Name, contact.Relationship, contact.Phone, contact.Address);
    }

    public async Task DeleteEmergencyContactAsync(Guid employeeId, Guid contactId, CancellationToken ct = default)
    {
        var employee = await _repo.GetByIdAsync(employeeId, ct)
            ?? throw new KeyNotFoundException($"Employee {employeeId} not found.");
        var contact = employee.EmergencyContacts.FirstOrDefault(c => c.Id == contactId)
            ?? throw new KeyNotFoundException($"Emergency contact {contactId} not found.");
        employee.EmergencyContacts.Remove(contact);
        await _repo.UpdateAsync(employee, ct);
    }

    private static EmployeeDto ToDto(Employee e) => new(
        e.Id, e.EmployeeNumber, e.FirstName, e.MiddleName, e.LastName, e.FullName,
        e.DateOfBirth, e.Gender, e.CivilStatus, e.WorkEmail, e.MobileNumber,
        e.DepartmentId, e.Department?.Name,
        e.PositionId, e.Position?.Title,
        e.ReportingManagerId, e.ReportingManager?.FullName,
        e.EmploymentStatus, e.EmploymentType, e.HireDate, e.RegularizationDate,
        e.IsActive, e.Is13thMonthEligible);
}
```

**Step 6: Run tests — verify pass**

```bash
dotnet test tests/PeopleCore.Application.Tests/PeopleCore.Application.Tests.csproj --filter "EmployeeServiceTests"
```
Expected: `Passed! - Failed: 0`

**Step 7: Implement EmployeeDocumentService**

```csharp
// src/PeopleCore.Application/Employees/Services/EmployeeDocumentService.cs
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
```

**Step 8: Implement MinioStorageService in Infrastructure**

```csharp
// src/PeopleCore.Infrastructure/Storage/MinioStorageService.cs
using Minio;
using Minio.DataModel.Args;
using PeopleCore.Application.Common.Interfaces;

namespace PeopleCore.Infrastructure.Storage;

public class MinioStorageService : IStorageService
{
    private readonly IMinioClient _minio;

    public MinioStorageService(IMinioClient minio) => _minio = minio;

    public async Task<string> UploadAsync(string bucketName, string objectKey, Stream stream, string contentType, CancellationToken ct = default)
    {
        await EnsureBucketExistsAsync(bucketName, ct);
        var args = new PutObjectArgs()
            .WithBucket(bucketName)
            .WithObject(objectKey)
            .WithStreamData(stream)
            .WithObjectSize(stream.Length)
            .WithContentType(contentType);
        await _minio.PutObjectAsync(args, ct);
        return objectKey;
    }

    public async Task<Stream> DownloadAsync(string bucketName, string objectKey, CancellationToken ct = default)
    {
        var ms = new MemoryStream();
        var args = new GetObjectArgs()
            .WithBucket(bucketName)
            .WithObject(objectKey)
            .WithCallbackStream(stream => stream.CopyTo(ms));
        await _minio.GetObjectAsync(args, ct);
        ms.Position = 0;
        return ms;
    }

    public async Task DeleteAsync(string bucketName, string objectKey, CancellationToken ct = default)
    {
        var args = new RemoveObjectArgs().WithBucket(bucketName).WithObject(objectKey);
        await _minio.RemoveObjectAsync(args, ct);
    }

    public async Task<string> GetPresignedUrlAsync(string bucketName, string objectKey, int expirySeconds = 3600)
    {
        var args = new PresignedGetObjectArgs()
            .WithBucket(bucketName)
            .WithObject(objectKey)
            .WithExpiry(expirySeconds);
        return await _minio.PresignedGetObjectAsync(args);
    }

    private async Task EnsureBucketExistsAsync(string bucketName, CancellationToken ct)
    {
        var exists = await _minio.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucketName), ct);
        if (!exists)
            await _minio.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucketName), ct);
    }
}
```

**Step 9: Register MinIO in ServiceExtensions**

```csharp
// Add to ServiceExtensions.AddInfrastructure:
var minioConfig = configuration.GetSection("Minio");
services.AddSingleton<IMinioClient>(sp =>
    new MinioClient()
        .WithEndpoint(minioConfig["Endpoint"])
        .WithCredentials(minioConfig["AccessKey"], minioConfig["SecretKey"])
        .WithSSL(bool.Parse(minioConfig["UseSSL"] ?? "false"))
        .Build());
services.AddScoped<IStorageService, MinioStorageService>();
services.AddScoped<IEmployeeRepository, EmployeeRepository>();
services.AddScoped<IEmployeeService, EmployeeService>();
services.AddScoped<IEmployeeDocumentService, EmployeeDocumentService>();
```

**Step 10: Implement EmployeeRepository in Infrastructure**

```csharp
// src/PeopleCore.Infrastructure/Persistence/Repositories/EmployeeRepository.cs
using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Employees.DTOs;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Domain.Entities.Employees;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class EmployeeRepository : Repository<Employee>, IEmployeeRepository
{
    public EmployeeRepository(AppDbContext context) : base(context) { }

    public async Task<(IReadOnlyList<Employee> Items, int TotalCount)> GetPagedAsync(
        EmployeeFilterDto filter, CancellationToken ct = default)
    {
        var query = Context.Employees
            .Include(e => e.Department)
            .Include(e => e.Position)
            .Include(e => e.ReportingManager)
            .Include(e => e.GovernmentIds)
            .Include(e => e.EmergencyContacts)
            .Include(e => e.Documents)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(e =>
                e.FirstName.Contains(filter.Search) ||
                e.LastName.Contains(filter.Search) ||
                e.EmployeeNumber.Contains(filter.Search) ||
                e.WorkEmail.Contains(filter.Search));

        if (filter.DepartmentId.HasValue)
            query = query.Where(e => e.DepartmentId == filter.DepartmentId);

        if (filter.Status.HasValue)
            query = query.Where(e => e.EmploymentStatus == filter.Status);

        if (filter.IsActive.HasValue)
            query = query.Where(e => e.IsActive == filter.IsActive);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(e => e.LastName).ThenBy(e => e.FirstName)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<bool> EmployeeNumberExistsAsync(string employeeNumber, CancellationToken ct = default)
        => await Context.Employees.AnyAsync(e => e.EmployeeNumber == employeeNumber, ct);
}
```

**Step 11: Commit**

```bash
git add -A
git commit -m "feat: implement employee service, document service, MinIO storage, and repository"
```

---

### Task 13: Employees — Controller

**Files:**
- Create: `src/PeopleCore.API/Controllers/Employees/EmployeesController.cs`

```csharp
// src/PeopleCore.API/Controllers/Employees/EmployeesController.cs
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
    {
        _service = service;
        _documentService = documentService;
    }

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
        => Ok(await _service.AddEmergencyContactAsync(id, dto, ct));

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
    public async Task<IActionResult> UploadDocument(Guid id, IFormFile file, [FromQuery] DocumentType documentType, CancellationToken ct)
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
```

**Step 2: Build**

```bash
dotnet build PeopleCore.sln
```
Expected: `Build succeeded. 0 Error(s)`

**Step 3: Commit**

```bash
git add -A
git commit -m "feat: add employees controller with document upload support"
```

---

**Phase 3 complete.** Continue with [Phase 4 — Attendance](2026-03-10-hrms-phase-4-attendance.md).
