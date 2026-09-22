# Separation, Clearance and Certificate of Employment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** HR records each employee's separation, tracks its clearance checklist and final-pay due date, and prints a Certificate of Employment for any current or former employee.

**Architecture:**
- **Data:** two new tables, `separations` and `separation_clearance_items`. A `SeparationService` records, cancels and completes a separation, and owns the clearance items. The existing Deactivate endpoint goes through it, so every departure has a record.
- **Certificate of Employment:** a pure `CoeContent` builder in Application writes the certificate's sentences. A QuestPDF `CoeDocument` in Reports lays them out on the company letterhead.
- **Web:** a Separations page (list and detail), plus row actions on the Employees page.

This is plan 1 of 2. The final-pay run is plan 2. Here, "final pay due by" is shown from the last working day alone.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core (Npgsql, snake_case), QuestPDF, Blazor WebAssembly, xUnit, FluentAssertions, Moq, bUnit, Postgres test fixture.

**Spec:** `docs/superpowers/specs/2026-09-22-separation-final-pay-coe-design.md` (sections "The separation record", "Clearance", "Certificate of Employment", "Where it lives")

## Global Constraints

- **Separation types:** Resignation, TerminationJustCause, AuthorizedCause, EndOfContract, Retirement, Death. **Authorized-cause sub-types:** Redundancy, Retrenchment, ClosureNotDueToLosses, ClosureDueToSeriousLosses, LaborSavingDevices, Disease. A sub-type is required when, and only when, the type is AuthorizedCause.
- **Status:** NoticeGiven, then Separated. Each employee has at most one separation that isn't cancelled. Cancelled separations are deleted, not kept.
- **Mark separated** is allowed on or after the last working day (Philippine date). It sets `Employee.SeparationDate` to the last working day and `IsActive = false`.
- **Cancel** is allowed only in NoticeGiven.
- **Default clearance items**, in this order: "HR", "IT", "Finance", "Immediate supervisor", "Property / admin".
- **Clearance:** items can be added, cleared (who, when, optional note), undone, or deleted while not cleared. Clearance is complete when every item is cleared.
- **Final pay due by** = last working day + 30 days. It shows as overdue when that date is before today (Philippine date) and the separation is Separated. Plan 2 adds "and no Paid final-pay run".
- **Permissions:** `Permissions.EmployeesManage` for separations, clearance and the COE.
- **COE wording:**
  - body: "This is to certify that {full name} has been employed by {company} as {position} from {hire date} to {last working day | 'present'}.";
  - salary sentence, when asked for: "{He/She/They} received a monthly basic salary of ₱{amount}." Use "They" unless you can't find the gender field; see Task 4;
  - default purpose: "This certification is issued upon the request of the employee for whatever legal purpose it may serve.";
  - dates are written like "March 1, 2021".
- **COE signatory:** defaults to the HR user's display name and the title "HR Manager". Both are editable.
- Build and test with `-nodeReuse:false -p:UseSharedCompilation=false`. For migrations, set `DataProtection__KeyEncryptionKey=design-time-only-not-a-secret-0123456789` and `ASPNETCORE_ENVIRONMENT=Development`.
- Commit messages end with a blank line, then `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/PeopleCore.Domain/Entities/Employees/Separation.cs` (new) | The separation record and its clearance items |
| `src/PeopleCore.Domain/Enums/SeparationEnums.cs` (new) | Type, sub-type and status |
| `src/PeopleCore.Infrastructure/Persistence/Configurations/Employees/SeparationConfiguration.cs` (new) | Tables, indexes, relationships |
| `src/PeopleCore.Application/Employees/Interfaces/ISeparationRepository.cs` (new), `src/PeopleCore.Infrastructure/Persistence/Repositories/SeparationRepository.cs` (new) | Load, list, save |
| `src/PeopleCore.Application/Employees/DTOs/SeparationDtos.cs` (new) | Requests and views |
| `src/PeopleCore.Application/Employees/Services/SeparationService.cs` (new) + interface | The rules |
| `src/PeopleCore.Application/Employees/Services/EmployeeService.cs` (modify) | Deactivate goes through the separation service |
| `src/PeopleCore.API/Controllers/Employees/SeparationsController.cs` (new) | Endpoints |
| `src/PeopleCore.Application/Employees/Coe/CoeContent.cs` (new) | The certificate's sentences |
| `src/PeopleCore.Application/Employees/Coe/ICoeRenderer.cs` (new), `src/PeopleCore.Reports/CoeDocument.cs` + `CoeRenderer.cs` (new) | The PDF |
| `src/PeopleCore.Web/Pages/HR/Separations.razor`, `SeparationDetail.razor` (new) | The pages |
| `src/PeopleCore.Web/Pages/HR/Employees.razor` (modify) | Row actions: Record separation, Certificate of Employment |

---

### Task 1: Separation and clearance storage

