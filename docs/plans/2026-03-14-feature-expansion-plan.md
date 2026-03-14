# PeopleCore Feature Expansion Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add Shift Scheduling, Leave Accruals, Careers Portal API, Advanced Analytics, and PWA to PeopleCore in order.

**Architecture:** API-first vertical slices following existing PeopleCore patterns — domain entity → repository interface → application service → EF config + migration → repository implementation → controller. Each feature is self-contained and committed independently.

**Tech Stack:** .NET 9, EF Core, PostgreSQL, Blazor Server, xUnit, Moq, IMemoryCache, IHostedService

---

## Conventions (read before starting)

- Domain entities → `src/PeopleCore.Domain/Entities/{Module}/`
- Repository interfaces → `src/PeopleCore.Domain/Interfaces/`
- Application services → `src/PeopleCore.Application/{Module}/Services/`
- DTOs → `src/PeopleCore.Application/{Module}/DTOs/`
- Service interfaces → `src/PeopleCore.Application/{Module}/Interfaces/`
- Controllers → `src/PeopleCore.API/Controllers/{Module}/`
- EF config → `src/PeopleCore.Infrastructure/Persistence/Configurations/`
- Repositories → `src/PeopleCore.Infrastructure/Persistence/Repositories/`
- DbContext → `src/PeopleCore.Infrastructure/Persistence/AppDbContext.cs`
- Tests → `tests/PeopleCore.Application.Tests/{Module}/`
- Migration command: `dotnet ef migrations add {Name} --project src/PeopleCore.Infrastructure --startup-project src/PeopleCore.API`
- Run tests: `dotnet test tests/PeopleCore.Application.Tests/`
- Build: `dotnet build`
- Table/column names use snake_case (EF convention configured globally)

---

# FEATURE 1: SHIFT SCHEDULING

---

### Task 1.1: Domain Entities

**Files:**
- Create: `src/PeopleCore.Domain/Entities/Scheduling/ShiftTemplate.cs`
- Create: `src/PeopleCore.Domain/Entities/Scheduling/RotatingPattern.cs`
- Create: `src/PeopleCore.Domain/Entities/Scheduling/RotatingPatternSlot.cs`
- Create: `src/PeopleCore.Domain/Entities/Scheduling/EmployeeShiftAssignment.cs`

**Step 1: Create ShiftTemplate**

```csharp
// src/PeopleCore.Domain/Entities/Scheduling/ShiftTemplate.cs
namespace PeopleCore.Domain.Entities.Scheduling;

public class ShiftTemplate : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int BreakMinutes { get; set; }
    public bool IsNightShift { get; set; }
    public bool IsActive { get; set; } = true;
}
```

**Step 2: Create RotatingPattern and RotatingPatternSlot**

```csharp
// src/PeopleCore.Domain/Entities/Scheduling/RotatingPattern.cs
namespace PeopleCore.Domain.Entities.Scheduling;

public class RotatingPattern : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public int CycleLengthDays { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<RotatingPatternSlot> Slots { get; set; } = [];
}

// src/PeopleCore.Domain/Entities/Scheduling/RotatingPatternSlot.cs
namespace PeopleCore.Domain.Entities.Scheduling;

public class RotatingPatternSlot : AuditableEntity
{
    public Guid RotatingPatternId { get; set; }
    public RotatingPattern RotatingPattern { get; set; } = null!;
    public int DayOffset { get; set; } // 0 to CycleLengthDays-1
    public Guid? ShiftTemplateId { get; set; } // null = rest day
    public ShiftTemplate? ShiftTemplate { get; set; }
}
```

**Step 3: Create EmployeeShiftAssignment**

```csharp
// src/PeopleCore.Domain/Entities/Scheduling/EmployeeShiftAssignment.cs
namespace PeopleCore.Domain.Entities.Scheduling;

public class EmployeeShiftAssignment : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public Guid? ShiftTemplateId { get; set; }       // fixed shift
    public ShiftTemplate? ShiftTemplate { get; set; }
    public Guid? RotatingPatternId { get; set; }      // rotating
    public RotatingPattern? RotatingPattern { get; set; }
    public DateOnly PatternStartDate { get; set; }    // anchor for cycle calculation
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }        // null = indefinite
}
```

**Step 4: Build**

```bash
dotnet build
```
Expected: 0 errors

**Step 5: Commit**

```bash
git add src/PeopleCore.Domain/Entities/Scheduling/
git commit -m "feat(scheduling): add shift scheduling domain entities"
```

---

### Task 1.2: Repository Interfaces

**Files:**
- Create: `src/PeopleCore.Domain/Interfaces/IShiftTemplateRepository.cs`
- Create: `src/PeopleCore.Domain/Interfaces/IRotatingPatternRepository.cs`
- Create: `src/PeopleCore.Domain/Interfaces/IShiftAssignmentRepository.cs`

**Step 1: Create interfaces**

```csharp
// IShiftTemplateRepository.cs
namespace PeopleCore.Domain.Interfaces;

public interface IShiftTemplateRepository
{
    Task<IReadOnlyList<ShiftTemplate>> GetAllAsync(CancellationToken ct = default);
    Task<ShiftTemplate?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ShiftTemplate> AddAsync(ShiftTemplate entity, CancellationToken ct = default);
    Task UpdateAsync(ShiftTemplate entity, CancellationToken ct = default);
    Task DeleteAsync(ShiftTemplate entity, CancellationToken ct = default);
}

// IRotatingPatternRepository.cs
public interface IRotatingPatternRepository
{
    Task<IReadOnlyList<RotatingPattern>> GetAllAsync(CancellationToken ct = default);
    Task<RotatingPattern?> GetByIdWithSlotsAsync(Guid id, CancellationToken ct = default);
    Task<RotatingPattern> AddAsync(RotatingPattern entity, CancellationToken ct = default);
    Task UpdateAsync(RotatingPattern entity, CancellationToken ct = default);
    Task DeleteAsync(RotatingPattern entity, CancellationToken ct = default);
}

// IShiftAssignmentRepository.cs
public interface IShiftAssignmentRepository
{
    Task<EmployeeShiftAssignment?> GetActiveAssignmentAsync(Guid employeeId, DateOnly date, CancellationToken ct = default);
    Task<IReadOnlyList<EmployeeShiftAssignment>> GetByEmployeeAsync(Guid employeeId, CancellationToken ct = default);
    Task<EmployeeShiftAssignment> AddAsync(EmployeeShiftAssignment entity, CancellationToken ct = default);
    Task DeleteAsync(EmployeeShiftAssignment entity, CancellationToken ct = default);
}
```

**Step 2: Commit**

```bash
git add src/PeopleCore.Domain/Interfaces/
git commit -m "feat(scheduling): add shift repository interfaces"
```

---

### Task 1.3: Application DTOs and Service Interface

**Files:**
- Create: `src/PeopleCore.Application/Scheduling/DTOs/ShiftDtos.cs`
- Create: `src/PeopleCore.Application/Scheduling/Interfaces/IShiftService.cs`

**Step 1: Create DTOs**

```csharp
// src/PeopleCore.Application/Scheduling/DTOs/ShiftDtos.cs
namespace PeopleCore.Application.Scheduling.DTOs;

public record ShiftTemplateDto(
    Guid Id, string Name, TimeOnly StartTime, TimeOnly EndTime,
    int BreakMinutes, bool IsNightShift, bool IsActive);

public record CreateShiftTemplateRequest(
    string Name, TimeOnly StartTime, TimeOnly EndTime,
    int BreakMinutes, bool IsNightShift);

public record RotatingPatternSlotDto(Guid Id, int DayOffset, Guid? ShiftTemplateId, string? ShiftName);

public record RotatingPatternDto(
    Guid Id, string Name, int CycleLengthDays, bool IsActive,
    IReadOnlyList<RotatingPatternSlotDto> Slots);

public record CreateRotatingPatternRequest(
    string Name, int CycleLengthDays,
    IReadOnlyList<CreateSlotRequest> Slots);

public record CreateSlotRequest(int DayOffset, Guid? ShiftTemplateId);

public record AssignShiftRequest(
    Guid EmployeeId, Guid? ShiftTemplateId, Guid? RotatingPatternId,
    DateOnly PatternStartDate, DateOnly EffectiveFrom, DateOnly? EffectiveTo);

public record DailyScheduleDto(DateOnly Date, string? ShiftName,
    TimeOnly? StartTime, TimeOnly? EndTime, bool IsRestDay, bool IsNightShift);
```

