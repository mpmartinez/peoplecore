# Phase 6: Recruitment Module

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Job postings, applicant tracking, interview stages, and convert hired applicant into employee record.

**Prereq:** Phase 5 complete.

---

### Task 18: Recruitment — DTOs, Interfaces, Unit Tests

**Files:**
- Create: `src/PeopleCore.Application/Recruitment/DTOs/RecruitmentDtos.cs`
- Create: `src/PeopleCore.Application/Recruitment/Interfaces/IJobPostingService.cs`
- Create: `src/PeopleCore.Application/Recruitment/Interfaces/IApplicantService.cs`
- Create: `src/PeopleCore.Application/Recruitment/Interfaces/IInterviewService.cs`
- Create: `tests/PeopleCore.Application.Tests/Recruitment/ApplicantServiceTests.cs`

**Step 1: Create DTOs**

```csharp
// src/PeopleCore.Application/Recruitment/DTOs/RecruitmentDtos.cs
using PeopleCore.Domain.Enums;

namespace PeopleCore.Application.Recruitment.DTOs;

public record JobPostingDto(
    Guid Id, string Title, Guid? DepartmentId, string? DepartmentName,
    Guid? PositionId, string? PositionTitle,
    string? Description, string? Requirements, int Vacancies,
    string Status, DateTime? PostedAt, DateTime? ClosedAt);

public record CreateJobPostingDto(
    string Title, Guid? DepartmentId, Guid? PositionId,
    string? Description, string? Requirements, int Vacancies);

public record UpdateJobPostingDto(
    string Title, Guid? DepartmentId, Guid? PositionId,
    string? Description, string? Requirements, int Vacancies, string Status);

public record ApplicantDto(
    Guid Id, Guid JobPostingId, string JobPostingTitle,
    string FirstName, string LastName, string Email, string? Phone,
    ApplicantStatus Status, Guid? ConvertedEmployeeId, DateTime AppliedAt);

public record CreateApplicantDto(
    Guid JobPostingId, string FirstName, string LastName,
    string Email, string? Phone);

public record UpdateApplicantStatusDto(ApplicantStatus Status);

public record ConvertToEmployeeDto(
    string EmployeeNumber,
    Guid? DepartmentId,
    Guid? PositionId,
    Guid? ReportingManagerId,
    EmploymentStatus EmploymentStatus,
    DateOnly HireDate);

public record InterviewStageDto(
    Guid Id, Guid ApplicantId, string ApplicantName,
    string StageName, DateTime? ScheduledAt,
    Guid? InterviewerId, string? InterviewerName,
    string? Outcome, string? Notes);

public record CreateInterviewStageDto(
    Guid ApplicantId, string StageName,
    DateTime? ScheduledAt, Guid? InterviewerId);

public record UpdateInterviewStageDto(
    DateTime? ScheduledAt, Guid? InterviewerId,
    string? Outcome, string? Notes);
```

**Step 2: Create service interfaces**

