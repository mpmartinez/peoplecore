# Phase 7: Performance Management Module

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Review cycles, employee self-evaluation, manager review, KPI items.

**Prereq:** Phase 6 complete.

---

### Task 19: Performance — DTOs, Interfaces, Service, Tests

**Files:**
- Create: `src/PeopleCore.Application/Performance/DTOs/PerformanceDtos.cs`
- Create: `src/PeopleCore.Application/Performance/Interfaces/IReviewCycleService.cs`
- Create: `src/PeopleCore.Application/Performance/Interfaces/IPerformanceReviewService.cs`
- Create: `src/PeopleCore.Application/Performance/Interfaces/IReviewCycleRepository.cs`
- Create: `src/PeopleCore.Application/Performance/Interfaces/IPerformanceReviewRepository.cs`
- Create: `src/PeopleCore.Application/Performance/Services/ReviewCycleService.cs`
- Create: `src/PeopleCore.Application/Performance/Services/PerformanceReviewService.cs`
- Create: `tests/PeopleCore.Application.Tests/Performance/PerformanceReviewServiceTests.cs`

**Step 1: Create DTOs**

```csharp
// src/PeopleCore.Application/Performance/DTOs/PerformanceDtos.cs
using PeopleCore.Domain.Enums;

namespace PeopleCore.Application.Performance.DTOs;

public record ReviewCycleDto(
    Guid Id, string Name, int Year, int? Quarter,
    DateOnly StartDate, DateOnly EndDate, ReviewStatus Status);

public record CreateReviewCycleDto(
    string Name, int Year, int? Quarter,
    DateOnly StartDate, DateOnly EndDate);

public record PerformanceReviewDto(
    Guid Id, Guid EmployeeId, string EmployeeName,
    Guid ReviewCycleId, string ReviewCycleName,
    Guid ReviewerId, string ReviewerName,
    decimal? SelfEvaluationScore, decimal? ManagerScore, decimal? FinalScore,
    string? SelfEvaluationComments, string? ManagerComments,
    ReviewStatus Status, DateTime? SubmittedAt, DateTime? CompletedAt,
    IReadOnlyList<KpiItemDto> KpiItems);

public record CreatePerformanceReviewDto(
    Guid EmployeeId, Guid ReviewCycleId, Guid ReviewerId,
    IReadOnlyList<CreateKpiItemDto> KpiItems);

public record SubmitSelfEvaluationDto(
    decimal Score, string? Comments,
    IReadOnlyList<UpdateKpiItemDto> KpiItems);

public record SubmitManagerReviewDto(
    decimal Score, string? Comments,
    IReadOnlyList<UpdateKpiItemDto> KpiItems);

public record KpiItemDto(
    Guid Id, string Description, string? Target,
    string? Actual, decimal Weight, decimal? Score);

public record CreateKpiItemDto(
    string Description, string? Target, decimal Weight);

public record UpdateKpiItemDto(
    Guid Id, string? Actual, decimal? Score);
```

**Step 2: Create service and repository interfaces**

```csharp
// src/PeopleCore.Application/Performance/Interfaces/IReviewCycleService.cs
using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Performance.DTOs;

namespace PeopleCore.Application.Performance.Interfaces;

public interface IReviewCycleService
{
    Task<PagedResult<ReviewCycleDto>> GetAllAsync(int page, int pageSize, CancellationToken ct = default);
    Task<ReviewCycleDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ReviewCycleDto> CreateAsync(CreateReviewCycleDto dto, CancellationToken ct = default);
    Task<ReviewCycleDto> CloseAsync(Guid id, CancellationToken ct = default);
}

// src/PeopleCore.Application/Performance/Interfaces/IPerformanceReviewService.cs
using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Performance.DTOs;

namespace PeopleCore.Application.Performance.Interfaces;

public interface IPerformanceReviewService
{
    Task<PagedResult<PerformanceReviewDto>> GetAllAsync(Guid? employeeId, Guid? cycleId, int page, int pageSize, CancellationToken ct = default);
    Task<PerformanceReviewDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PerformanceReviewDto> CreateAsync(CreatePerformanceReviewDto dto, CancellationToken ct = default);
    Task<PerformanceReviewDto> SubmitSelfEvaluationAsync(Guid id, Guid employeeId, SubmitSelfEvaluationDto dto, CancellationToken ct = default);
    Task<PerformanceReviewDto> SubmitManagerReviewAsync(Guid id, Guid reviewerId, SubmitManagerReviewDto dto, CancellationToken ct = default);
}

// src/PeopleCore.Application/Performance/Interfaces/IPerformanceReviewRepository.cs
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities.Performance;

namespace PeopleCore.Application.Performance.Interfaces;

public interface IPerformanceReviewRepository : IRepository<PerformanceReview>
{
    Task<(IReadOnlyList<PerformanceReview> Items, int TotalCount)> GetPagedAsync(
        Guid? employeeId, Guid? cycleId, int page, int pageSize, CancellationToken ct = default);
}
```

