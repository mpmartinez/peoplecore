# Phase 2: Organization Module

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Implement full CRUD for Departments, Positions, and Teams with service layer, unit tests, and controllers.

**Prereq:** Phase 1 complete.

---

### Task 9: Organization — DTOs and Service Interfaces

**Files:**
- Create: `src/PeopleCore.Application/Organization/DTOs/DepartmentDtos.cs`
- Create: `src/PeopleCore.Application/Organization/DTOs/PositionDtos.cs`
- Create: `src/PeopleCore.Application/Organization/DTOs/TeamDtos.cs`
- Create: `src/PeopleCore.Application/Organization/Interfaces/IDepartmentService.cs`
- Create: `src/PeopleCore.Application/Organization/Interfaces/IPositionService.cs`
- Create: `src/PeopleCore.Application/Organization/Interfaces/ITeamService.cs`

**Step 1: Create DTOs**

```csharp
// src/PeopleCore.Application/Organization/DTOs/DepartmentDtos.cs
namespace PeopleCore.Application.Organization.DTOs;

public record DepartmentDto(
    Guid Id,
    Guid CompanyId,
    Guid? ParentDepartmentId,
    string? ParentDepartmentName,
    string Name,
    string? Code,
    int SubDepartmentCount);

public record CreateDepartmentDto(
    Guid CompanyId,
    Guid? ParentDepartmentId,
    string Name,
    string? Code);

public record UpdateDepartmentDto(
    Guid? ParentDepartmentId,
    string Name,
    string? Code);

// src/PeopleCore.Application/Organization/DTOs/PositionDtos.cs
namespace PeopleCore.Application.Organization.DTOs;

public record PositionDto(Guid Id, Guid DepartmentId, string DepartmentName, string Title, string? Level);
public record CreatePositionDto(Guid DepartmentId, string Title, string? Level);
public record UpdatePositionDto(string Title, string? Level);

// src/PeopleCore.Application/Organization/DTOs/TeamDtos.cs
namespace PeopleCore.Application.Organization.DTOs;

public record TeamDto(Guid Id, Guid DepartmentId, string DepartmentName, string Name);
public record CreateTeamDto(Guid DepartmentId, string Name);
public record UpdateTeamDto(string Name);
```

**Step 2: Create service interfaces**

```csharp
// src/PeopleCore.Application/Organization/Interfaces/IDepartmentService.cs
using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Organization.DTOs;

namespace PeopleCore.Application.Organization.Interfaces;

public interface IDepartmentService
{
    Task<PagedResult<DepartmentDto>> GetAllAsync(int page, int pageSize, CancellationToken ct = default);
    Task<DepartmentDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<DepartmentDto> CreateAsync(CreateDepartmentDto dto, CancellationToken ct = default);
    Task<DepartmentDto> UpdateAsync(Guid id, UpdateDepartmentDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

// src/PeopleCore.Application/Organization/Interfaces/IPositionService.cs
using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Organization.DTOs;

namespace PeopleCore.Application.Organization.Interfaces;

public interface IPositionService
{
    Task<PagedResult<PositionDto>> GetAllAsync(int page, int pageSize, Guid? departmentId = null, CancellationToken ct = default);
    Task<PositionDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PositionDto> CreateAsync(CreatePositionDto dto, CancellationToken ct = default);
    Task<PositionDto> UpdateAsync(Guid id, UpdatePositionDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

// src/PeopleCore.Application/Organization/Interfaces/ITeamService.cs
using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Organization.DTOs;

namespace PeopleCore.Application.Organization.Interfaces;

public interface ITeamService
{
    Task<PagedResult<TeamDto>> GetAllAsync(int page, int pageSize, Guid? departmentId = null, CancellationToken ct = default);
    Task<TeamDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<TeamDto> CreateAsync(CreateTeamDto dto, CancellationToken ct = default);
    Task<TeamDto> UpdateAsync(Guid id, UpdateTeamDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
```

**Step 3: Build**

```bash
dotnet build src/PeopleCore.Application/PeopleCore.Application.csproj
```

**Step 4: Commit**

```bash
git add -A
git commit -m "feat: add organization DTOs and service interfaces"
```

---

### Task 10: Organization — Service Implementations and Unit Tests