```csharp
// src/PeopleCore.Application/Recruitment/Interfaces/IJobPostingService.cs
using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Recruitment.DTOs;

namespace PeopleCore.Application.Recruitment.Interfaces;

public interface IJobPostingService
{
    Task<PagedResult<JobPostingDto>> GetAllAsync(string? status, int page, int pageSize, CancellationToken ct = default);
    Task<JobPostingDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<JobPostingDto> CreateAsync(CreateJobPostingDto dto, CancellationToken ct = default);
    Task<JobPostingDto> UpdateAsync(Guid id, UpdateJobPostingDto dto, CancellationToken ct = default);
    Task<JobPostingDto> PublishAsync(Guid id, CancellationToken ct = default);
    Task<JobPostingDto> CloseAsync(Guid id, CancellationToken ct = default);
}

// src/PeopleCore.Application/Recruitment/Interfaces/IApplicantService.cs
using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Recruitment.DTOs;
using PeopleCore.Application.Employees.DTOs;

namespace PeopleCore.Application.Recruitment.Interfaces;

public interface IApplicantService
{
    Task<PagedResult<ApplicantDto>> GetAllAsync(Guid? jobPostingId, string? status, int page, int pageSize, CancellationToken ct = default);
    Task<ApplicantDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ApplicantDto> CreateAsync(CreateApplicantDto dto, CancellationToken ct = default);
    Task<ApplicantDto> UpdateStatusAsync(Guid id, UpdateApplicantStatusDto dto, CancellationToken ct = default);
    Task<EmployeeDto> ConvertToEmployeeAsync(Guid applicantId, ConvertToEmployeeDto dto, CancellationToken ct = default);
}

// src/PeopleCore.Application/Recruitment/Interfaces/IInterviewService.cs
using PeopleCore.Application.Recruitment.DTOs;

namespace PeopleCore.Application.Recruitment.Interfaces;

public interface IInterviewService
{
    Task<IReadOnlyList<InterviewStageDto>> GetByApplicantAsync(Guid applicantId, CancellationToken ct = default);
    Task<InterviewStageDto> CreateAsync(CreateInterviewStageDto dto, CancellationToken ct = default);
    Task<InterviewStageDto> UpdateAsync(Guid id, UpdateInterviewStageDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
```

**Step 3: Write failing tests**

```csharp
// tests/PeopleCore.Application.Tests/Recruitment/ApplicantServiceTests.cs
using FluentAssertions;
using Moq;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Recruitment.DTOs;
using PeopleCore.Application.Recruitment.Interfaces;
using PeopleCore.Application.Recruitment.Services;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Recruitment;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Recruitment;

public class ApplicantServiceTests
{
    private readonly Mock<IApplicantRepository> _applicantRepo = new();
    private readonly Mock<IJobPostingRepository> _jobRepo = new();
    private readonly Mock<IEmployeeRepository> _employeeRepo = new();
    private readonly ApplicantService _sut;

    public ApplicantServiceTests()
    {
        _sut = new ApplicantService(_applicantRepo.Object, _jobRepo.Object, _employeeRepo.Object);
    }

    [Fact]
    public async Task ConvertToEmployeeAsync_WhenApplicantNotHired_ThrowsDomainException()
    {
        var applicant = new Applicant
        {
            Id = Guid.NewGuid(),
            JobPostingId = Guid.NewGuid(),
            FirstName = "Maria",
            LastName = "Santos",
            Email = "maria@test.com",
            Status = ApplicantStatus.Interview
        };
        _applicantRepo.Setup(r => r.GetByIdAsync(applicant.Id, default)).ReturnsAsync(applicant);

        var act = () => _sut.ConvertToEmployeeAsync(applicant.Id, new ConvertToEmployeeDto(
            "EMP-002", null, null, null, EmploymentStatus.Probationary, new DateOnly(2025, 3, 1)));

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("*Hired status*");
    }

    [Fact]
    public async Task ConvertToEmployeeAsync_WhenAlreadyConverted_ThrowsDomainException()
    {
        var applicant = new Applicant
        {
            Id = Guid.NewGuid(),
            JobPostingId = Guid.NewGuid(),
            FirstName = "Maria",
            LastName = "Santos",
            Email = "maria@test.com",
            Status = ApplicantStatus.Hired,
            ConvertedEmployeeId = Guid.NewGuid()
        };
        _applicantRepo.Setup(r => r.GetByIdAsync(applicant.Id, default)).ReturnsAsync(applicant);

        var act = () => _sut.ConvertToEmployeeAsync(applicant.Id, new ConvertToEmployeeDto(
            "EMP-002", null, null, null, EmploymentStatus.Probationary, new DateOnly(2025, 3, 1)));

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("*already been converted*");
    }

    [Fact]
    public async Task ConvertToEmployeeAsync_WhenValid_CreatesEmployeeWithApplicantData()
    {
        var applicant = new Applicant
        {
            Id = Guid.NewGuid(),
            JobPostingId = Guid.NewGuid(),
            FirstName = "Maria",
            LastName = "Santos",
            Email = "maria@test.com",
            Status = ApplicantStatus.Hired,
            ConvertedEmployeeId = null
        };
        var dto = new ConvertToEmployeeDto("EMP-002", null, null, null, EmploymentStatus.Probationary, new DateOnly(2025, 3, 1));

        _applicantRepo.Setup(r => r.GetByIdAsync(applicant.Id, default)).ReturnsAsync(applicant);
        _employeeRepo.Setup(r => r.EmployeeNumberExistsAsync("EMP-002", default)).ReturnsAsync(false);
        _employeeRepo.Setup(r => r.AddAsync(It.IsAny<Employee>(), default))
                     .ReturnsAsync((Employee e, CancellationToken _) => e);
        _applicantRepo.Setup(r => r.UpdateAsync(It.IsAny<Applicant>(), default)).Returns(Task.CompletedTask);

        var result = await _sut.ConvertToEmployeeAsync(applicant.Id, dto);

        result.Should().NotBeNull();
        result.FirstName.Should().Be("Maria");
        result.LastName.Should().Be("Santos");
        result.WorkEmail.Should().Be("maria@test.com");
        result.EmployeeNumber.Should().Be("EMP-002");
    }
}
```