**Step 3: Write failing tests**

```csharp
// tests/PeopleCore.Application.Tests/Performance/PerformanceReviewServiceTests.cs
using FluentAssertions;
using Moq;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Performance.DTOs;
using PeopleCore.Application.Performance.Interfaces;
using PeopleCore.Application.Performance.Services;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Performance;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Performance;

public class PerformanceReviewServiceTests
{
    private readonly Mock<IPerformanceReviewRepository> _reviewRepo = new();
    private readonly Mock<IEmployeeRepository> _employeeRepo = new();
    private readonly PerformanceReviewService _sut;

    public PerformanceReviewServiceTests()
    {
        _sut = new PerformanceReviewService(_reviewRepo.Object, _employeeRepo.Object);
    }

    [Fact]
    public async Task SubmitSelfEvaluationAsync_WhenNotTheEmployee_ThrowsDomainException()
    {
        var review = new PerformanceReview
        {
            Id = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            ReviewCycleId = Guid.NewGuid(),
            ReviewerId = Guid.NewGuid(),
            Status = ReviewStatus.Draft
        };
        _reviewRepo.Setup(r => r.GetByIdAsync(review.Id, default)).ReturnsAsync(review);

        var differentEmployee = Guid.NewGuid();
        var act = () => _sut.SubmitSelfEvaluationAsync(review.Id, differentEmployee,
            new SubmitSelfEvaluationDto(4.5m, "Good work", []));

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("*only submit your own*");
    }

    [Fact]
    public async Task SubmitManagerReviewAsync_WhenNotTheReviewer_ThrowsDomainException()
    {
        var review = new PerformanceReview
        {
            Id = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            ReviewCycleId = Guid.NewGuid(),
            ReviewerId = Guid.NewGuid(),
            Status = ReviewStatus.Submitted,
            SelfEvaluationScore = 4.0m
        };
        _reviewRepo.Setup(r => r.GetByIdAsync(review.Id, default)).ReturnsAsync(review);

        var differentReviewer = Guid.NewGuid();
        var act = () => _sut.SubmitManagerReviewAsync(review.Id, differentReviewer,
            new SubmitManagerReviewDto(4.2m, "Good", []));

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("*assigned reviewer*");
    }

    [Fact]
    public async Task SubmitManagerReviewAsync_WhenSelfEvaluationNotSubmitted_ThrowsDomainException()
    {
        var reviewerId = Guid.NewGuid();
        var review = new PerformanceReview
        {
            Id = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            ReviewCycleId = Guid.NewGuid(),
            ReviewerId = reviewerId,
            Status = ReviewStatus.Draft  // not yet submitted by employee
        };
        _reviewRepo.Setup(r => r.GetByIdAsync(review.Id, default)).ReturnsAsync(review);

        var act = () => _sut.SubmitManagerReviewAsync(review.Id, reviewerId,
            new SubmitManagerReviewDto(4.2m, "Good", []));

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("*self-evaluation*");
    }
}
```

**Step 4: Run failing tests**

```bash
dotnet test tests/PeopleCore.Application.Tests/PeopleCore.Application.Tests.csproj --filter "PerformanceReviewServiceTests"
```
Expected: FAIL.

**Step 5: Implement PerformanceReviewService**