**Files:**
- Create: `src/PeopleCore.Application/Organization/Interfaces/IDepartmentRepository.cs`
- Create: `src/PeopleCore.Application/Organization/Services/DepartmentService.cs`
- Create: `src/PeopleCore.Infrastructure/Persistence/Repositories/DepartmentRepository.cs`
- Create: `tests/PeopleCore.Application.Tests/Organization/DepartmentServiceTests.cs`

**Step 1: Write the failing test first (TDD)**

```csharp
// tests/PeopleCore.Application.Tests/Organization/DepartmentServiceTests.cs
using FluentAssertions;
using Moq;
using PeopleCore.Application.Organization.DTOs;
using PeopleCore.Application.Organization.Interfaces;
using PeopleCore.Application.Organization.Services;
using PeopleCore.Domain.Entities.Organization;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Organization;

public class DepartmentServiceTests
{
    private readonly Mock<IDepartmentRepository> _repo = new();
    private readonly DepartmentService _sut;

    public DepartmentServiceTests()
    {
        _sut = new DepartmentService(_repo.Object);
    }

    [Fact]
    public async Task GetByIdAsync_WhenDepartmentNotFound_ThrowsKeyNotFoundException()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default))
             .ReturnsAsync((Department?)null);

        var act = () => _sut.GetByIdAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<KeyNotFoundException>()
                 .WithMessage("*not found*");
    }

    [Fact]
    public async Task CreateAsync_WithValidData_ReturnsDepartmentDto()
    {
        var dto = new CreateDepartmentDto(Guid.NewGuid(), null, "Engineering", "ENG");
        var created = new Department { Id = Guid.NewGuid(), CompanyId = dto.CompanyId, Name = dto.Name, Code = dto.Code };

        _repo.Setup(r => r.AddAsync(It.IsAny<Department>(), default))
             .ReturnsAsync(created);

        var result = await _sut.CreateAsync(dto);

        result.Should().NotBeNull();
        result.Name.Should().Be("Engineering");
        result.Code.Should().Be("ENG");
    }

    [Fact]
    public async Task DeleteAsync_WhenDepartmentHasSubDepartments_ThrowsDomainException()
    {
        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Name = "Parent",
            CompanyId = Guid.NewGuid(),
            SubDepartments = [new Department { Name = "Child", CompanyId = Guid.NewGuid() }]
        };
        _repo.Setup(r => r.GetByIdAsync(dept.Id, default)).ReturnsAsync(dept);

        var act = () => _sut.DeleteAsync(dept.Id);

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("*sub-departments*");
    }
}
```

**Step 2: Run test — verify it fails**

```bash
dotnet test tests/PeopleCore.Application.Tests/PeopleCore.Application.Tests.csproj --filter "DepartmentServiceTests"
```
Expected: FAIL — `DepartmentService` does not exist yet.

**Step 3: Create IDepartmentRepository**

```csharp
// src/PeopleCore.Application/Organization/Interfaces/IDepartmentRepository.cs
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities.Organization;

namespace PeopleCore.Application.Organization.Interfaces;

public interface IDepartmentRepository : IRepository<Department>
{
    Task<(IReadOnlyList<Department> Items, int TotalCount)> GetPagedAsync(int page, int pageSize, CancellationToken ct = default);
}
```

**Step 4: Implement DepartmentService**