**Files:**
- Create: `src/PeopleCore.Domain/Enums/SeparationEnums.cs`, `src/PeopleCore.Domain/Entities/Employees/Separation.cs`
- Create: `src/PeopleCore.Infrastructure/Persistence/Configurations/Employees/SeparationConfiguration.cs`
- Create: `src/PeopleCore.Application/Employees/Interfaces/ISeparationRepository.cs`, `src/PeopleCore.Infrastructure/Persistence/Repositories/SeparationRepository.cs`
- Modify: `src/PeopleCore.Infrastructure/Persistence/AppDbContext.cs` (DbSets), `src/PeopleCore.API/Extensions/ServiceExtensions.cs` (register the repository next to the other employee repositories)
- Generate: migration `AddSeparations`
- Test: `tests/PeopleCore.Infrastructure.Tests/Employees/SeparationRepositoryTests.cs`

**Interfaces:**
- Produces:
  - enums `SeparationType`, `AuthorizedCause`, `SeparationStatus`;
  - entities `Separation` and `SeparationClearanceItem`;
  - `ISeparationRepository`:
    - `Task<Separation?> GetAsync(Guid id, CancellationToken ct = default)`: with `ClearanceItems` and `Employee`;
    - `Task<Separation?> GetOpenForEmployeeAsync(Guid employeeId, CancellationToken ct = default)`;
    - `Task<IReadOnlyList<Separation>> ListAsync(CancellationToken ct = default)`: with `ClearanceItems` and `Employee`, newest last working day first;
    - `Task AddAsync(Separation s, CancellationToken ct = default)`;
    - `Task SaveAsync(CancellationToken ct = default)`;
    - `Task DeleteAsync(Separation s, CancellationToken ct = default)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using FluentAssertions;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Enums;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Employees;

public class SeparationRepositoryTests : DatabaseTestBase
{
    public SeparationRepositoryTests(PostgresFixture fixture) : base(fixture) { }

    private async Task<Guid> AnEmployeeIdAsync()
    {
        var e = AnEmployee();
        Context.Employees.Add(e);
        await Context.SaveChangesAsync();
        return e.Id;
    }

    private static Separation ASeparation(Guid employeeId, DateOnly lastDay) => new()
    {
        EmployeeId = employeeId, Type = SeparationType.Resignation, NoticeDate = lastDay.AddDays(-30),
        LastWorkingDay = lastDay, Status = SeparationStatus.NoticeGiven, RecordedBy = "hr@company.test",
        ClearanceItems = [new SeparationClearanceItem { Name = "HR", SortOrder = 0 }, new SeparationClearanceItem { Name = "IT", SortOrder = 1 }]
    };

    [Fact]
    public async Task Add_ThenGet_ReturnsTheSeparationWithItsClearanceItemsInOrder()
    {
        var id = await AnEmployeeIdAsync();
        var separation = ASeparation(id, new DateOnly(2026, 9, 30));
        await new SeparationRepository(NewContext()).AddAsync(separation);

        var loaded = await new SeparationRepository(NewContext()).GetAsync(separation.Id);

        loaded!.Employee.Should().NotBeNull();
        loaded.ClearanceItems.OrderBy(i => i.SortOrder).Select(i => i.Name).Should().Equal("HR", "IT");
    }

    [Fact]
    public async Task GetOpenForEmployee_FindsTheirSeparation()
    {
        var id = await AnEmployeeIdAsync();
        await new SeparationRepository(NewContext()).AddAsync(ASeparation(id, new DateOnly(2026, 9, 30)));

        (await new SeparationRepository(NewContext()).GetOpenForEmployeeAsync(id)).Should().NotBeNull();
        (await new SeparationRepository(NewContext()).GetOpenForEmployeeAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task AnEmployee_CanHaveOnlyOneSeparation()
    {
        var id = await AnEmployeeIdAsync();
        await new SeparationRepository(NewContext()).AddAsync(ASeparation(id, new DateOnly(2026, 9, 30)));

        var act = () => new SeparationRepository(NewContext()).AddAsync(ASeparation(id, new DateOnly(2026, 10, 31)));

        await act.Should().ThrowAsync<Microsoft.EntityFrameworkCore.DbUpdateException>();
    }

    [Fact]
    public async Task Delete_RemovesTheSeparationAndItsItems()
    {
        var id = await AnEmployeeIdAsync();
        var separation = ASeparation(id, new DateOnly(2026, 9, 30));
        await new SeparationRepository(NewContext()).AddAsync(separation);

        await using (var ctx = NewContext())
        {
            var repo = new SeparationRepository(ctx);
            await repo.DeleteAsync((await repo.GetAsync(separation.Id))!);
        }

        await using var reader = NewContext();
        reader.Set<Separation>().Count().Should().Be(0);
        reader.Set<SeparationClearanceItem>().Count().Should().Be(0);
    }
}
```