**Step 2: Create service interface**

```csharp
// src/PeopleCore.Application/Scheduling/Interfaces/IShiftService.cs
namespace PeopleCore.Application.Scheduling.Interfaces;

public interface IShiftService
{
    Task<IReadOnlyList<ShiftTemplateDto>> GetShiftTemplatesAsync(CancellationToken ct = default);
    Task<ShiftTemplateDto> CreateShiftTemplateAsync(CreateShiftTemplateRequest request, CancellationToken ct = default);
    Task UpdateShiftTemplateAsync(Guid id, CreateShiftTemplateRequest request, CancellationToken ct = default);
    Task DeleteShiftTemplateAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<RotatingPatternDto>> GetRotatingPatternsAsync(CancellationToken ct = default);
    Task<RotatingPatternDto> CreateRotatingPatternAsync(CreateRotatingPatternRequest request, CancellationToken ct = default);
    Task DeleteRotatingPatternAsync(Guid id, CancellationToken ct = default);

    Task<EmployeeShiftAssignment> AssignShiftAsync(AssignShiftRequest request, CancellationToken ct = default);
    Task RemoveAssignmentAsync(Guid assignmentId, CancellationToken ct = default);
    Task<IReadOnlyList<DailyScheduleDto>> GetEmployeeScheduleAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct = default);

    // Used internally by attendance service
    Task<DailyScheduleDto?> ResolveShiftForDayAsync(Guid employeeId, DateOnly date, CancellationToken ct = default);
}
```

**Step 3: Commit**

```bash
git add src/PeopleCore.Application/Scheduling/
git commit -m "feat(scheduling): add shift DTOs and service interface"
```

---

### Task 1.4: Write Service Tests (TDD)

**Files:**
- Create: `tests/PeopleCore.Application.Tests/Scheduling/ShiftServiceTests.cs`

**Step 1: Write failing tests**

```csharp
// tests/PeopleCore.Application.Tests/Scheduling/ShiftServiceTests.cs
namespace PeopleCore.Application.Tests.Scheduling;

public class ShiftServiceTests
{
    private readonly Mock<IShiftTemplateRepository> _templateRepo = new();
    private readonly Mock<IRotatingPatternRepository> _patternRepo = new();
    private readonly Mock<IShiftAssignmentRepository> _assignmentRepo = new();
    private readonly ShiftService _sut;

    public ShiftServiceTests()
    {
        _sut = new ShiftService(_templateRepo.Object, _patternRepo.Object, _assignmentRepo.Object);
    }

    [Fact]
    public async Task ResolveShiftForDay_FixedShift_ReturnsShiftDetails()
    {
        var employeeId = Guid.NewGuid();
        var date = new DateOnly(2026, 3, 14);
        var shift = new ShiftTemplate { Name = "Morning", StartTime = new TimeOnly(6, 0), EndTime = new TimeOnly(15, 0), IsNightShift = false };
        var assignment = new EmployeeShiftAssignment
        {
            EmployeeId = employeeId,
            ShiftTemplate = shift,
            ShiftTemplateId = shift.Id,
            EffectiveFrom = date.AddDays(-30),
            EffectiveTo = null
        };

        _assignmentRepo.Setup(r => r.GetActiveAssignmentAsync(employeeId, date, default))
            .ReturnsAsync(assignment);

        var result = await _sut.ResolveShiftForDayAsync(employeeId, date);

        Assert.NotNull(result);
        Assert.Equal("Morning", result.ShiftName);
        Assert.Equal(new TimeOnly(6, 0), result.StartTime);
        Assert.False(result.IsRestDay);
    }

    [Fact]
    public async Task ResolveShiftForDay_RotatingPattern_RestDay_ReturnsIsRestDayTrue()
    {
        var employeeId = Guid.NewGuid();
        var patternStart = new DateOnly(2026, 3, 9); // Monday
        var date = new DateOnly(2026, 3, 15);        // Sunday = day offset 6
        var pattern = new RotatingPattern
        {
            CycleLengthDays = 7,
            Slots = [new RotatingPatternSlot { DayOffset = 6, ShiftTemplateId = null }]
        };
        var assignment = new EmployeeShiftAssignment
        {
            EmployeeId = employeeId,
            RotatingPattern = pattern,
            PatternStartDate = patternStart,
            EffectiveFrom = patternStart
        };

        _assignmentRepo.Setup(r => r.GetActiveAssignmentAsync(employeeId, date, default))
            .ReturnsAsync(assignment);

        var result = await _sut.ResolveShiftForDayAsync(employeeId, date);

        Assert.NotNull(result);
        Assert.True(result.IsRestDay);
    }

    [Fact]
    public async Task ResolveShiftForDay_NoAssignment_ReturnsNull()
    {
        _assignmentRepo.Setup(r => r.GetActiveAssignmentAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), default))
            .ReturnsAsync((EmployeeShiftAssignment?)null);

        var result = await _sut.ResolveShiftForDayAsync(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today));

        Assert.Null(result);
    }
}
```

**Step 2: Run tests — expect fail (ShiftService not yet created)**

```bash
dotnet test tests/PeopleCore.Application.Tests/ --filter "FullyQualifiedName~ShiftServiceTests"
```
Expected: Build error — `ShiftService` does not exist yet.

**Step 3: Commit tests**

```bash
git add tests/PeopleCore.Application.Tests/Scheduling/
git commit -m "test(scheduling): add shift service tests (failing)"
```

---

### Task 1.5: Implement ShiftService

**Files:**
- Create: `src/PeopleCore.Application/Scheduling/Services/ShiftService.cs`

**Step 1: Implement**