**Step 4: Run failing tests**

```bash
dotnet test tests/PeopleCore.Application.Tests/PeopleCore.Application.Tests.csproj --filter "ApplicantServiceTests"
```
Expected: FAIL.

**Step 5: Add repository interfaces**

```csharp
// src/PeopleCore.Application/Recruitment/Interfaces/IApplicantRepository.cs
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities.Recruitment;

namespace PeopleCore.Application.Recruitment.Interfaces;

public interface IApplicantRepository : IRepository<Applicant>
{
    Task<(IReadOnlyList<Applicant> Items, int TotalCount)> GetPagedAsync(
        Guid? jobPostingId, string? status, int page, int pageSize, CancellationToken ct = default);
}

// src/PeopleCore.Application/Recruitment/Interfaces/IJobPostingRepository.cs
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities.Recruitment;

namespace PeopleCore.Application.Recruitment.Interfaces;

public interface IJobPostingRepository : IRepository<JobPosting>
{
    Task<(IReadOnlyList<JobPosting> Items, int TotalCount)> GetPagedAsync(
        string? status, int page, int pageSize, CancellationToken ct = default);
}
```

**Step 6: Implement ApplicantService**

```csharp
// src/PeopleCore.Application/Recruitment/Services/ApplicantService.cs
using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Employees.DTOs;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Recruitment.DTOs;
using PeopleCore.Application.Recruitment.Interfaces;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Recruitment;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.Application.Recruitment.Services;

public class ApplicantService : IApplicantService
{
    private readonly IApplicantRepository _applicantRepo;
    private readonly IJobPostingRepository _jobRepo;
    private readonly IEmployeeRepository _employeeRepo;

    public ApplicantService(
        IApplicantRepository applicantRepo,
        IJobPostingRepository jobRepo,
        IEmployeeRepository employeeRepo)
    {
        _applicantRepo = applicantRepo;
        _jobRepo = jobRepo;
        _employeeRepo = employeeRepo;
    }

    public async Task<PagedResult<ApplicantDto>> GetAllAsync(
        Guid? jobPostingId, string? status, int page, int pageSize, CancellationToken ct = default)
    {
        var (items, total) = await _applicantRepo.GetPagedAsync(jobPostingId, status, page, pageSize, ct);
        return PagedResult<ApplicantDto>.Create(items.Select(ToDto).ToList(), total, page, pageSize);
    }

    public async Task<ApplicantDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var applicant = await _applicantRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Applicant {id} not found.");
        return ToDto(applicant);
    }

    public async Task<ApplicantDto> CreateAsync(CreateApplicantDto dto, CancellationToken ct = default)
    {
        var posting = await _jobRepo.GetByIdAsync(dto.JobPostingId, ct)
            ?? throw new KeyNotFoundException($"Job posting {dto.JobPostingId} not found.");

        if (posting.Status != "Open")
            throw new DomainException("Cannot apply to a job posting that is not open.");

        var applicant = new Applicant
        {
            JobPostingId = dto.JobPostingId,
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            Email = dto.Email,
            Phone = dto.Phone,
            Status = ApplicantStatus.Applied
        };

        var created = await _applicantRepo.AddAsync(applicant, ct);
        return ToDto(created);
    }

    public async Task<ApplicantDto> UpdateStatusAsync(Guid id, UpdateApplicantStatusDto dto, CancellationToken ct = default)
    {
        var applicant = await _applicantRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Applicant {id} not found.");
        applicant.Status = dto.Status;
        applicant.UpdatedAt = DateTime.UtcNow;
        await _applicantRepo.UpdateAsync(applicant, ct);
        return ToDto(applicant);
    }

    public async Task<EmployeeDto> ConvertToEmployeeAsync(Guid applicantId, ConvertToEmployeeDto dto, CancellationToken ct = default)
    {
        var applicant = await _applicantRepo.GetByIdAsync(applicantId, ct)
            ?? throw new KeyNotFoundException($"Applicant {applicantId} not found.");

        if (applicant.Status != ApplicantStatus.Hired)
            throw new DomainException("Applicant must be in Hired status before converting to employee.");

        if (applicant.ConvertedEmployeeId.HasValue)
            throw new DomainException("This applicant has already been converted to an employee.");

        if (await _employeeRepo.EmployeeNumberExistsAsync(dto.EmployeeNumber, ct))
            throw new DomainException($"Employee number '{dto.EmployeeNumber}' already exists.");

        var employee = new Employee
        {
            EmployeeNumber = dto.EmployeeNumber,
            FirstName = applicant.FirstName,
            LastName = applicant.LastName,
            DateOfBirth = new DateOnly(1990, 1, 1), // placeholder — HR fills in profile
            Gender = "Unknown", // placeholder
            WorkEmail = applicant.Email,
            DepartmentId = dto.DepartmentId,
            PositionId = dto.PositionId,
            ReportingManagerId = dto.ReportingManagerId,
            EmploymentStatus = dto.EmploymentStatus,
            EmploymentType = "FullTime",
            HireDate = dto.HireDate,
            IsActive = true,
            Is13thMonthEligible = true
        };

        var created = await _employeeRepo.AddAsync(employee, ct);

        applicant.ConvertedEmployeeId = created.Id;
        applicant.UpdatedAt = DateTime.UtcNow;
        await _applicantRepo.UpdateAsync(applicant, ct);

        return new EmployeeDto(
            created.Id, created.EmployeeNumber,
            created.FirstName, created.MiddleName, created.LastName, created.FullName,
            created.DateOfBirth, created.Gender, created.CivilStatus, created.WorkEmail,
            created.MobileNumber, created.DepartmentId, null, created.PositionId, null,
            created.ReportingManagerId, null, created.EmploymentStatus, created.EmploymentType,
            created.HireDate, created.RegularizationDate, created.IsActive, created.Is13thMonthEligible);
    }

    private static ApplicantDto ToDto(Applicant a) => new(
        a.Id, a.JobPostingId, a.JobPosting?.Title ?? string.Empty,
        a.FirstName, a.LastName, a.Email, a.Phone,
        a.Status, a.ConvertedEmployeeId, a.AppliedAt);
}
```