Copy any `[Collection]` attribute used by the other classes in `tests/PeopleCore.Infrastructure.Tests`.

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test tests/PeopleCore.Infrastructure.Tests --filter "FullyQualifiedName~SeparationRepositoryTests" -nodeReuse:false -p:UseSharedCompilation=false`
Expected: build error.

- [ ] **Step 3: Write the enums and entities**

```csharp
// SeparationEnums.cs
namespace PeopleCore.Domain.Enums;

public enum SeparationType { Resignation, TerminationJustCause, AuthorizedCause, EndOfContract, Retirement, Death }

/// <summary>Labor Code Art. 298-299 causes; required when the type is AuthorizedCause.</summary>
public enum AuthorizedCause { Redundancy, Retrenchment, ClosureNotDueToLosses, ClosureDueToSeriousLosses, LaborSavingDevices, Disease }

public enum SeparationStatus { NoticeGiven, Separated }
```

```csharp
// Separation.cs
using PeopleCore.Domain.Enums;

namespace PeopleCore.Domain.Entities.Employees;

/// <summary>
/// One employee's departure: how and when they left, and the clearance that has to be complete
/// before their final pay is released.
/// </summary>
public class Separation : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;

    public SeparationType Type { get; set; }
    public AuthorizedCause? AuthorizedCause { get; set; }
    public DateOnly NoticeDate { get; set; }
    public DateOnly LastWorkingDay { get; set; }
    public string? Reason { get; set; }

    public SeparationStatus Status { get; set; }
    public string RecordedBy { get; set; } = "";
    public string? SeparatedBy { get; set; }
    public DateTime? SeparatedAt { get; set; }

    public List<SeparationClearanceItem> ClearanceItems { get; set; } = [];

    /// <summary>DOLE Labor Advisory 06-2020: final pay within 30 days of separation.</summary>
    public DateOnly FinalPayDueBy => LastWorkingDay.AddDays(30);

    public bool ClearanceComplete => ClearanceItems.Count > 0 && ClearanceItems.All(i => i.ClearedAt is not null);
}

public class SeparationClearanceItem : AuditableEntity
{
    public Guid SeparationId { get; set; }
    public Separation Separation { get; set; } = null!;
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public string? ClearedBy { get; set; }
    public DateTime? ClearedAt { get; set; }
    public string? Note { get; set; }
}
```

Check `AuditableEntity`'s namespace (the other entities in `Entities/Employees` show it) and whether `Employee` in this folder is `PeopleCore.Domain.Entities.Employees.Employee`.

- [ ] **Step 4: Write the configuration, repository and registration**

```csharp
// SeparationConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Employees;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Employees;

public class SeparationConfiguration : IEntityTypeConfiguration<Separation>
{
    public void Configure(EntityTypeBuilder<Separation> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.AuthorizedCause).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Reason).HasMaxLength(1000);
        builder.Property(x => x.RecordedBy).HasMaxLength(256);
        builder.Property(x => x.SeparatedBy).HasMaxLength(256);
        builder.Ignore(x => x.FinalPayDueBy);
        builder.Ignore(x => x.ClearanceComplete);

        // At most one separation per employee: a withdrawn one is deleted, not kept.
        builder.HasIndex(x => x.EmployeeId).IsUnique();

        builder.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.ClearanceItems).WithOne(i => i.Separation).HasForeignKey(i => i.SeparationId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public class SeparationClearanceItemConfiguration : IEntityTypeConfiguration<SeparationClearanceItem>
{
    public void Configure(EntityTypeBuilder<SeparationClearanceItem> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ClearedBy).HasMaxLength(256);
        builder.Property(x => x.Note).HasMaxLength(500);
    }
}
```

```csharp
// ISeparationRepository.cs
using PeopleCore.Domain.Entities.Employees;

namespace PeopleCore.Application.Employees.Interfaces;