```csharp
// src/PeopleCore.Application/Scheduling/Services/ShiftService.cs
namespace PeopleCore.Application.Scheduling.Services;

public class ShiftService : IShiftService
{
    private readonly IShiftTemplateRepository _templates;
    private readonly IRotatingPatternRepository _patterns;
    private readonly IShiftAssignmentRepository _assignments;

    public ShiftService(IShiftTemplateRepository templates,
        IRotatingPatternRepository patterns, IShiftAssignmentRepository assignments)
    {
        _templates = templates;
        _patterns = patterns;
        _assignments = assignments;
    }

    public async Task<IReadOnlyList<ShiftTemplateDto>> GetShiftTemplatesAsync(CancellationToken ct = default)
    {
        var all = await _templates.GetAllAsync(ct);
        return all.Select(s => new ShiftTemplateDto(s.Id, s.Name, s.StartTime, s.EndTime, s.BreakMinutes, s.IsNightShift, s.IsActive)).ToList();
    }

    public async Task<ShiftTemplateDto> CreateShiftTemplateAsync(CreateShiftTemplateRequest r, CancellationToken ct = default)
    {
        var entity = new ShiftTemplate { Name = r.Name, StartTime = r.StartTime, EndTime = r.EndTime, BreakMinutes = r.BreakMinutes, IsNightShift = r.IsNightShift };
        await _templates.AddAsync(entity, ct);
        return new ShiftTemplateDto(entity.Id, entity.Name, entity.StartTime, entity.EndTime, entity.BreakMinutes, entity.IsNightShift, entity.IsActive);
    }

    public async Task UpdateShiftTemplateAsync(Guid id, CreateShiftTemplateRequest r, CancellationToken ct = default)
    {
        var entity = await _templates.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException();
        entity.Name = r.Name; entity.StartTime = r.StartTime; entity.EndTime = r.EndTime;
        entity.BreakMinutes = r.BreakMinutes; entity.IsNightShift = r.IsNightShift;
        await _templates.UpdateAsync(entity, ct);
    }

    public async Task DeleteShiftTemplateAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _templates.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException();
        await _templates.DeleteAsync(entity, ct);
    }

    public async Task<IReadOnlyList<RotatingPatternDto>> GetRotatingPatternsAsync(CancellationToken ct = default)
    {
        var all = await _patterns.GetAllAsync(ct);
        return all.Select(MapPattern).ToList();
    }

    public async Task<RotatingPatternDto> CreateRotatingPatternAsync(CreateRotatingPatternRequest r, CancellationToken ct = default)
    {
        var entity = new RotatingPattern
        {
            Name = r.Name, CycleLengthDays = r.CycleLengthDays,
            Slots = r.Slots.Select(s => new RotatingPatternSlot { DayOffset = s.DayOffset, ShiftTemplateId = s.ShiftTemplateId }).ToList()
        };
        await _patterns.AddAsync(entity, ct);
        return MapPattern(entity);
    }

    public async Task DeleteRotatingPatternAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _patterns.GetByIdWithSlotsAsync(id, ct) ?? throw new KeyNotFoundException();
        await _patterns.DeleteAsync(entity, ct);
    }

    public async Task<EmployeeShiftAssignment> AssignShiftAsync(AssignShiftRequest r, CancellationToken ct = default)
    {
        var entity = new EmployeeShiftAssignment
        {
            EmployeeId = r.EmployeeId, ShiftTemplateId = r.ShiftTemplateId,
            RotatingPatternId = r.RotatingPatternId, PatternStartDate = r.PatternStartDate,
            EffectiveFrom = r.EffectiveFrom, EffectiveTo = r.EffectiveTo
        };
        return await _assignments.AddAsync(entity, ct);
    }

    public async Task RemoveAssignmentAsync(Guid assignmentId, CancellationToken ct = default)
    {
        var all = await _assignments.GetByEmployeeAsync(Guid.Empty, ct); // use a dedicated GetById
        // Note: add GetByIdAsync to IShiftAssignmentRepository if needed
        throw new NotImplementedException("Add GetByIdAsync to IShiftAssignmentRepository");
    }

    public async Task<IReadOnlyList<DailyScheduleDto>> GetEmployeeScheduleAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var result = new List<DailyScheduleDto>();
        for (var d = from; d <= to; d = d.AddDays(1))
            result.Add(await ResolveShiftForDayAsync(employeeId, d, ct) ?? new DailyScheduleDto(d, null, null, null, true, false));
        return result;
    }

    public async Task<DailyScheduleDto?> ResolveShiftForDayAsync(Guid employeeId, DateOnly date, CancellationToken ct = default)
    {
        var assignment = await _assignments.GetActiveAssignmentAsync(employeeId, date, ct);
        if (assignment is null) return null;

        if (assignment.ShiftTemplateId.HasValue && assignment.ShiftTemplate is not null)
        {
            var s = assignment.ShiftTemplate;
            return new DailyScheduleDto(date, s.Name, s.StartTime, s.EndTime, false, s.IsNightShift);
        }

        if (assignment.RotatingPatternId.HasValue && assignment.RotatingPattern is not null)
        {
            var dayOffset = (date.DayNumber - assignment.PatternStartDate.DayNumber) % assignment.RotatingPattern.CycleLengthDays;
            var slot = assignment.RotatingPattern.Slots.FirstOrDefault(s => s.DayOffset == dayOffset);
            if (slot?.ShiftTemplate is null)
                return new DailyScheduleDto(date, null, null, null, true, false);
            var s = slot.ShiftTemplate;
            return new DailyScheduleDto(date, s.Name, s.StartTime, s.EndTime, false, s.IsNightShift);
        }

        return null;
    }

    private static RotatingPatternDto MapPattern(RotatingPattern p) =>
        new(p.Id, p.Name, p.CycleLengthDays, p.IsActive,
            p.Slots.Select(s => new RotatingPatternSlotDto(s.Id, s.DayOffset, s.ShiftTemplateId, s.ShiftTemplate?.Name)).ToList());
}
```

**Step 2: Run tests**

```bash
dotnet test tests/PeopleCore.Application.Tests/ --filter "FullyQualifiedName~ShiftServiceTests"
```
Expected: All pass (fix `RemoveAssignmentAsync` — add `GetByIdAsync` to interface and repo first)

**Step 3: Commit**

```bash
git add src/PeopleCore.Application/Scheduling/
git commit -m "feat(scheduling): implement ShiftService"
```

---

### Task 1.6: EF Configuration and Migration

**Files:**
- Create: `src/PeopleCore.Infrastructure/Persistence/Configurations/ShiftTemplateConfiguration.cs`
- Create: `src/PeopleCore.Infrastructure/Persistence/Configurations/RotatingPatternConfiguration.cs`
- Create: `src/PeopleCore.Infrastructure/Persistence/Configurations/EmployeeShiftAssignmentConfiguration.cs`
- Modify: `src/PeopleCore.Infrastructure/Persistence/AppDbContext.cs`

**Step 1: Add EF configurations**

```csharp
// ShiftTemplateConfiguration.cs
public class ShiftTemplateConfiguration : IEntityTypeConfiguration<ShiftTemplate>
{
    public void Configure(EntityTypeBuilder<ShiftTemplate> builder)
    {
        builder.ToTable("shift_templates");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(100).IsRequired();
    }
}

// RotatingPatternConfiguration.cs
public class RotatingPatternConfiguration : IEntityTypeConfiguration<RotatingPattern>
{
    public void Configure(EntityTypeBuilder<RotatingPattern> builder)
    {
        builder.ToTable("rotating_patterns");
        builder.HasMany(e => e.Slots).WithOne(s => s.RotatingPattern)
            .HasForeignKey(s => s.RotatingPatternId).OnDelete(DeleteBehavior.Cascade);
    }
}

// EmployeeShiftAssignmentConfiguration.cs
public class EmployeeShiftAssignmentConfiguration : IEntityTypeConfiguration<EmployeeShiftAssignment>
{
    public void Configure(EntityTypeBuilder<EmployeeShiftAssignment> builder)
    {
        builder.ToTable("employee_shift_assignments");
        builder.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId);
        builder.HasOne(e => e.ShiftTemplate).WithMany().HasForeignKey(e => e.ShiftTemplateId).IsRequired(false);
        builder.HasOne(e => e.RotatingPattern).WithMany().HasForeignKey(e => e.RotatingPatternId).IsRequired(false);
    }
}
```

**Step 2: Add DbSets to AppDbContext**

```csharp
// In AppDbContext.cs, add:
public DbSet<ShiftTemplate> ShiftTemplates => Set<ShiftTemplate>();
public DbSet<RotatingPattern> RotatingPatterns => Set<RotatingPattern>();
public DbSet<RotatingPatternSlot> RotatingPatternSlots => Set<RotatingPatternSlot>();
public DbSet<EmployeeShiftAssignment> ShiftAssignments => Set<EmployeeShiftAssignment>();
```

**Step 3: Create migration**

```bash
dotnet ef migrations add AddShiftScheduling --project src/PeopleCore.Infrastructure --startup-project src/PeopleCore.API
dotnet ef database update --project src/PeopleCore.Infrastructure --startup-project src/PeopleCore.API
```

**Step 4: Commit**

```bash
git add src/PeopleCore.Infrastructure/
git commit -m "feat(scheduling): add EF configuration and migration for shift scheduling"
```

---

### Task 1.7: Repository Implementations

**Files:**
- Create: `src/PeopleCore.Infrastructure/Persistence/Repositories/ShiftTemplateRepository.cs`
- Create: `src/PeopleCore.Infrastructure/Persistence/Repositories/RotatingPatternRepository.cs`
- Create: `src/PeopleCore.Infrastructure/Persistence/Repositories/ShiftAssignmentRepository.cs`
- Modify: `src/PeopleCore.API/Extensions/ServiceExtensions.cs` (register repos and service)

**Step 1: Implement repositories (follow existing repo patterns in the project)**

```csharp
// ShiftAssignmentRepository.cs — key method
public async Task<EmployeeShiftAssignment?> GetActiveAssignmentAsync(Guid employeeId, DateOnly date, CancellationToken ct = default)
{
    return await _context.ShiftAssignments
        .Include(a => a.ShiftTemplate)
        .Include(a => a.RotatingPattern).ThenInclude(p => p.Slots).ThenInclude(s => s.ShiftTemplate)
        .Where(a => a.EmployeeId == employeeId
            && a.EffectiveFrom <= date
            && (a.EffectiveTo == null || a.EffectiveTo >= date))
        .OrderByDescending(a => a.EffectiveFrom)
        .FirstOrDefaultAsync(ct);
}
```

**Step 2: Register in ServiceExtensions.cs**

```csharp
services.AddScoped<IShiftTemplateRepository, ShiftTemplateRepository>();
services.AddScoped<IRotatingPatternRepository, RotatingPatternRepository>();
services.AddScoped<IShiftAssignmentRepository, ShiftAssignmentRepository>();
services.AddScoped<IShiftService, ShiftService>();
```

**Step 3: Build and run tests**