```csharp
// src/PeopleCore.Application/Performance/Services/PerformanceReviewService.cs
using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Performance.DTOs;
using PeopleCore.Application.Performance.Interfaces;
using PeopleCore.Domain.Entities.Performance;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.Application.Performance.Services;

public class PerformanceReviewService : IPerformanceReviewService
{
    private readonly IPerformanceReviewRepository _reviewRepo;
    private readonly IEmployeeRepository _employeeRepo;

    public PerformanceReviewService(IPerformanceReviewRepository reviewRepo, IEmployeeRepository employeeRepo)
    {
        _reviewRepo = reviewRepo;
        _employeeRepo = employeeRepo;
    }

    public async Task<PagedResult<PerformanceReviewDto>> GetAllAsync(
        Guid? employeeId, Guid? cycleId, int page, int pageSize, CancellationToken ct = default)
    {
        var (items, total) = await _reviewRepo.GetPagedAsync(employeeId, cycleId, page, pageSize, ct);
        return PagedResult<PerformanceReviewDto>.Create(items.Select(ToDto).ToList(), total, page, pageSize);
    }

    public async Task<PerformanceReviewDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var review = await _reviewRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Performance review {id} not found.");
        return ToDto(review);
    }

    public async Task<PerformanceReviewDto> CreateAsync(CreatePerformanceReviewDto dto, CancellationToken ct = default)
    {
        var review = new PerformanceReview
        {
            EmployeeId = dto.EmployeeId,
            ReviewCycleId = dto.ReviewCycleId,
            ReviewerId = dto.ReviewerId,
            Status = ReviewStatus.Draft,
            KpiItems = dto.KpiItems.Select(k => new KpiItem
            {
                Description = k.Description,
                Target = k.Target,
                Weight = k.Weight
            }).ToList()
        };
        var created = await _reviewRepo.AddAsync(review, ct);
        return ToDto(created);
    }

    public async Task<PerformanceReviewDto> SubmitSelfEvaluationAsync(
        Guid id, Guid employeeId, SubmitSelfEvaluationDto dto, CancellationToken ct = default)
    {
        var review = await _reviewRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Review {id} not found.");

        if (review.EmployeeId != employeeId)
            throw new DomainException("Employees can only submit their own self-evaluation.");

        if (review.Status != ReviewStatus.Draft)
            throw new DomainException("Self-evaluation has already been submitted.");

        review.SelfEvaluationScore = dto.Score;
        review.SelfEvaluationComments = dto.Comments;
        review.Status = ReviewStatus.Submitted;
        review.SubmittedAt = DateTime.UtcNow;
        review.UpdatedAt = DateTime.UtcNow;

        foreach (var kpiUpdate in dto.KpiItems)
        {
            var kpi = review.KpiItems.FirstOrDefault(k => k.Id == kpiUpdate.Id);
            if (kpi is not null)
            {
                kpi.Actual = kpiUpdate.Actual;
                kpi.Score = kpiUpdate.Score;
                kpi.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _reviewRepo.UpdateAsync(review, ct);
        return ToDto(review);
    }

    public async Task<PerformanceReviewDto> SubmitManagerReviewAsync(
        Guid id, Guid reviewerId, SubmitManagerReviewDto dto, CancellationToken ct = default)
    {
        var review = await _reviewRepo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Review {id} not found.");

        if (review.ReviewerId != reviewerId)
            throw new DomainException("Only the assigned reviewer can submit a manager review.");

        if (review.Status != ReviewStatus.Submitted)
            throw new DomainException("Employee self-evaluation must be submitted before manager review.");

        review.ManagerScore = dto.Score;
        review.ManagerComments = dto.Comments;
        // Final score = average of self + manager (50/50 weighting)
        review.FinalScore = review.SelfEvaluationScore.HasValue
            ? (review.SelfEvaluationScore.Value + dto.Score) / 2
            : dto.Score;
        review.Status = ReviewStatus.Completed;
        review.CompletedAt = DateTime.UtcNow;
        review.UpdatedAt = DateTime.UtcNow;

        foreach (var kpiUpdate in dto.KpiItems)
        {
            var kpi = review.KpiItems.FirstOrDefault(k => k.Id == kpiUpdate.Id);
            if (kpi is not null)
            {
                kpi.Score = kpiUpdate.Score;
                kpi.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _reviewRepo.UpdateAsync(review, ct);
        return ToDto(review);
    }

    private static PerformanceReviewDto ToDto(PerformanceReview r) => new(
        r.Id, r.EmployeeId, r.Employee?.FullName ?? string.Empty,
        r.ReviewCycleId, r.ReviewCycle?.Name ?? string.Empty,
        r.ReviewerId, r.Reviewer?.FullName ?? string.Empty,
        r.SelfEvaluationScore, r.ManagerScore, r.FinalScore,
        r.SelfEvaluationComments, r.ManagerComments,
        r.Status, r.SubmittedAt, r.CompletedAt,
        r.KpiItems.Select(k => new KpiItemDto(k.Id, k.Description, k.Target, k.Actual, k.Weight, k.Score)).ToList());
}
```