public interface ISeparationRepository
{
    Task<Separation?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Separation?> GetOpenForEmployeeAsync(Guid employeeId, CancellationToken ct = default);
    Task<IReadOnlyList<Separation>> ListAsync(CancellationToken ct = default);
    Task AddAsync(Separation separation, CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default);
    Task DeleteAsync(Separation separation, CancellationToken ct = default);
}
```

```csharp
// SeparationRepository.cs
using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Domain.Entities.Employees;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class SeparationRepository : ISeparationRepository
{
    private readonly AppDbContext _context;

    public SeparationRepository(AppDbContext context) => _context = context;

    private IQueryable<Separation> WithDetails() =>
        _context.Separations.Include(s => s.ClearanceItems).Include(s => s.Employee).ThenInclude(e => e.Position);

    public async Task<Separation?> GetAsync(Guid id, CancellationToken ct = default)
        => await WithDetails().FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<Separation?> GetOpenForEmployeeAsync(Guid employeeId, CancellationToken ct = default)
        => await WithDetails().FirstOrDefaultAsync(s => s.EmployeeId == employeeId, ct);

    public async Task<IReadOnlyList<Separation>> ListAsync(CancellationToken ct = default)
        => await WithDetails().OrderByDescending(s => s.LastWorkingDay).AsSplitQuery().ToListAsync(ct);

    public async Task AddAsync(Separation separation, CancellationToken ct = default)
    {
        _context.Separations.Add(separation);
        await _context.SaveChangesAsync(ct);
    }

    public Task SaveAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);

    public async Task DeleteAsync(Separation separation, CancellationToken ct = default)
    {
        _context.Separations.Remove(separation);
        await _context.SaveChangesAsync(ct);
    }
}
```

Add `DbSet<Separation> Separations` and `DbSet<SeparationClearanceItem> SeparationClearanceItems` to `AppDbContext`. Register `services.AddScoped<ISeparationRepository, SeparationRepository>();`. If `Employee.Position` isn't a navigation you can `ThenInclude`, drop that include and say so.

- [ ] **Step 5: Generate the migration**

Run: `DataProtection__KeyEncryptionKey=design-time-only-not-a-secret-0123456789 ASPNETCORE_ENVIRONMENT=Development dotnet ef migrations add AddSeparations --project src/PeopleCore.Infrastructure --startup-project src/PeopleCore.API`
Expected: the migration creates only the two tables, their unique index and foreign keys. Stop and report if it touches anything else.

- [ ] **Step 6: Run the tests and confirm they pass**, then run the whole solution once.

- [ ] **Step 7: Commit**: `feat(employees): store separations and their clearance items`

---

### Task 2: The separation rules

**Files:**
- Create: `src/PeopleCore.Application/Employees/DTOs/SeparationDtos.cs`, `src/PeopleCore.Application/Employees/Interfaces/ISeparationService.cs`, `src/PeopleCore.Application/Employees/Services/SeparationService.cs`
- Modify: `src/PeopleCore.Application/Employees/Services/EmployeeService.cs` (`DeactivateAsync`), `src/PeopleCore.API/Controllers/Employees/EmployeesController.cs` (`DeactivateEmployeeRequest` gains an optional type), `src/PeopleCore.API/Extensions/ServiceExtensions.cs`
- Test: `tests/PeopleCore.Application.Tests/Employees/SeparationServiceTests.cs`; update the existing Deactivate tests (find them with `grep -rn DeactivateAsync tests`)

**Interfaces:**
- Consumes: `ISeparationRepository` (Task 1); `IEmployeeRepository.GetByIdAsync` / `UpdateAsync`; `ICurrentUserService.Email`; `TimeProvider` + `PhilippineTime.Now` (`PeopleCore.Application.Common.Time`).
- Produces:
  - DTOs:
    - `RecordSeparationRequest(Guid EmployeeId, SeparationType Type, AuthorizedCause? AuthorizedCause, DateOnly NoticeDate, DateOnly LastWorkingDay, string? Reason)`;
    - `SeparationDto(Guid Id, Guid EmployeeId, string EmployeeName, string EmployeeNumber, string? Position, SeparationType Type, AuthorizedCause? AuthorizedCause, DateOnly NoticeDate, DateOnly LastWorkingDay, string? Reason, SeparationStatus Status, string RecordedBy, string? SeparatedBy, DateTime? SeparatedAt, DateOnly FinalPayDueBy, bool FinalPayOverdue, int ClearedCount, int ClearanceCount, IReadOnlyList<ClearanceItemDto> ClearanceItems)`;
    - `ClearanceItemDto(Guid Id, string Name, string? ClearedBy, DateTime? ClearedAt, string? Note)`;
    - `ClearItemRequest(string? Note)`;
    - `AddClearanceItemRequest(string Name)`.
  - `ISeparationService`:
    - `RecordAsync(RecordSeparationRequest)` → `SeparationDto`;
    - `GetAsync(Guid id)` → `SeparationDto?`;
    - `ListAsync()` → `IReadOnlyList<SeparationDto>`;
    - `MarkSeparatedAsync(Guid id)` → `SeparationDto`;
    - `CancelAsync(Guid id)` → `Task`;
    - `AddClearanceItemAsync(Guid id, string name)` → `SeparationDto`;
    - `ClearItemAsync(Guid id, Guid itemId, string? note)` → `SeparationDto`;
    - `UndoClearItemAsync(Guid id, Guid itemId)` → `SeparationDto`;
    - `DeleteClearanceItemAsync(Guid id, Guid itemId)` → `SeparationDto`;
    - `SeparateNowAsync(Guid employeeId, DateOnly lastWorkingDay, SeparationType type)` → `SeparationDto` (records and marks separated in one step, for Deactivate).
  - Each method takes a `CancellationToken ct = default`.

**Rules** (each gets a test; messages exact):
- `RecordAsync`:
  - The employee must exist and be active; otherwise KeyNotFound or DomainException("{name} is no longer active.").
  - The employee must have no separation; otherwise DomainException("{name} already has a separation recorded.").
  - `AuthorizedCause` is required when Type is AuthorizedCause ("Choose the authorized cause."), and must be null otherwise ("Only an authorized-cause separation has a cause.").
  - `LastWorkingDay` must not be before `NoticeDate` ("The last working day can't be before the notice date.").
  - It creates the five default clearance items in order, and sets `RecordedBy` to the current user's email.
- `MarkSeparatedAsync`:
  - Only from NoticeGiven ("This separation is already complete.").
  - Only when today (Philippine date) is on or after `LastWorkingDay` ("{name}'s last working day is {Mar 3, 2026}; mark them separated on or after it.").
  - Sets the status to Separated, and `SeparatedBy` and `SeparatedAt`. Sets the employee's `SeparationDate` to `LastWorkingDay` and `IsActive` to false, and saves both.
- `CancelAsync`: only in NoticeGiven ("A completed separation can't be cancelled."). Deletes the record.
- Clearance:
  - An added name must be non-blank and at most 100 characters, and not a duplicate (ignoring case) of an existing item on the separation ("There's already a {name} item.").
  - Clearing an already-cleared item: "{item} is already cleared."
  - Undo on an item that isn't cleared: "{item} isn't cleared."
  - Delete of a cleared item: "Undo {item}'s clearance before removing it."
  - Every clearance change is refused once the separation's final pay is Paid. There is no final pay in plan 1, so no check is needed yet. Leave a single `private void EnsureClearanceEditable(Separation s)` that plan 2 will fill in, and call it from each clearance method.
- `SeparationDto.FinalPayOverdue`: `Status == Separated && FinalPayDueBy < today`.
- Deactivate: `EmployeeService.DeactivateAsync(id, separationDate)` now calls `ISeparationService.SeparateNowAsync(id, separationDate, type)`. `DeactivateEmployeeRequest` gains `SeparationType Type = SeparationType.Resignation`. If the employee already has a NoticeGiven separation, SeparateNowAsync uses that record: it updates the last working day to the given date and marks it separated, ignoring the today check, because HR is asserting the date.

- [ ] **Step 1: Write the failing tests**, one per rule above. Use Moq for `ISeparationRepository` (capture `AddAsync`'s argument; have `GetAsync` return it) and `IEmployeeRepository`, a `FixedClock : TimeProvider` like the other service tests, and a mocked `ICurrentUserService` returning `"hr@company.test"`. Keep them concrete, for example:

```csharp
    [Fact]
    public async Task Record_CreatesTheFiveDefaultClearanceItemsInOrder()
    {
        var dto = await _sut.RecordAsync(new RecordSeparationRequest(_employee.Id, SeparationType.Resignation, null,
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), "Moving abroad"));

        dto.ClearanceItems.Select(i => i.Name).Should().Equal("HR", "IT", "Finance", "Immediate supervisor", "Property / admin");
        dto.ClearanceCount.Should().Be(5);
        dto.ClearedCount.Should().Be(0);
        dto.RecordedBy.Should().Be("hr@company.test");
        dto.FinalPayDueBy.Should().Be(new DateOnly(2026, 10, 30));
    }

    [Fact]
    public async Task MarkSeparated_BeforeTheLastWorkingDay_IsRefused()
    {
        // The clock is 2026-09-21 in Manila; the last working day is 2026-09-30.
        var s = await Recorded(lastDay: new DateOnly(2026, 9, 30));

        var act = () => _sut.MarkSeparatedAsync(s.Id);

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Contain("Sep 30, 2026");
    }

    [Fact]
    public async Task MarkSeparated_OnTheLastWorkingDay_DeactivatesTheEmployee()
    {
        var s = await Recorded(lastDay: new DateOnly(2026, 9, 21));

        var dto = await _sut.MarkSeparatedAsync(s.Id);

        dto.Status.Should().Be(SeparationStatus.Separated);
        _employee.IsActive.Should().BeFalse();
        _employee.SeparationDate.Should().Be(new DateOnly(2026, 9, 21));
    }