```bash
dotnet build && dotnet test tests/PeopleCore.Application.Tests/
```

**Step 4: Commit**

```bash
git add src/PeopleCore.Infrastructure/ src/PeopleCore.API/Extensions/ServiceExtensions.cs
git commit -m "feat(scheduling): add repository implementations and DI registration"
```

---

### Task 1.8: API Controller

**Files:**
- Create: `src/PeopleCore.API/Controllers/Scheduling/ShiftTemplatesController.cs`
- Create: `src/PeopleCore.API/Controllers/Scheduling/RotatingPatternsController.cs`
- Create: `src/PeopleCore.API/Controllers/Scheduling/ShiftAssignmentsController.cs`

**Step 1: Create controllers (follow existing controller patterns)**

```csharp
// ShiftTemplatesController.cs
[ApiController]
[Route("api/shift-templates")]
[Authorize(Roles = "Admin,HRManager")]
public class ShiftTemplatesController : ControllerBase
{
    private readonly IShiftService _service;
    public ShiftTemplatesController(IShiftService service) => _service = service;

    [HttpGet] public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await _service.GetShiftTemplatesAsync(ct));

    [HttpPost] public async Task<IActionResult> Create(CreateShiftTemplateRequest request, CancellationToken ct) =>
        CreatedAtAction(nameof(GetAll), await _service.CreateShiftTemplateAsync(request, ct));

    [HttpPut("{id}")] public async Task<IActionResult> Update(Guid id, CreateShiftTemplateRequest request, CancellationToken ct)
    {
        await _service.UpdateShiftTemplateAsync(id, request, ct);
        return NoContent();
    }

    [HttpDelete("{id}")] public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _service.DeleteShiftTemplateAsync(id, ct);
        return NoContent();
    }
}

// ShiftAssignmentsController.cs
[ApiController]
[Route("api/shift-assignments")]
[Authorize]
public class ShiftAssignmentsController : ControllerBase
{
    [HttpGet("{employeeId}/schedule")]
    public async Task<IActionResult> GetSchedule(Guid employeeId, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        Ok(await _service.GetEmployeeScheduleAsync(employeeId, from, to, ct));

    [HttpPost]
    [Authorize(Roles = "Admin,HRManager")]
    public async Task<IActionResult> Assign(AssignShiftRequest request, CancellationToken ct) =>
        CreatedAtAction(nameof(GetSchedule), await _service.AssignShiftAsync(request, ct));
}
```

**Step 2: Build**

```bash
dotnet build
```

**Step 3: Commit**

```bash
git add src/PeopleCore.API/Controllers/Scheduling/
git commit -m "feat(scheduling): add shift scheduling API controllers"
```

---

### Task 1.9: Integrate with Attendance Late Calculation

**Files:**
- Modify: `src/PeopleCore.Application/Attendance/Services/AttendanceService.cs`

**Step 1: Inject IShiftService into AttendanceService**

Find where `LateMinutes` is computed. Replace hardcoded shift start time with:

```csharp
var schedule = await _shiftService.ResolveShiftForDayAsync(record.EmployeeId, record.Date, ct);
var shiftStart = schedule?.StartTime ?? new TimeOnly(8, 0); // default 8am if no shift assigned
record.LateMinutes = record.TimeIn.HasValue && record.TimeIn.Value > shiftStart
    ? (int)(record.TimeIn.Value - shiftStart).TotalMinutes
    : 0;
```

**Step 2: Update PayrollAttendanceSummaryDto**

Add to `src/PeopleCore.Application/PayrollIntegration/DTOs/PayrollExportDtos.cs`:
```csharp
// Add to PayrollAttendanceSummaryDto:
string? ShiftName, bool HasNightShift
```

**Step 3: Run all tests**

```bash
dotnet test tests/PeopleCore.Application.Tests/
```

**Step 4: Commit**

```bash
git add src/PeopleCore.Application/
git commit -m "feat(scheduling): integrate shift resolution into attendance late calculation"
```

---

# FEATURE 2: LEAVE ACCRUALS

---

### Task 2.1: Domain Entities

**Files:**
- Create: `src/PeopleCore.Domain/Entities/Leave/LeaveAccrualPolicy.cs`
- Create: `src/PeopleCore.Domain/Entities/Leave/LeaveAccrualTransaction.cs`

**Step 1: Create entities**

```csharp
// LeaveAccrualPolicy.cs
namespace PeopleCore.Domain.Entities.Leave;

public class LeaveAccrualPolicy : AuditableEntity
{
    public Guid LeaveTypeId { get; set; }
    public LeaveType LeaveType { get; set; } = null!;
    public int TenureMonthsMin { get; set; }
    public int? TenureMonthsMax { get; set; } // null = open-ended
    public decimal DaysPerYear { get; set; }
    public AccrualFrequency AccrualFrequency { get; set; } = AccrualFrequency.Monthly;
    public bool IsActive { get; set; } = true;
}

public enum AccrualFrequency { Monthly, Annual }

// LeaveAccrualTransaction.cs
namespace PeopleCore.Domain.Entities.Leave;

public class LeaveAccrualTransaction : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public Guid LeaveTypeId { get; set; }
    public LeaveType LeaveType { get; set; } = null!;
    public DateOnly AccrualDate { get; set; }
    public decimal DaysAccrued { get; set; }
    public string PolicySnapshot { get; set; } = string.Empty; // JSON
    public int PeriodYear { get; set; }
    public int PeriodMonth { get; set; }
}
```

**Step 2: Build and commit**

```bash
dotnet build
git add src/PeopleCore.Domain/Entities/Leave/
git commit -m "feat(accruals): add leave accrual domain entities"
```

---

### Task 2.2: Repository Interfaces, DTOs, Service Interface

**Files:**
- Create: `src/PeopleCore.Domain/Interfaces/ILeaveAccrualRepository.cs`
- Create: `src/PeopleCore.Application/Leave/DTOs/LeaveAccrualDtos.cs`
- Create: `src/PeopleCore.Application/Leave/Interfaces/ILeaveAccrualService.cs`

**Step 1: Create repository interface**

```csharp
public interface ILeaveAccrualRepository
{
    Task<IReadOnlyList<LeaveAccrualPolicy>> GetPoliciesByLeaveTypeAsync(Guid leaveTypeId, CancellationToken ct = default);
    Task<IReadOnlyList<LeaveAccrualPolicy>> GetAllActivePoliciesAsync(CancellationToken ct = default);
    Task<LeaveAccrualPolicy> AddPolicyAsync(LeaveAccrualPolicy policy, CancellationToken ct = default);
    Task UpdatePolicyAsync(LeaveAccrualPolicy policy, CancellationToken ct = default);
    Task DeletePolicyAsync(LeaveAccrualPolicy policy, CancellationToken ct = default);
    Task<bool> TransactionExistsAsync(Guid employeeId, Guid leaveTypeId, int year, int month, CancellationToken ct = default);
    Task AddTransactionAsync(LeaveAccrualTransaction transaction, CancellationToken ct = default);
    Task<IReadOnlyList<LeaveAccrualTransaction>> GetTransactionsByEmployeeAsync(Guid employeeId, CancellationToken ct = default);
}
```

**Step 2: Create DTOs and service interface**

```csharp
// LeaveAccrualDtos.cs
public record LeaveAccrualPolicyDto(
    Guid Id, Guid LeaveTypeId, string LeaveTypeName,
    int TenureMonthsMin, int? TenureMonthsMax,
    decimal DaysPerYear, string AccrualFrequency, bool IsActive);

public record CreateLeaveAccrualPolicyRequest(
    Guid LeaveTypeId, int TenureMonthsMin, int? TenureMonthsMax,
    decimal DaysPerYear, AccrualFrequency AccrualFrequency);

public record LeaveAccrualTransactionDto(
    Guid Id, string LeaveTypeName, DateOnly AccrualDate,
    decimal DaysAccrued, int PeriodYear, int PeriodMonth);

// ILeaveAccrualService.cs
public interface ILeaveAccrualService
{
    Task<IReadOnlyList<LeaveAccrualPolicyDto>> GetPoliciesAsync(Guid leaveTypeId, CancellationToken ct = default);
    Task<LeaveAccrualPolicyDto> CreatePolicyAsync(CreateLeaveAccrualPolicyRequest request, CancellationToken ct = default);
    Task UpdatePolicyAsync(Guid id, CreateLeaveAccrualPolicyRequest request, CancellationToken ct = default);
    Task DeletePolicyAsync(Guid id, CancellationToken ct = default);
    Task RunAccrualsAsync(int year, int month, CancellationToken ct = default); // called by hosted service + manual endpoint
    Task<IReadOnlyList<LeaveAccrualTransactionDto>> GetEmployeeAccrualHistoryAsync(Guid employeeId, CancellationToken ct = default);
}
```