**Step 6: Run tests — verify pass**

```bash
dotnet test tests/PeopleCore.Application.Tests/PeopleCore.Application.Tests.csproj --filter "PerformanceReviewServiceTests"
```
Expected: `Passed! - Failed: 0`

**Step 7: Implement ReviewCycleService** (simple CRUD — no special business logic beyond status transitions)

**Step 8: Create Performance Controllers**

```csharp
// src/PeopleCore.API/Controllers/Performance/PerformanceController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Performance.DTOs;
using PeopleCore.Application.Performance.Interfaces;

namespace PeopleCore.API.Controllers.Performance;

[ApiController]
[Route("api")]
[Authorize]
public class PerformanceController : ControllerBase
{
    private readonly IReviewCycleService _cycleService;
    private readonly IPerformanceReviewService _reviewService;
    private readonly ICurrentUserService _currentUser;

    public PerformanceController(
        IReviewCycleService cycleService,
        IPerformanceReviewService reviewService,
        ICurrentUserService currentUser)
    {
        _cycleService = cycleService;
        _reviewService = reviewService;
        _currentUser = currentUser;
    }

    // Review Cycles
    [HttpGet("review-cycles")]
    public async Task<IActionResult> GetCycles([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await _cycleService.GetAllAsync(page, pageSize, ct));

    [HttpPost("review-cycles")]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> CreateCycle([FromBody] CreateReviewCycleDto dto, CancellationToken ct)
        => Ok(await _cycleService.CreateAsync(dto, ct));

    [HttpPut("review-cycles/{id:guid}/close")]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> CloseCycle(Guid id, CancellationToken ct)
        => Ok(await _cycleService.CloseAsync(id, ct));

    // Performance Reviews
    [HttpGet("performance-reviews")]
    public async Task<IActionResult> GetReviews(
        [FromQuery] Guid? employeeId, [FromQuery] Guid? cycleId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await _reviewService.GetAllAsync(employeeId, cycleId, page, pageSize, ct));

    [HttpGet("performance-reviews/{id:guid}")]
    public async Task<IActionResult> GetReview(Guid id, CancellationToken ct)
        => Ok(await _reviewService.GetByIdAsync(id, ct));

    [HttpPost("performance-reviews")]
    [Authorize(Roles = "Admin,HRManager,Manager")]
    public async Task<IActionResult> CreateReview([FromBody] CreatePerformanceReviewDto dto, CancellationToken ct)
        => Ok(await _reviewService.CreateAsync(dto, ct));

    [HttpPost("performance-reviews/{id:guid}/self-evaluation")]
    public async Task<IActionResult> SubmitSelfEvaluation(
        Guid id, [FromBody] SubmitSelfEvaluationDto dto, CancellationToken ct)
    {
        var employeeId = _currentUser.EmployeeId
            ?? throw new UnauthorizedAccessException("Employee ID not found in token.");
        return Ok(await _reviewService.SubmitSelfEvaluationAsync(id, employeeId, dto, ct));
    }

    [HttpPost("performance-reviews/{id:guid}/manager-review")]
    [Authorize(Roles = "Admin,HRManager,Manager")]
    public async Task<IActionResult> SubmitManagerReview(
        Guid id, [FromBody] SubmitManagerReviewDto dto, CancellationToken ct)
    {
        var reviewerId = _currentUser.EmployeeId
            ?? throw new UnauthorizedAccessException("Employee ID not found in token.");
        return Ok(await _reviewService.SubmitManagerReviewAsync(id, reviewerId, dto, ct));
    }
}
```

**Step 9: Run all tests**

```bash
dotnet test PeopleCore.sln
```
Expected: All tests pass.

**Step 10: Commit**

```bash
git add -A
git commit -m "feat: implement performance management module with review workflow"
```

---

**Phase 7 complete.** Continue with [Phase 8 — Payroll Export](2026-03-10-hrms-phase-8-payroll-export.md).