```

- [ ] **Step 2: Run the tests and confirm they fail.**

Run: `dotnet test tests/PeopleCore.Application.Tests --filter "FullyQualifiedName~SeparationServiceTests" -nodeReuse:false -p:UseSharedCompilation=false`

- [ ] **Step 3: Implement.** The service maps entities to DTOs in one private `ToDto(Separation s, DateOnly today)`, using the employee's `FullName`, `EmployeeNumber` and `Position?.Name`. Keep the default item names in a `static readonly string[] DefaultClearanceItems`. Format dates in messages with `MMM d, yyyy` in the invariant culture.

- [ ] **Step 4: Run the tests and confirm they pass**, then run the whole solution once. Existing Deactivate tests must pass unchanged, except for a constructor argument if `EmployeeService` gains `ISeparationService`.

- [ ] **Step 5: Commit**: `feat(employees): record, complete and cancel separations, with a clearance checklist`

---

### Task 3: Separation endpoints

**Files:**
- Create: `src/PeopleCore.API/Controllers/Employees/SeparationsController.cs`
- Test: `tests/PeopleCore.Application.Tests/Employees/SeparationsControllerTests.cs`; add the routes wherever `PermissionEquivalenceTests` pins endpoints (`AddedEndpoints`)

**Interfaces:**
- Consumes: `ISeparationService` (Task 2).
- Produces the routes, all under `[RequirePermission(Permissions.EmployeesManage)]` at class level, route `api/separations`:
  - `GET` → list;
  - `GET {id:guid}` → one, or 404;
  - `POST` (body `RecordSeparationRequest`) → 201 with the DTO;
  - `POST {id:guid}/mark-separated`;
  - `POST {id:guid}/cancel` → 204;
  - `POST {id:guid}/clearance` (body `AddClearanceItemRequest`);
  - `POST {id:guid}/clearance/{itemId:guid}/clear` (body `ClearItemRequest`);
  - `POST {id:guid}/clearance/{itemId:guid}/undo`;
  - `DELETE {id:guid}/clearance/{itemId:guid}`.

- [ ] **Step 1: Tests:** the class-level permission is `EmployeesManage` (by reflection, as `Bir2316AuthorizationTests` does); each action calls the matching service method and returns the right result type (Ok, Created, NoContent, NotFound).
- [ ] **Step 2: Run them and confirm they fail.**
- [ ] **Step 3: Implement** the controller as a thin pass-through to the service. DomainException and KeyNotFound are mapped by the middleware.
- [ ] **Step 4: Run the whole solution once.** `PermissionEquivalenceTests` will fail until the new routes are added to it. Add them.
- [ ] **Step 5: Commit**: `feat(api): separation and clearance endpoints`

---

### Task 4: Certificate of Employment

**Files:**
- Create: `src/PeopleCore.Application/Employees/Coe/CoeContent.cs`, `src/PeopleCore.Application/Employees/Coe/ICoeRenderer.cs`, `src/PeopleCore.Application/Employees/Coe/CoeService.cs` (+ `ICoeService`)
- Create: `src/PeopleCore.Reports/CoeDocument.cs`, `src/PeopleCore.Reports/CoeRenderer.cs`
- Modify: `src/PeopleCore.API/Controllers/Employees/EmployeesController.cs` (a `coe` action), `ServiceExtensions.cs` (register `ICoeService` and `ICoeRenderer` next to `IPayslipRenderer`)
- Test: `tests/PeopleCore.Application.Tests/Employees/CoeContentTests.cs`, `CoeServiceTests.cs`, `tests/PeopleCore.Application.Tests/Employees/CoeDocumentTests.cs`

**Interfaces:**
- Produces:
  - `CoeRequest(string? Purpose, string? SignatoryName, string? SignatoryTitle, bool IncludeSalary)`: the request body.
  - `CoeContent`: a record `(string CompanyName, string? CompanyAddress, byte[]? Logo, string Title, IReadOnlyList<string> Paragraphs, string DateLine, string SignatoryName, string SignatoryTitle)`, plus `static CoeContent Build(CoeFacts facts, CoeRequest request, DateOnly today)`.
  - `CoeFacts(string FullName, string? Position, DateOnly HireDate, DateOnly? LastWorkingDay, string CompanyName, string? CompanyAddress, byte[]? Logo, decimal? MonthlyBasicSalary, string? Gender, string DefaultSignatoryName)`.
  - `ICoeRenderer.Render(CoeContent) : byte[]`.
  - `ICoeService.GenerateAsync(Guid employeeId, CoeRequest request, CancellationToken ct = default) : Task<(byte[] Pdf, string FileName)>`.
  - The route `POST api/employees/{id:guid}/coe` → `application/pdf`, named `COE-<LastName><FirstName>-<yyyyMMdd>.pdf`.

- [ ] **Step 1: Write the failing `CoeContentTests`** (pure):

```csharp
using FluentAssertions;
using PeopleCore.Application.Employees.Coe;
using Xunit;