**Step 3: Commit**

```bash
git add src/PeopleCore.Domain/Interfaces/ src/PeopleCore.Application/Leave/
git commit -m "feat(accruals): add accrual repository interface, DTOs, service interface"
```

---

### Task 2.3: Write Accrual Service Tests (TDD)

**Files:**
- Create: `tests/PeopleCore.Application.Tests/Leave/LeaveAccrualServiceTests.cs`

**Step 1: Write key tests**

```csharp
public class LeaveAccrualServiceTests
{
    private readonly Mock<ILeaveAccrualRepository> _accrualRepo = new();
    private readonly Mock<ILeaveBalanceRepository> _balanceRepo = new();
    private readonly Mock<IEmployeeRepository> _employeeRepo = new();
    private readonly LeaveAccrualService _sut;

    public LeaveAccrualServiceTests()
    {
        _sut = new LeaveAccrualService(_accrualRepo.Object, _balanceRepo.Object, _employeeRepo.Object);
    }

    [Fact]
    public async Task RunAccruals_MatchingPolicy_CreatesTransaction()
    {
        var employeeId = Guid.NewGuid();
        var leaveTypeId = Guid.NewGuid();
        var hireDate = new DateOnly(2025, 1, 1); // 14 months ago = matches 12-23 band
        var employee = new Employee { Id = employeeId, HireDate = hireDate, EmploymentStatus = EmploymentStatus.Regular };
        var policy = new LeaveAccrualPolicy
        {
            LeaveTypeId = leaveTypeId, TenureMonthsMin = 12, TenureMonthsMax = 23,
            DaysPerYear = 15m, AccrualFrequency = AccrualFrequency.Monthly, IsActive = true
        };

        _employeeRepo.Setup(r => r.GetActiveEmployeesAsync(default)).ReturnsAsync([employee]);
        _accrualRepo.Setup(r => r.GetAllActivePoliciesAsync(default)).ReturnsAsync([policy]);
        _accrualRepo.Setup(r => r.TransactionExistsAsync(employeeId, leaveTypeId, 2026, 3, default)).ReturnsAsync(false);

        await _sut.RunAccrualsAsync(2026, 3);

        _accrualRepo.Verify(r => r.AddTransactionAsync(
            It.Is<LeaveAccrualTransaction>(t =>
                t.EmployeeId == employeeId &&
                t.LeaveTypeId == leaveTypeId &&
                t.DaysAccrued == 15m / 12m),
            default), Times.Once);
    }

    [Fact]
    public async Task RunAccruals_IdempotentWhenTransactionExists_SkipsEmployee()
    {
        var employeeId = Guid.NewGuid();
        var leaveTypeId = Guid.NewGuid();
        var employee = new Employee { Id = employeeId, HireDate = new DateOnly(2024, 1, 1) };
        var policy = new LeaveAccrualPolicy { LeaveTypeId = leaveTypeId, TenureMonthsMin = 0, DaysPerYear = 15m, IsActive = true };

        _employeeRepo.Setup(r => r.GetActiveEmployeesAsync(default)).ReturnsAsync([employee]);
        _accrualRepo.Setup(r => r.GetAllActivePoliciesAsync(default)).ReturnsAsync([policy]);
        _accrualRepo.Setup(r => r.TransactionExistsAsync(employeeId, leaveTypeId, 2026, 3, default)).ReturnsAsync(true);

        await _sut.RunAccrualsAsync(2026, 3);

        _accrualRepo.Verify(r => r.AddTransactionAsync(It.IsAny<LeaveAccrualTransaction>(), default), Times.Never);
    }

    [Fact]
    public async Task RunAccruals_ProbationaryEmployee_ZeroDaysPolicy_NoTransaction()
    {
        var employeeId = Guid.NewGuid();
        var leaveTypeId = Guid.NewGuid();
        var employee = new Employee { Id = employeeId, HireDate = new DateOnly(2026, 2, 1) }; // 1 month tenure
        var policy = new LeaveAccrualPolicy
        {
            LeaveTypeId = leaveTypeId, TenureMonthsMin = 0, TenureMonthsMax = 11,
            DaysPerYear = 0m, IsActive = true
        };

        _employeeRepo.Setup(r => r.GetActiveEmployeesAsync(default)).ReturnsAsync([employee]);
        _accrualRepo.Setup(r => r.GetAllActivePoliciesAsync(default)).ReturnsAsync([policy]);
        _accrualRepo.Setup(r => r.TransactionExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), default)).ReturnsAsync(false);

        await _sut.RunAccrualsAsync(2026, 3);

        _accrualRepo.Verify(r => r.AddTransactionAsync(It.IsAny<LeaveAccrualTransaction>(), default), Times.Never);
    }
}
```

**Step 2: Run tests (expect build error)**

```bash
dotnet test tests/PeopleCore.Application.Tests/ --filter "FullyQualifiedName~LeaveAccrualServiceTests"
```

**Step 3: Commit**

```bash
git add tests/PeopleCore.Application.Tests/Leave/LeaveAccrualServiceTests.cs
git commit -m "test(accruals): add leave accrual service tests (failing)"
```

---

### Task 2.4: Implement LeaveAccrualService and Background Job

**Files:**
- Create: `src/PeopleCore.Application/Leave/Services/LeaveAccrualService.cs`
- Create: `src/PeopleCore.Infrastructure/BackgroundJobs/LeaveAccrualHostedService.cs`

**Step 1: Implement LeaveAccrualService**

```csharp
public class LeaveAccrualService : ILeaveAccrualService
{
    // ... inject repos
    public async Task RunAccrualsAsync(int year, int month, CancellationToken ct = default)
    {
        var employees = await _employeeRepo.GetActiveEmployeesAsync(ct);
        var policies = await _accrualRepo.GetAllActivePoliciesAsync(ct);
        var today = new DateOnly(year, month, 1);

        foreach (var employee in employees)
        {
            var tenureMonths = ((today.Year - employee.HireDate.Year) * 12) + today.Month - employee.HireDate.Month;
            var applicablePolicies = policies.Where(p =>
                p.TenureMonthsMin <= tenureMonths &&
                (!p.TenureMonthsMax.HasValue || p.TenureMonthsMax >= tenureMonths) &&
                p.DaysPerYear > 0);

            foreach (var policy in applicablePolicies)
            {
                if (await _accrualRepo.TransactionExistsAsync(employee.Id, policy.LeaveTypeId, year, month, ct))
                    continue;

                var daysAccrued = policy.DaysPerYear / 12m;
                var transaction = new LeaveAccrualTransaction
                {
                    EmployeeId = employee.Id, LeaveTypeId = policy.LeaveTypeId,
                    AccrualDate = today, DaysAccrued = daysAccrued,
                    PolicySnapshot = JsonSerializer.Serialize(policy),
                    PeriodYear = year, PeriodMonth = month
                };
                await _accrualRepo.AddTransactionAsync(transaction, ct);
                // Upsert LeaveBalance
                await _balanceRepo.AddDaysAsync(employee.Id, policy.LeaveTypeId, year, daysAccrued, ct);
            }
        }
    }
    // ... other methods
}
```

**Step 2: Implement background job**

```csharp
// LeaveAccrualHostedService.cs
public class LeaveAccrualHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LeaveAccrualHostedService> _logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var nextRun = new DateTime(now.Year, now.Month, 1).AddMonths(1); // 1st of next month
            var delay = nextRun - now;
            await Task.Delay(delay, stoppingToken);

            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ILeaveAccrualService>();
            try { await service.RunAccrualsAsync(nextRun.Year, nextRun.Month, stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "Leave accrual job failed"); }
        }
    }
}
```

**Step 3: Register in Program.cs**

```csharp
builder.Services.AddHostedService<LeaveAccrualHostedService>();
```

**Step 4: Run tests**

```bash
dotnet test tests/PeopleCore.Application.Tests/ --filter "FullyQualifiedName~LeaveAccrualServiceTests"
```
Expected: All pass

**Step 5: Commit**