**Step 7: Run tests — verify pass**

```bash
dotnet test tests/PeopleCore.Application.Tests/PeopleCore.Application.Tests.csproj --filter "ApplicantServiceTests"
```
Expected: `Passed! - Failed: 0`

**Step 8: Implement JobPostingService and InterviewService** (same pattern — CRUD with status validation)

**Step 9: Create Recruitment Controllers**

```csharp
// src/PeopleCore.API/Controllers/Recruitment/RecruitmentController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Recruitment.DTOs;
using PeopleCore.Application.Recruitment.Interfaces;

namespace PeopleCore.API.Controllers.Recruitment;

[ApiController]
[Route("api")]
[Authorize]
public class RecruitmentController : ControllerBase
{
    private readonly IJobPostingService _jobService;
    private readonly IApplicantService _applicantService;
    private readonly IInterviewService _interviewService;

    public RecruitmentController(
        IJobPostingService jobService,
        IApplicantService applicantService,
        IInterviewService interviewService)
    {
        _jobService = jobService;
        _applicantService = applicantService;
        _interviewService = interviewService;
    }

    // Job Postings
    [HttpGet("job-postings")]
    public async Task<IActionResult> GetPostings([FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await _jobService.GetAllAsync(status, page, pageSize, ct));

    [HttpGet("job-postings/{id:guid}")]
    public async Task<IActionResult> GetPosting(Guid id, CancellationToken ct)
        => Ok(await _jobService.GetByIdAsync(id, ct));

    [HttpPost("job-postings")]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> CreatePosting([FromBody] CreateJobPostingDto dto, CancellationToken ct)
        => Ok(await _jobService.CreateAsync(dto, ct));

    [HttpPut("job-postings/{id:guid}")]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> UpdatePosting(Guid id, [FromBody] UpdateJobPostingDto dto, CancellationToken ct)
        => Ok(await _jobService.UpdateAsync(id, dto, ct));

    [HttpPut("job-postings/{id:guid}/publish")]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> PublishPosting(Guid id, CancellationToken ct)
        => Ok(await _jobService.PublishAsync(id, ct));

    // Applicants
    [HttpGet("applicants")]
    public async Task<IActionResult> GetApplicants([FromQuery] Guid? jobPostingId, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await _applicantService.GetAllAsync(jobPostingId, status, page, pageSize, ct));

    [HttpPost("applicants")]
    public async Task<IActionResult> CreateApplicant([FromBody] CreateApplicantDto dto, CancellationToken ct)
        => Ok(await _applicantService.CreateAsync(dto, ct));

    [HttpPut("applicants/{id:guid}/status")]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateApplicantStatusDto dto, CancellationToken ct)
        => Ok(await _applicantService.UpdateStatusAsync(id, dto, ct));

    [HttpPost("applicants/{id:guid}/convert-to-employee")]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> ConvertToEmployee(Guid id, [FromBody] ConvertToEmployeeDto dto, CancellationToken ct)
        => Ok(await _applicantService.ConvertToEmployeeAsync(id, dto, ct));

    // Interviews
    [HttpGet("applicants/{applicantId:guid}/interviews")]
    public async Task<IActionResult> GetInterviews(Guid applicantId, CancellationToken ct)
        => Ok(await _interviewService.GetByApplicantAsync(applicantId, ct));

    [HttpPost("interviews")]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> CreateInterview([FromBody] CreateInterviewStageDto dto, CancellationToken ct)
        => Ok(await _interviewService.CreateAsync(dto, ct));

    [HttpPut("interviews/{id:guid}")]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> UpdateInterview(Guid id, [FromBody] UpdateInterviewStageDto dto, CancellationToken ct)
        => Ok(await _interviewService.UpdateAsync(id, dto, ct));
}
```

**Step 10: Commit**

```bash
git add -A
git commit -m "feat: implement recruitment module with applicant-to-employee conversion"
```

---

**Phase 6 complete.** Continue with [Phase 7 — Performance](2026-03-10-hrms-phase-7-performance.md).