namespace PeopleCore.Application.Tests.Employees;

public class CoeContentTests
{
    private static CoeFacts Facts(DateOnly? lastDay = null, decimal? salary = 35_000m) => new(
        "Juan Santos Cruz", "Payroll Officer", new DateOnly(2021, 3, 1), lastDay,
        "Acme Inc.", "123 Ayala Ave, Makati", null, salary, null, "Maria Reyes");

    private static readonly DateOnly Today = new(2026, 9, 22);

    [Fact]
    public void ACurrentEmployee_IsEmployedToPresent()
    {
        var c = CoeContent.Build(Facts(), new CoeRequest(null, null, null, false), Today);

        c.Title.Should().Be("CERTIFICATE OF EMPLOYMENT");
        c.Paragraphs[0].Should().Be(
            "This is to certify that Juan Santos Cruz has been employed by Acme Inc. as Payroll Officer from March 1, 2021 to present.");
        c.Paragraphs.Last().Should().Be(
            "This certification is issued upon the request of the employee for whatever legal purpose it may serve.");
        c.DateLine.Should().Be("Issued on September 22, 2026.");
        c.SignatoryName.Should().Be("Maria Reyes");
        c.SignatoryTitle.Should().Be("HR Manager");
    }

    [Fact]
    public void AFormerEmployee_WasEmployedToTheirLastWorkingDay()
    {
        var c = CoeContent.Build(Facts(lastDay: new DateOnly(2026, 8, 31)), new CoeRequest(null, null, null, false), Today);

        c.Paragraphs[0].Should().Be(
            "This is to certify that Juan Santos Cruz was employed by Acme Inc. as Payroll Officer from March 1, 2021 to August 31, 2026.");
    }