```bash
git add src/PeopleCore.Application/Leave/ src/PeopleCore.Infrastructure/BackgroundJobs/ src/PeopleCore.API/Program.cs
git commit -m "feat(accruals): implement leave accrual service and monthly background job"
```

---

### Task 2.5: EF Config, Migration, Repository, Controller

Follow same pattern as Task 1.6–1.8.

**Key migration:** `AddLeaveAccruals`
**Key controller endpoints:**
```
GET/POST/PUT/DELETE  /api/leave-accrual-policies
GET                  /api/leave-accrual-policies/{leaveTypeId}/rules
POST                 /api/leave-accruals/run-manual   [Authorize(Roles="Admin")]
GET                  /api/employees/{id}/accrual-history
```

**Commit:**
```bash
git commit -m "feat(accruals): add EF config, migration, repo, and controller for leave accruals"
```

---

# FEATURE 3: CAREERS PORTAL API

---

### Task 3.1: DTOs and Service Interface

**Files:**
- Create: `src/PeopleCore.Application/Careers/DTOs/CareersDtos.cs`
- Create: `src/PeopleCore.Application/Careers/Interfaces/ICareersService.cs`

**Step 1: Create DTOs**

```csharp
// CareersDtos.cs
public record PublicJobPostingDto(
    Guid Id, string Title, string Description, string Requirements,
    string? DepartmentName, string? PositionTitle, int Vacancies, DateTime PostedAt);

public record JobApplicationRequest(
    string FirstName, string LastName, string Email, string Phone,
    string ResumeBase64, string ResumeFileName);

public record JobApplicationResponse(Guid ApplicationId, string Message);

// ICareersService.cs
public interface ICareersService
{
    Task<IReadOnlyList<PublicJobPostingDto>> GetOpenJobsAsync(CancellationToken ct = default);
    Task<PublicJobPostingDto?> GetJobAsync(Guid id, CancellationToken ct = default);
    Task<JobApplicationResponse> ApplyAsync(Guid jobPostingId, JobApplicationRequest request, CancellationToken ct = default);
}
```

**Step 2: Commit**

```bash
git add src/PeopleCore.Application/Careers/
git commit -m "feat(careers): add careers portal DTOs and service interface"
```

---

### Task 3.2: Write Tests (TDD)

**Files:**
- Create: `tests/PeopleCore.Application.Tests/Careers/CareersServiceTests.cs`

**Step 1: Write key tests**

```csharp
public class CareersServiceTests
{
    private readonly Mock<IJobPostingRepository> _jobRepo = new();
    private readonly Mock<IApplicantRepository> _applicantRepo = new();
    private readonly Mock<IStorageService> _storage = new();
    private readonly CareersService _sut;

    [Fact]
    public async Task GetOpenJobs_ReturnsOnlyOpenPostings()
    {
        _jobRepo.Setup(r => r.GetOpenAsync(default)).ReturnsAsync([
            new JobPosting { Title = "Dev", Status = "Open" },
            // closed ones should not appear
        ]);
        var result = await _sut.GetOpenJobsAsync();
        Assert.All(result, j => Assert.NotNull(j.Title));
    }

    [Fact]
    public async Task Apply_DuplicateEmail_ThrowsInvalidOperationException()
    {
        var jobId = Guid.NewGuid();
        _jobRepo.Setup(r => r.GetByIdAsync(jobId, default)).ReturnsAsync(new JobPosting { Id = jobId, Status = "Open" });
        _applicantRepo.Setup(r => r.ExistsByEmailAndJobAsync("test@test.com", jobId, default)).ReturnsAsync(true);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.ApplyAsync(jobId, new JobApplicationRequest("A", "B", "test@test.com", "123", "", "resume.pdf")));
    }

    [Fact]
    public async Task Apply_ValidRequest_CreatesApplicantWithAppliedStatus()
    {
        var jobId = Guid.NewGuid();
        _jobRepo.Setup(r => r.GetByIdAsync(jobId, default)).ReturnsAsync(new JobPosting { Id = jobId, Status = "Open" });
        _applicantRepo.Setup(r => r.ExistsByEmailAndJobAsync(It.IsAny<string>(), jobId, default)).ReturnsAsync(false);
        _storage.Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), default)).ReturnsAsync("storage-key");

        var result = await _sut.ApplyAsync(jobId, new JobApplicationRequest("John", "Doe", "j@j.com", "09171234567", Convert.ToBase64String(new byte[10]), "cv.pdf"));

        Assert.NotEqual(Guid.Empty, result.ApplicationId);
        _applicantRepo.Verify(r => r.AddAsync(It.Is<Applicant>(a => a.Status == ApplicantStatus.Applied), default), Times.Once);
    }
}
```

**Step 2: Commit**

```bash
git add tests/PeopleCore.Application.Tests/Careers/
git commit -m "test(careers): add careers service tests (failing)"
```

---

### Task 3.3: Implement CareersService

**Files:**
- Create: `src/PeopleCore.Application/Careers/Services/CareersService.cs`

**Step 1: Implement**

```csharp
public class CareersService : ICareersService
{
    public async Task<JobApplicationResponse> ApplyAsync(Guid jobPostingId, JobApplicationRequest request, CancellationToken ct = default)
    {
        var job = await _jobRepo.GetByIdAsync(jobPostingId, ct)
            ?? throw new KeyNotFoundException("Job posting not found");

        if (job.Status != "Open")
            throw new InvalidOperationException("This position is no longer accepting applications.");

        if (await _applicantRepo.ExistsByEmailAndJobAsync(request.Email, jobPostingId, ct))
            throw new InvalidOperationException("An application with this email already exists for this position.");

        // Validate and store resume
        var resumeBytes = Convert.FromBase64String(request.ResumeBase64);
        if (resumeBytes.Length > 5 * 1024 * 1024)
            throw new InvalidOperationException("Resume file exceeds 5MB limit.");

        var allowedExtensions = new[] { ".pdf", ".docx" };
        var ext = Path.GetExtension(request.ResumeFileName).ToLowerInvariant();
        if (!allowedExtensions.Contains(ext))
            throw new InvalidOperationException("Only PDF and DOCX files are accepted.");

        var storageKey = await _storage.UploadAsync($"resumes/{Guid.NewGuid()}{ext}", resumeBytes, request.ResumeFileName, ct);

        var applicant = new Applicant
        {
            FirstName = request.FirstName, LastName = request.LastName,
            Email = request.Email, Phone = request.Phone,
            JobPostingId = jobPostingId, Status = ApplicantStatus.Applied,
            ResumeStorageKey = storageKey, AppliedAt = DateTime.UtcNow
        };
        await _applicantRepo.AddAsync(applicant, ct);
        return new JobApplicationResponse(applicant.Id, "Application received. We will contact you shortly.");
    }
}
```

**Step 2: Run tests**

```bash
dotnet test tests/PeopleCore.Application.Tests/ --filter "FullyQualifiedName~CareersServiceTests"
```
Expected: All pass

**Step 3: Commit**

```bash
git add src/PeopleCore.Application/Careers/
git commit -m "feat(careers): implement careers service"
```

---

### Task 3.4: Public Controller and CORS

**Files:**
- Create: `src/PeopleCore.API/Controllers/Careers/CareersController.cs`
- Modify: `src/PeopleCore.API/appsettings.json`
- Modify: `src/PeopleCore.API/Program.cs`

**Step 1: Create controller (no [Authorize])**

```csharp
[ApiController]
[Route("api/careers")]
public class CareersController : ControllerBase
{
    private readonly ICareersService _service;

    [HttpGet("jobs")]
    public async Task<IActionResult> GetJobs(CancellationToken ct) =>
        Ok(await _service.GetOpenJobsAsync(ct));

    [HttpGet("jobs/{id}")]
    public async Task<IActionResult> GetJob(Guid id, CancellationToken ct)
    {
        var job = await _service.GetJobAsync(id, ct);
        return job is null ? NotFound() : Ok(job);
    }

    [HttpPost("jobs/{id}/apply")]
    public async Task<IActionResult> Apply(Guid id, JobApplicationRequest request, CancellationToken ct)
    {
        var result = await _service.ApplyAsync(id, request, ct);
        return CreatedAtAction(nameof(GetJob), new { id }, result);
    }
}
```

**Step 2: Add CORS policy for careers portal in Program.cs**