```csharp
// src/PeopleCore.Application/Organization/Services/DepartmentService.cs
using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Organization.DTOs;
using PeopleCore.Application.Organization.Interfaces;
using PeopleCore.Domain.Entities.Organization;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.Application.Organization.Services;

public class DepartmentService : IDepartmentService
{
    private readonly IDepartmentRepository _repo;

    public DepartmentService(IDepartmentRepository repo) => _repo = repo;

    public async Task<PagedResult<DepartmentDto>> GetAllAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var (items, total) = await _repo.GetPagedAsync(page, pageSize, ct);
        return PagedResult<DepartmentDto>.Create(items.Select(ToDto).ToList(), total, page, pageSize);
    }

    public async Task<DepartmentDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var dept = await _repo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Department {id} not found.");
        return ToDto(dept);
    }

    public async Task<DepartmentDto> CreateAsync(CreateDepartmentDto dto, CancellationToken ct = default)
    {
        var dept = new Department
        {
            CompanyId = dto.CompanyId,
            ParentDepartmentId = dto.ParentDepartmentId,
            Name = dto.Name,
            Code = dto.Code
        };
        var created = await _repo.AddAsync(dept, ct);
        return ToDto(created);
    }

    public async Task<DepartmentDto> UpdateAsync(Guid id, UpdateDepartmentDto dto, CancellationToken ct = default)
    {
        var dept = await _repo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Department {id} not found.");
        dept.ParentDepartmentId = dto.ParentDepartmentId;
        dept.Name = dto.Name;
        dept.Code = dto.Code;
        dept.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(dept, ct);
        return ToDto(dept);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var dept = await _repo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Department {id} not found.");
        if (dept.SubDepartments.Count > 0)
            throw new DomainException("Cannot delete a department that has sub-departments.");
        await _repo.DeleteAsync(dept, ct);
    }

    private static DepartmentDto ToDto(Department d) => new(
        d.Id, d.CompanyId, d.ParentDepartmentId,
        d.ParentDepartment?.Name, d.Name, d.Code,
        d.SubDepartments.Count);
}
```

**Step 5: Run tests — verify they pass**

```bash
dotnet test tests/PeopleCore.Application.Tests/PeopleCore.Application.Tests.csproj --filter "DepartmentServiceTests"
```
Expected: `Passed! - Failed: 0`

**Step 6: Create DepartmentRepository in Infrastructure**

```csharp
// src/PeopleCore.Infrastructure/Persistence/Repositories/DepartmentRepository.cs
using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Organization.Interfaces;
using PeopleCore.Domain.Entities.Organization;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class DepartmentRepository : Repository<Department>, IDepartmentRepository
{
    public DepartmentRepository(AppDbContext context) : base(context) { }

    public async Task<(IReadOnlyList<Department> Items, int TotalCount)> GetPagedAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        var query = Context.Departments
            .Include(d => d.ParentDepartment)
            .Include(d => d.SubDepartments)
            .OrderBy(d => d.Name);

        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }
}
```

**Step 7: Register services in DI (add to ServiceExtensions.cs)**

```csharp
// Add to src/PeopleCore.API/Extensions/ServiceExtensions.cs in AddInfrastructure:
services.AddScoped<IDepartmentRepository, DepartmentRepository>();
services.AddScoped<IDepartmentService, DepartmentService>();
// (repeat pattern for Position and Team after implementing them)
```

**Step 8: Implement PositionService and TeamService** (follow identical pattern to DepartmentService — same structure, different entity/DTO names)

**Step 9: Commit**

```bash
git add -A
git commit -m "feat: implement organization services with tests"
```

---

### Task 11: Organization — Controllers

**Files:**
- Create: `src/PeopleCore.API/Controllers/Organization/DepartmentsController.cs`
- Create: `src/PeopleCore.API/Controllers/Organization/PositionsController.cs`
- Create: `src/PeopleCore.API/Controllers/Organization/TeamsController.cs`

**Step 1: Create DepartmentsController (pattern for all org controllers)**

```csharp
// src/PeopleCore.API/Controllers/Organization/DepartmentsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeopleCore.Application.Organization.DTOs;
using PeopleCore.Application.Organization.Interfaces;

namespace PeopleCore.API.Controllers.Organization;

[ApiController]
[Route("api/departments")]
[Authorize]
public class DepartmentsController : ControllerBase
{
    private readonly IDepartmentService _service;
    public DepartmentsController(IDepartmentService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await _service.GetAllAsync(page, pageSize, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

    [HttpPost]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> Create([FromBody] CreateDepartmentDto dto, CancellationToken ct)
    {
        var result = await _service.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDepartmentDto dto, CancellationToken ct)
        => Ok(await _service.UpdateAsync(id, dto, ct));

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _service.DeleteAsync(id, ct);
        return NoContent();
    }
}
```

**Step 2: Create PositionsController and TeamsController** following the same pattern.

**Step 3: Build and run**

```bash
dotnet build PeopleCore.sln
dotnet run --project src/PeopleCore.API
```
Expected: API starts on https://localhost:5001

**Step 4: Commit**

```bash
git add -A
git commit -m "feat: add organization controllers (departments, positions, teams)"
```

---

**Phase 2 complete.** Continue with [Phase 3 — Employees](2026-03-10-hrms-phase-3-employees.md).