    [Fact]
    public void TheSalarySentence_IsAddedOnlyWhenAskedFor()
    {
        CoeContent.Build(Facts(), new CoeRequest(null, null, null, false), Today).Paragraphs.Should().HaveCount(2);

        var withSalary = CoeContent.Build(Facts(), new CoeRequest(null, null, null, true), Today);

        withSalary.Paragraphs.Should().HaveCount(3);
        withSalary.Paragraphs[1].Should().Be("They received a monthly basic salary of ₱35,000.00.");
    }

    [Fact]
    public void PurposeAndSignatory_CanBeChanged()
    {
        var c = CoeContent.Build(Facts(), new CoeRequest("  For a bank loan application.  ", "Ana Lim", "HR Director", false), Today);

        c.Paragraphs.Last().Should().Be("For a bank loan application.");
        c.SignatoryName.Should().Be("Ana Lim");
        c.SignatoryTitle.Should().Be("HR Director");
    }

    [Fact]
    public void WithNoPosition_TheSentenceOmitsIt()
    {
        var c = CoeContent.Build(Facts() with { Position = null }, new CoeRequest(null, null, null, false), Today);

        c.Paragraphs[0].Should().Be("This is to certify that Juan Santos Cruz has been employed by Acme Inc. from March 1, 2021 to present.");
    }

    [Fact]
    public void AskingForSalary_WithNoneOnRecord_IsRefused()
    {
        var act = () => CoeContent.Build(Facts(salary: null), new CoeRequest(null, null, null, true), Today);

        act.Should().Throw<PeopleCore.Domain.Exceptions.DomainException>().WithMessage("*no salary on record*");
    }
}
```

A former employee's sentence says "was employed", and a current one's "has been employed". The pronoun in the salary sentence is "They" unless `Gender` is "Male" ("He") or "Female" ("She"). Match whatever the `Gender` enum's `ToString()` gives. Dates use the invariant culture `MMMM d, yyyy`, and the amount uses `#,##0.00` with a ₱ prefix.

- [ ] **Step 2: Write the failing `CoeServiceTests`.** With mocked `IEmployeeRepository`, `ICompanyRepository`, `IEmployeeCompensationRepository`, `ICurrentUserService` and `ICoeRenderer`:
  - the service passes the right facts to the renderer: `LastWorkingDay` = `SeparationDate` for a former employee and null for a current one; position name; salary from the compensation record; signatory = the current user's display name, falling back to the email;
  - the file name is `COE-CruzJuan-20260922.pdf`;
  - an unknown employee throws KeyNotFound.

  Check `ICurrentUserService` for a display-name property. If there is none, use the email. Capture the `CoeContent` passed to `ICoeRenderer.Render` with Moq.

- [ ] **Step 3: Write the failing `CoeDocumentTests`**, mirroring `PayslipDocumentTests`:
  - it renders a real PDF (`%PDF-` prefix);
  - it renders with a logo and without;
  - it renders with very long names.

- [ ] **Step 4: Run them all and confirm they fail.**