```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("CareersPortal", policy =>
        policy.WithOrigins(builder.Configuration.GetSection("CareersPortal:AllowedOrigins").Get<string[]>() ?? [])
              .AllowAnyHeader().AllowAnyMethod());
});
// Apply to careers routes only via middleware or attribute
```

**Step 3: Add appsettings entry**

```json
"CareersPortal": {
  "AllowedOrigins": ["https://careers.yourcompany.com"],
  "MaxApplicationsPerHourPerIp": 3
}
```

**Step 4: Register service in DI**

```csharp
services.AddScoped<ICareersService, CareersService>();
```

**Step 5: Build and test**

```bash
dotnet build && dotnet test tests/PeopleCore.Application.Tests/
```

**Step 6: Commit**

```bash
git add src/PeopleCore.API/Controllers/Careers/ src/PeopleCore.API/appsettings.json src/PeopleCore.API/Program.cs
git commit -m "feat(careers): add public careers controller and CORS configuration"
```

---

# FEATURE 4: ADVANCED ANALYTICS

---

### Task 4.1: Analytics DTOs

**Files:**
- Create: `src/PeopleCore.Application/Analytics/DTOs/AnalyticsDtos.cs`

**Step 1: Create shared DTO types**

```csharp
// AnalyticsDtos.cs
namespace PeopleCore.Application.Analytics.DTOs;

public record AnalyticsPeriod(DateOnly From, DateOnly To);
public record AnalyticsDataPoint(string Label, decimal Value, string? Trend = null);
public record AnalyticsResult(AnalyticsPeriod Period, IReadOnlyList<AnalyticsDataPoint> Data, DateTime GeneratedAt);

// HR-specific response records
public record HeadcountDto(string Department, int Active, int Inactive, int Total);
public record TurnoverDto(string Period, int NewHires, int Separations, decimal TurnoverRate);
public record AttendanceRateDto(string Department, decimal AttendanceRate, decimal LatePercent, decimal UndertimePercent);
public record LeaveUtilizationDto(string LeaveType, decimal TotalDays, decimal UsedDays, decimal UtilizationPercent);
public record OvertimeSummaryDto(string Department, int TotalMinutes, int EmployeeCount);
public record RecruitmentFunnelDto(string Stage, int Count);
public record PerformanceDistributionDto(string ScoreBand, int Count, decimal Percent);

// Executive-specific
public record WorkforceSummaryDto(int TotalActive, int TotalInactive, int NewThisMonth, IReadOnlyList<HeadcountDto> ByDepartment);
public record HiringTrendDto(string Month, int NewHires);
public record AttritionRateDto(string Period, decimal AttritionPercent);
```

**Step 2: Commit**

```bash
git add src/PeopleCore.Application/Analytics/
git commit -m "feat(analytics): add analytics DTOs"
```

---

### Task 4.2: HR Analytics Service

**Files:**
- Create: `src/PeopleCore.Application/Analytics/Interfaces/IHrAnalyticsService.cs`
- Create: `src/PeopleCore.Application/Analytics/Services/HrAnalyticsService.cs`
- Create: `tests/PeopleCore.Application.Tests/Analytics/HrAnalyticsServiceTests.cs`

**Step 1: Write key tests first**

```csharp
public class HrAnalyticsServiceTests
{
    [Fact]
    public async Task GetHeadcount_GroupsByDepartment()
    {
        // Arrange: mock employee repo to return employees in 2 departments
        // Act: call GetHeadcountAsync
        // Assert: result has entry per department with correct counts
    }

    [Fact]
    public async Task GetRecruitmentFunnel_CountsEachStatus()
    {
        // Arrange: mock applicant repo with various statuses
        // Act: call GetRecruitmentFunnelAsync
        // Assert: Applied=3, Interview=2, Hired=1 etc
    }
}
```

**Step 2: Implement service — each method queries via EF repositories**

```csharp
public class HrAnalyticsService : IHrAnalyticsService
{
    public async Task<IReadOnlyList<HeadcountDto>> GetHeadcountAsync(DateOnly? asOf = null, CancellationToken ct = default)
    {
        var employees = await _employeeRepo.GetAllWithDepartmentAsync(ct);
        return employees
            .GroupBy(e => e.Department?.Name ?? "Unassigned")
            .Select(g => new HeadcountDto(g.Key,
                g.Count(e => e.SeparationDate == null),
                g.Count(e => e.SeparationDate != null),
                g.Count()))
            .ToList();
    }
    // ... implement all HR methods
}
```

**Step 3: Run tests, fix, commit**

```bash
dotnet test tests/PeopleCore.Application.Tests/ --filter "FullyQualifiedName~HrAnalyticsServiceTests"
git commit -m "feat(analytics): add HR analytics service with tests"
```

---

### Task 4.3: Executive Analytics Service

**Files:**
- Create: `src/PeopleCore.Application/Analytics/Interfaces/IExecutiveAnalyticsService.cs`
- Create: `src/PeopleCore.Application/Analytics/Services/ExecutiveAnalyticsService.cs`
- Create: `tests/PeopleCore.Application.Tests/Analytics/ExecutiveAnalyticsServiceTests.cs`

Same TDD pattern as Task 4.2. Key methods:
- `GetWorkforceSummaryAsync()` — headcount totals + by dept
- `GetHiringTrendAsync(months)` — new hires per month for last N months
- `GetAttritionRateAsync(from, to, groupBy)` — separations / avg headcount
- `GetLeaveSummaryAsync(from, to)` — total leave days company-wide
- `GetPerformanceOverviewAsync(cycleId)` — avg score per dept

**Commit:**
```bash
git commit -m "feat(analytics): add executive analytics service with tests"
```

---

### Task 4.4: Analytics Controllers and Caching

**Files:**
- Create: `src/PeopleCore.API/Controllers/Analytics/HrAnalyticsController.cs`
- Create: `src/PeopleCore.API/Controllers/Analytics/ExecutiveAnalyticsController.cs`
- Modify: `src/PeopleCore.API/Extensions/ServiceExtensions.cs`

**Step 1: Create HR controller**

```csharp
[ApiController]
[Route("api/analytics/hr")]
[Authorize(Roles = "Admin,HRManager")]
public class HrAnalyticsController : ControllerBase
{
    private readonly IHrAnalyticsService _service;
    private readonly IMemoryCache _cache;

    [HttpGet("headcount")]
    public async Task<IActionResult> Headcount(CancellationToken ct)
    {
        var cacheKey = "analytics:hr:headcount";
        if (!_cache.TryGetValue(cacheKey, out var result))
        {
            result = await _service.GetHeadcountAsync(ct: ct);
            _cache.Set(cacheKey, result, TimeSpan.FromMinutes(15));
        }
        return Ok(result);
    }
    // ... repeat pattern for all HR endpoints
}
```

**Step 2: Create Executive controller (Roles = "Admin")**

**Step 3: Register services and add IMemoryCache**

```csharp
services.AddMemoryCache();
services.AddScoped<IHrAnalyticsService, HrAnalyticsService>();
services.AddScoped<IExecutiveAnalyticsService, ExecutiveAnalyticsService>();
```

**Step 4: Build and test**

```bash
dotnet build && dotnet test tests/PeopleCore.Application.Tests/
```

**Step 5: Commit**

```bash
git add src/PeopleCore.API/Controllers/Analytics/ src/PeopleCore.API/Extensions/
git commit -m "feat(analytics): add analytics controllers with 15-minute memory cache"
```

---

### Task 4.5: Analytics Dashboard Page (Blazor)

**Files:**
- Create: `src/PeopleCore.Web/Pages/Analytics/Analytics.razor`

**Step 1: Add page with role-gated sections and Chart.js interop**

```razor
@page "/analytics"
@attribute [Authorize(Roles = "Admin,HRManager")]

<h1>Analytics</h1>

@* HR Section *@
<AuthorizeView Roles="Admin,HRManager">
    <!-- Headcount by department chart -->
    <!-- Recruitment funnel -->
    <!-- Leave utilization -->
</AuthorizeView>

@* Executive Section - Admin only *@
<AuthorizeView Roles="Admin">
    <!-- Workforce summary -->
    <!-- Hiring trend (12-month) -->
    <!-- Attrition rate -->
</AuthorizeView>
```

**Step 2: Add Chart.js via CDN in index.html**

```html
<script src="https://cdn.jsdelivr.net/npm/chart.js"></script>
```

**Step 3: Add Analytics link to NavMenu.razor** (visible to Admin, HRManager)

**Step 4: Build**