- [ ] **Step 5: Implement.**
  - `CoeContent.Build` in Application: pure, with the exact sentences above. It throws a DomainException ("{name} has no salary on record, so the salary line can't be added.") when `IncludeSalary` is set and the salary is null.
  - `CoeService` in Application: loads the facts, calls `Build` with today's Philippine date, then the renderer.
  - `CoeDocument` in Reports, with QuestPDF on A4:
    - the letterhead (logo if any, company name and address) at the top;
    - the title centred in bold;
    - the paragraphs justified with space between;
    - the date line;
    - a signature block (a line, the signatory's name in bold, the title).
  - Look at `PayslipDocument` for fonts and the logo handling.
  - `CoeRenderer => new CoeDocument(content).GeneratePdf()`.
  - The controller action:

```csharp
    /// <summary>A Certificate of Employment for a current or former employee.</summary>
    [HttpPost("{id:guid}/coe")]
    [RequirePermission(Permissions.EmployeesManage)]
    public async Task<IActionResult> Coe(Guid id, [FromBody] CoeRequest request, CancellationToken ct)
    {
        var (pdf, fileName) = await _coe.GenerateAsync(id, request, ct);
        return File(pdf, "application/pdf", fileName);
    }
```

  Inject `ICoeService` into `EmployeesController` and update its tests' construction if any build it. Add the route to `PermissionEquivalenceTests` if it pins endpoints.

- [ ] **Step 6: Run the tests and confirm they pass**, then run the whole solution once.

- [ ] **Step 7: Commit**: `feat(employees): print a Certificate of Employment for a current or former employee`

---

### Task 5: The pages

**Files:**
- Modify: `src/PeopleCore.Web/Services/ApiClient.cs`: records mirroring the separation DTOs, `CoeRequest`, and the methods below
- Create: `src/PeopleCore.Web/Pages/HR/Separations.razor` (`/separations`), `src/PeopleCore.Web/Pages/HR/SeparationDetail.razor` (`/separations/{Id:guid}`)
- Modify: `src/PeopleCore.Web/Layout/NavMenu.razor`: add `new("/separations", "Separations", "bi-box-arrow-right")` to the "Employees" section after "All Employees"
- Modify: `src/PeopleCore.Web/Pages/HR/Employees.razor`: row actions
- Test: `tests/PeopleCore.Web.Tests/Pages/HR/SeparationsTests.cs`, and the Employees tests; update `NavMenuTests` for the new link

**Interfaces:**
- Consumes: the Task 3 routes and `POST api/employees/{id}/coe` (Task 4).
- Produces these ApiClient methods:
  - `GetSeparationsAsync()`, `GetSeparationAsync(id)`;
  - `RecordSeparationAsync(request)`;
  - `MarkSeparatedAsync(id)`, `CancelSeparationAsync(id)`;
  - `AddClearanceItemAsync(id, name)`, `ClearItemAsync(id, itemId, note)`, `UndoClearItemAsync(id, itemId)`, `DeleteClearanceItemAsync(id, itemId)`;
  - `GetCoeAsync(employeeId, request) : Task<byte[]>`.

  Follow the existing patterns: `GetJsonAsync`, `SendJsonAsync`, and `EnsureSuccessAsync` for the PDF. The enums travel as strings; check `JsonStringEnumConverter` in the API's `Program.cs`, and mirror the enums in the Web client as the other enums are.

**Behavior** (each gets a bUnit test):
- **Separations list** (`[RequirePermission(Permissions.EmployeesManage)]`):
  - a table of employee, type (with the cause for authorized causes), last working day, status, clearance "3 of 5", and final pay due by;
  - an "Overdue" badge (`data-overdue`) when `FinalPayOverdue`;
  - a "Record separation" button opens a sheet form (`data-record-form`) with an employee picker (active employees), type, cause (shown only for Authorized cause), notice date, last working day and reason. Submitting posts and navigates to the new record's detail page;
  - API errors show in the form (`data-form-error`).
- **Separation detail:**
  - the record's fields;
  - "Mark separated" (`data-mark-separated`), shown in NoticeGiven. On error, for example before the last day, the message shows in `data-page-error`;
  - "Cancel separation" (`data-cancel`), shown in NoticeGiven, with a confirm step. On success it navigates back to the list;
  - the clearance checklist (`data-clearance`). Each item has Clear (with an optional note input), Undo when cleared, and Remove when not cleared. There is an "Add item" input and button;
  - "Clearance complete" (`data-clearance-complete`) when every item is cleared;
  - a note: "Final pay is due by {date}." In plan 2 this panel gets "Create final pay".
- **Employees page** row actions:
  - "Record separation" links to `/separations?employee={id}`. The list page opens the form with that employee chosen. Show it only for active employees without a separation. The list DTO doesn't know about separations, so show it for active employees; the API refuses a duplicate, with the message shown in the form;
  - "Certificate of Employment" opens a dialog (`data-coe-dialog`) with purpose (prefilled with the default sentence), signatory name, signatory title (prefilled "HR Manager") and an "Include monthly salary" checkbox. "Download" calls `GetCoeAsync` and saves the file through `downloadFileFromBytes`. Errors show in the dialog.

Model the pages on `GovernmentReports.razor` and `AttendanceRecords.razor` (Sheet, Dialog, Button, Alert, the error-message conventions, stale-response care where a page loads more than once).

- [ ] **Step 1: Write the failing bUnit tests** for each behavior above, using `StubHttpHandler` and the auth setup other HR page tests use (`SeededPermissions.ClaimsFor("HRManager")`).
- [ ] **Step 2: Run them and confirm they fail.**
- [ ] **Step 3: Implement** the ApiClient methods, the two pages, the nav entry and the Employees row actions.
- [ ] **Step 4: Run the Web tests, then the whole solution once.**
- [ ] **Step 5: Commit**: `feat(web): Separations pages with clearance, and a Certificate of Employment from the Employees page`

---

### Task 6: Verification

- [ ] Run `dotnet test PeopleCore.slnx -nodeReuse:false -p:UseSharedCompilation=false`. Expected: every project passes with no compiler warnings.
- [ ] Read the `AddSeparations` migration's `Up`: only the two tables, the unique index and the foreign keys.
- [ ] Browser check: needs a signed-in HR account. Record it as not done if no one can sign in.