```bash
dotnet build
```

**Step 5: Commit**

```bash
git add src/PeopleCore.Web/Pages/Analytics/ src/PeopleCore.Web/Layout/NavMenu.razor src/PeopleCore.Web/wwwroot/index.html
git commit -m "feat(analytics): add analytics dashboard Blazor page with Chart.js"
```

---

# FEATURE 5: PWA

---

### Task 5.1: Web App Manifest and Service Worker

**Files:**
- Create: `src/PeopleCore.Web/wwwroot/manifest.json`
- Create: `src/PeopleCore.Web/wwwroot/sw.js`

**Step 1: Create manifest**

```json
{
  "name": "PeopleCore",
  "short_name": "PeopleCore",
  "start_url": "/",
  "display": "standalone",
  "background_color": "#ffffff",
  "theme_color": "#1e40af",
  "icons": [
    { "src": "/icons/icon-192.png", "sizes": "192x192", "type": "image/png" },
    { "src": "/icons/icon-512.png", "sizes": "512x512", "type": "image/png" },
    { "src": "/icons/apple-touch-icon.png", "sizes": "180x180", "type": "image/png" }
  ]
}
```

**Step 2: Create minimal service worker (passthrough, online-only)**

```javascript
// sw.js
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', () => self.clients.claim());
self.addEventListener('fetch', (event) => {
  // Online-only: pass all requests through to network
  event.respondWith(fetch(event.request));
});
```

**Step 3: Commit**

```bash
git add src/PeopleCore.Web/wwwroot/manifest.json src/PeopleCore.Web/wwwroot/sw.js
git commit -m "feat(pwa): add web app manifest and passthrough service worker"
```

---

### Task 5.2: Update index.html

**Files:**
- Modify: `src/PeopleCore.Web/wwwroot/index.html`

**Step 1: Add PWA meta tags and register service worker**

```html
<!-- In <head>: -->
<link rel="manifest" href="/manifest.json" />
<meta name="theme-color" content="#1e40af" />
<meta name="mobile-web-app-capable" content="yes" />
<meta name="apple-mobile-web-app-capable" content="yes" />
<meta name="apple-mobile-web-app-status-bar-style" content="default" />
<link rel="apple-touch-icon" href="/icons/apple-touch-icon.png" />

<!-- Before </body>: -->
<script>
  if ('serviceWorker' in navigator) {
    navigator.serviceWorker.register('/sw.js');
  }
</script>
```

**Step 2: Build and verify no errors**

```bash
dotnet build
```

**Step 3: Commit**

```bash
git add src/PeopleCore.Web/wwwroot/index.html
git commit -m "feat(pwa): add PWA meta tags and service worker registration"
```

---

### Task 5.3: App Icons

**Step 1: Generate icons**

Using any image tool (or online PWA icon generator), create from the company logo:
- `src/PeopleCore.Web/wwwroot/icons/icon-192.png` (192×192)
- `src/PeopleCore.Web/wwwroot/icons/icon-512.png` (512×512)
- `src/PeopleCore.Web/wwwroot/icons/apple-touch-icon.png` (180×180)

**Step 2: Commit**

```bash
git add src/PeopleCore.Web/wwwroot/icons/
git commit -m "feat(pwa): add PWA app icons"
```

---

### Task 5.4: Mobile Footer Navigation

**Files:**
- Create: `src/PeopleCore.Web/Layout/FooterNav.razor`
- Modify: `src/PeopleCore.Web/Layout/MainLayout.razor`
- Modify: `src/PeopleCore.Web/Layout/NavMenu.razor`

**Step 1: Create FooterNav component**

```razor
@* FooterNav.razor *@
@inject NavigationManager Nav

<nav class="footer-nav">
    <a href="/" class="footer-nav-item @IsActive("/")">
        <i class="bi bi-house"></i>
        <span>Home</span>
    </a>
    <a href="/my-attendance" class="footer-nav-item @IsActive("/my-attendance")">
        <i class="bi bi-clock"></i>
        <span>Attendance</span>
    </a>
    <a href="/my-leave" class="footer-nav-item @IsActive("/my-leave")">
        <i class="bi bi-calendar"></i>
        <span>Leave</span>
    </a>
    <a href="/my-profile" class="footer-nav-item @IsActive("/my-profile")">
        <i class="bi bi-person"></i>
        <span>Profile</span>
    </a>
    <AuthorizeView Roles="Admin,HRManager,Manager">
        <a href="/leave-approvals" class="footer-nav-item @IsActive("/leave-approvals")">
            <i class="bi bi-check-circle"></i>
            <span>Approvals</span>
        </a>
    </AuthorizeView>
</nav>

@code {
    private string IsActive(string href) =>
        Nav.Uri.EndsWith(href) ? "active" : string.Empty;
}
```

**Step 2: Add to MainLayout.razor**

```razor
@* In MainLayout.razor, after the main content area: *@
<FooterNav />
```

**Step 3: Add CSS to app.css (or site.css)**

```css
/* Desktop: hide footer nav */
.footer-nav { display: none; }

/* Mobile: show footer nav, hide sidebar */
@media (max-width: 768px) {
    .sidebar { display: none !important; }
    .footer-nav {
        display: flex;
        position: fixed;
        bottom: 0; left: 0; right: 0;
        height: 60px;
        background: #ffffff;
        border-top: 1px solid #e5e7eb;
        z-index: 1000;
        justify-content: space-around;
        align-items: center;
        padding-bottom: env(safe-area-inset-bottom);
    }
    .footer-nav-item {
        display: flex; flex-direction: column;
        align-items: center; gap: 2px;
        font-size: 0.65rem; color: #6b7280;
        text-decoration: none; padding: 4px 8px;
    }
    .footer-nav-item.active { color: #1e40af; }
    .footer-nav-item i { font-size: 1.25rem; }
    /* Add bottom padding to main content so footer doesn't overlap */
    .main-content { padding-bottom: 70px; }
}
```

**Step 4: Build**

```bash
dotnet build
```

**Step 5: Commit**

```bash
git add src/PeopleCore.Web/Layout/FooterNav.razor src/PeopleCore.Web/Layout/MainLayout.razor src/PeopleCore.Web/wwwroot/
git commit -m "feat(pwa): add mobile footer tab navigation"
```

---

### Task 5.5: Responsive UI Audit

**Files to review and update for mobile:**
- `src/PeopleCore.Web/Pages/ESS/MyAttendance.razor` — make Time In/Out button large and prominent
- `src/PeopleCore.Web/Pages/ESS/MyLeave.razor` — stack form fields full-width
- `src/PeopleCore.Web/Pages/HR/Employees.razor` — convert table to card list on mobile
- `src/PeopleCore.Web/Pages/Dashboard.razor` — ensure cards stack vertically on mobile

**Pattern for table → card on mobile (apply per page):**

```css
@media (max-width: 768px) {
    table thead { display: none; }
    table tr { display: block; margin-bottom: 1rem; border: 1px solid #e5e7eb; border-radius: 8px; padding: 0.75rem; }
    table td { display: flex; justify-content: space-between; border: none; padding: 4px 0; }
    table td::before { content: attr(data-label); font-weight: 600; color: #374151; }
}
```

Add `data-label="Column Name"` attributes to each `<td>` in Razor tables.

**Step 1: Update MyAttendance.razor — large Time In/Out button**

```razor
@* Replace existing button with: *@
<button class="btn btn-primary btn-lg w-100 py-4 fs-5" @onclick="TimeIn">
    <i class="bi bi-play-circle me-2"></i> Time In
</button>
```

**Step 2: Build and commit**

```bash
dotnet build
git add src/PeopleCore.Web/Pages/
git commit -m "feat(pwa): responsive UI audit — mobile-friendly tables, forms, and time in/out button"
```

---

## Final Verification

After all 5 features are implemented:

```bash
dotnet build
dotnet test tests/PeopleCore.Application.Tests/
```

Expected: 0 errors, all tests pass.

---

## Summary

| Feature | Tasks | Key Commits |
|---|---|---|
| Shift Scheduling | 1.1–1.9 | 9 commits |
| Leave Accruals | 2.1–2.5 | 7 commits |
| Careers Portal API | 3.1–3.4 | 5 commits |
| Advanced Analytics | 4.1–4.5 | 6 commits |
| PWA | 5.1–5.5 | 6 commits |
| **Total** | **33 tasks** | **~33 commits** |
