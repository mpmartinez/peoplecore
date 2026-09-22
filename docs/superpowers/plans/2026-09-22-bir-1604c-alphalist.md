# BIR 1604-C Alphalist Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Save each employee's BIR 2316 manual inputs per year, and add a BIR 1604-C alphalist tab to the Government Reports page. The tab groups each employee's 2316 figures into BIR's schedules and has a CSV download.

**Architecture:** A new `Bir2316Inputs` table stores the manual 2316 fields. `Bir2316Service` saves them when a single 2316 is generated, and reads them for the preview, "Generate all" and the alphalist. A pure `Bir1604CAlphalist` builder turns the year's 2316 DTOs, the employees and the year's paid runs into a `GovernmentReportDto` with three `Sections`. `GovernmentReportService` gains an annual entry point, and the controller, CSV writer and page learn to handle sections and a year-only report.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core (Npgsql, snake_case naming), Blazor WebAssembly, xUnit, FluentAssertions, Moq, bUnit, Postgres test fixture (`DatabaseTestBase`).

**Spec:** `docs/superpowers/specs/2026-09-22-bir-1604c-alphalist-design.md`

## Global Constraints

- Only `PayrollRunStatus.Paid` runs count. The alphalist year is the `PayDate` year, the 2316's basis.
- The saved inputs hold exactly the fields of `Bir2316ManualInputs`, and no derived figure. They are validated with `Bir2316ManualInputsValidator.Validate` before saving.
- `BuildAsync` saves nothing. Only the new `GenerateAsync` saves.
- Groups: (1) `SeparationDate` within the year and before December 31; otherwise (3) Item 22 or Item 25B is non-zero; otherwise (2).
- Group titles: "Terminated before December 31", "Employed as of December 31, no previous employer", "Employed as of December 31, with previous employer".
- Report key `1604c`. Route `GET api/reports/government/1604c?year=[&format=csv]`. CSV file name `1604c-<yyyy>.csv`. The monthly reports still require `month`; for them a missing month is a 400.
- Money cells use invariant `0.00` (`GovernmentReportMath.Money`). CSV cells keep the formula-injection neutralising.
- Guarded by `Permissions.PayrollManage`.
- Build and test with `-nodeReuse:false -p:UseSharedCompilation=false`. For the migration, set the environment variable `DataProtection__KeyEncryptionKey=design-time-only-not-a-secret-0123456789` and `ASPNETCORE_ENVIRONMENT=Development`.
- Commit messages end with a blank line then `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/PeopleCore.Domain/Entities/Payroll/Bir2316Inputs.cs` (new) | Saved manual 2316 fields for one employee-year |
| `src/PeopleCore.Infrastructure/Persistence/Configurations/Payroll/Bir2316InputsConfiguration.cs` (new) | Table, unique index, column types |
| `src/PeopleCore.Infrastructure/Persistence/Migrations/*_AddBir2316Inputs.cs` (generated) | Migration |
| `src/PeopleCore.Application/Payroll/Interfaces/IBir2316InputsRepository.cs` (new) | Get, get for the year, upsert |
| `src/PeopleCore.Infrastructure/Persistence/Repositories/Bir2316InputsRepository.cs` (new) | EF implementation |
| `src/PeopleCore.Application/Payroll/Services/Bir2316Service.cs` (modify) | `GenerateAsync`, `GetInputsAsync`; preview and bulk build read saved inputs |
| `src/PeopleCore.API/Controllers/Payroll/Bir2316Controller.cs` (modify) | Generate calls `GenerateAsync`; new `inputs` endpoint |
| `src/PeopleCore.Application/Payroll/GovernmentReports/GovernmentReportDtos.cs` (modify) | `Sections` and `GovernmentReportSectionDto` |
| `src/PeopleCore.Application/Payroll/GovernmentReports/GovernmentReportCsv.cs` (modify) | Write sections; annual file name |
| `src/PeopleCore.Application/Payroll/GovernmentReports/Bir1604CAlphalist.cs` (new) | Pure builder: groups, columns, totals, warnings |
| `src/PeopleCore.Application/Payroll/GovernmentReports/GovernmentReportService.cs` + interface (modify) | `BuildAnnualAsync` |
| `src/PeopleCore.Application/Payroll/Interfaces/IPayrollRunRepository.cs` + implementation (modify) | `CountUnpaidRunsPaidInYearAsync` |
| `src/PeopleCore.API/Controllers/Payroll/GovernmentReportsController.cs` (modify) | Optional month; annual dispatch |
| `src/PeopleCore.Web/Services/ApiClient.cs` (modify) | Section DTO, annual calls, 2316 inputs call |
| `src/PeopleCore.Web/Pages/Payroll/GovernmentReports.razor` (modify) | 1604-C tab, year picker, sections |
| `src/PeopleCore.Web/Pages/Payroll/Bir2316.razor` (modify) | Fill the form from saved inputs |

---

### Task 1: Saved 2316 inputs: entity, table, repository

**Files:**
- Create: `src/PeopleCore.Domain/Entities/Payroll/Bir2316Inputs.cs`
- Create: `src/PeopleCore.Infrastructure/Persistence/Configurations/Payroll/Bir2316InputsConfiguration.cs`
- Create: `src/PeopleCore.Application/Payroll/Interfaces/IBir2316InputsRepository.cs`
- Create: `src/PeopleCore.Infrastructure/Persistence/Repositories/Bir2316InputsRepository.cs`
- Modify: `src/PeopleCore.Infrastructure/Persistence/AppDbContext.cs` (add the DbSet next to `PayrollRunPremiumDays`)
- Modify: `src/PeopleCore.API/Extensions/ServiceExtensions.cs:212` (register after `IEmployeeCompensationRepository`)
- Generate: migration `AddBir2316Inputs`
- Test: `tests/PeopleCore.Infrastructure.Tests/Payroll/Bir2316InputsRepositoryTests.cs`

**Interfaces:**
- Produces:
  - `Bir2316Inputs` entity (Domain): `Guid EmployeeId, int Year`, plus the eleven `Bir2316ManualInputs` fields with the same names and types (the four `PrevEmployer*` fields as `string?`; the seven others as `decimal`).
  - Extension methods in Application, `Bir2316InputsMapping`: `Bir2316ManualInputs ToManualInputs(this Bir2316Inputs e)` and `void Apply(this Bir2316Inputs e, Bir2316ManualInputs m)`.
  - `IBir2316InputsRepository`:
    - `Task<Bir2316Inputs?> GetAsync(Guid employeeId, int year, CancellationToken ct = default)`
    - `Task<IReadOnlyDictionary<Guid, Bir2316Inputs>> GetForYearAsync(int year, CancellationToken ct = default)`
    - `Task SaveAsync(Guid employeeId, int year, Bir2316ManualInputs inputs, CancellationToken ct = default)`

`Bir2316ManualInputs` lives in `PeopleCore.Application.Payroll.DTOs`, and the Domain project cannot reference Application. So the entity stores plain fields, and the conversions live in the Application mapper `src/PeopleCore.Application/Payroll/Services/Bir2316InputsMapping.cs`.

- [ ] **Step 1: Write the failing repository tests**

```csharp
using FluentAssertions;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Payroll;

public class Bir2316InputsRepositoryTests : DatabaseTestBase
{
    public Bir2316InputsRepositoryTests(PostgresFixture fixture) : base(fixture) { }

    private async Task<Guid> AnEmployeeIdAsync()
    {
        var employee = AnEmployee();
        Context.Employees.Add(employee);
        await Context.SaveChangesAsync();
        return employee.Id;
    }

    private static Bir2316ManualInputs Inputs(decimal prevTaxable) => new()
    {
        PrevEmployerTin = "111-222-333-000", PrevEmployerName = "Old Co.",
        Item22_PrevTaxableCompensation = prevTaxable, Item25B_PrevTaxWithheld = 1_200m
    };

    [Fact]
    public async Task Save_ThenGet_ReturnsTheInputs()
    {
        var id = await AnEmployeeIdAsync();

        await new Bir2316InputsRepository(NewContext()).SaveAsync(id, 2026, Inputs(50_000m));

        var saved = await new Bir2316InputsRepository(NewContext()).GetAsync(id, 2026);
        saved!.PrevEmployerName.Should().Be("Old Co.");
        saved.Item22_PrevTaxableCompensation.Should().Be(50_000m);
        saved.Item25B_PrevTaxWithheld.Should().Be(1_200m);
    }

    [Fact]
    public async Task Save_Twice_ReplacesRatherThanDuplicates()
    {
        var id = await AnEmployeeIdAsync();
        await new Bir2316InputsRepository(NewContext()).SaveAsync(id, 2026, Inputs(50_000m));

        await new Bir2316InputsRepository(NewContext()).SaveAsync(id, 2026, Inputs(60_000m));

        await using var reader = NewContext();
        reader.Set<PeopleCore.Domain.Entities.Payroll.Bir2316Inputs>().Count().Should().Be(1);
        (await new Bir2316InputsRepository(reader).GetAsync(id, 2026))!.Item22_PrevTaxableCompensation.Should().Be(60_000m);
    }

    [Fact]
    public async Task GetForYear_ReturnsOnlyThatYear_KeyedByEmployee()
    {
        var id = await AnEmployeeIdAsync();
        await new Bir2316InputsRepository(NewContext()).SaveAsync(id, 2025, Inputs(10_000m));
        await new Bir2316InputsRepository(NewContext()).SaveAsync(id, 2026, Inputs(20_000m));

        var year = await new Bir2316InputsRepository(NewContext()).GetForYearAsync(2026);

        year.Should().ContainSingle();
        year[id].Item22_PrevTaxableCompensation.Should().Be(20_000m);
    }

    [Fact]
    public async Task Get_WithNothingSaved_IsNull()
    {
        var id = await AnEmployeeIdAsync();

        (await new Bir2316InputsRepository(NewContext()).GetAsync(id, 2026)).Should().BeNull();
    }
}
```

The Infrastructure test project may not reference `PeopleCore.Application` directly. If `Bir2316ManualInputs` doesn't resolve, check the csproj's project references. It references Infrastructure, which references Application, so the type should resolve transitively.

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test tests/PeopleCore.Infrastructure.Tests --filter "FullyQualifiedName~Bir2316InputsRepositoryTests" -nodeReuse:false -p:UseSharedCompilation=false`
Expected: build error; `Bir2316InputsRepository` does not exist.

- [ ] **Step 3: Write the entity**

```csharp
namespace PeopleCore.Domain.Entities.Payroll;

/// <summary>
/// The BIR 2316 fields a person supplies for one employee and year - a previous employer's
/// figures, a PERA credit, de minimis - saved so a single 2316, "Generate all" and the 1604-C
/// alphalist all use the same values instead of each starting blank. Holds no derived figure:
/// a certificate's money still comes from payroll.
/// </summary>
public class Bir2316Inputs : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public int Year { get; set; }

    public string? PrevEmployerTin { get; set; }
    public string? PrevEmployerName { get; set; }
    public string? PrevEmployerAddress { get; set; }
    public string? PrevEmployerZipCode { get; set; }
    public decimal Item22_PrevTaxableCompensation { get; set; }
    public decimal Item25B_PrevTaxWithheld { get; set; }
    public decimal Item27_PeraTaxCredit { get; set; }
    public decimal Item35_DeMinimis { get; set; }
    public decimal Item33_HazardPayMwe { get; set; }
    public decimal StatutoryMinWagePerDay { get; set; }
    public decimal StatutoryMinWagePerMonth { get; set; }
}
```

- [ ] **Step 4: Write the mapper** in `src/PeopleCore.Application/Payroll/Services/Bir2316InputsMapping.cs`

```csharp
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Domain.Entities.Payroll;

namespace PeopleCore.Application.Payroll.Services;

/// <summary>Between the saved row and the request-shaped <see cref="Bir2316ManualInputs"/>.</summary>
public static class Bir2316InputsMapping
{
    public static Bir2316ManualInputs ToManualInputs(this Bir2316Inputs e) => new()
    {
        PrevEmployerTin = e.PrevEmployerTin,
        PrevEmployerName = e.PrevEmployerName,
        PrevEmployerAddress = e.PrevEmployerAddress,
        PrevEmployerZipCode = e.PrevEmployerZipCode,
        Item22_PrevTaxableCompensation = e.Item22_PrevTaxableCompensation,
        Item25B_PrevTaxWithheld = e.Item25B_PrevTaxWithheld,
        Item27_PeraTaxCredit = e.Item27_PeraTaxCredit,
        Item35_DeMinimis = e.Item35_DeMinimis,
        Item33_HazardPayMwe = e.Item33_HazardPayMwe,
        StatutoryMinWagePerDay = e.StatutoryMinWagePerDay,
        StatutoryMinWagePerMonth = e.StatutoryMinWagePerMonth
    };

    public static void Apply(this Bir2316Inputs e, Bir2316ManualInputs m)
    {
        e.PrevEmployerTin = m.PrevEmployerTin;
        e.PrevEmployerName = m.PrevEmployerName;
        e.PrevEmployerAddress = m.PrevEmployerAddress;
        e.PrevEmployerZipCode = m.PrevEmployerZipCode;
        e.Item22_PrevTaxableCompensation = m.Item22_PrevTaxableCompensation;
        e.Item25B_PrevTaxWithheld = m.Item25B_PrevTaxWithheld;
        e.Item27_PeraTaxCredit = m.Item27_PeraTaxCredit;
        e.Item35_DeMinimis = m.Item35_DeMinimis;
        e.Item33_HazardPayMwe = m.Item33_HazardPayMwe;
        e.StatutoryMinWagePerDay = m.StatutoryMinWagePerDay;
        e.StatutoryMinWagePerMonth = m.StatutoryMinWagePerMonth;
    }
}
```

Check that `Bir2316ManualInputs` has no field beyond these ten. If it has more, add each one to the entity, the mapper and the configuration. There is a `Bir2316ServiceTests` test that pins that record's field list by name; read it.

- [ ] **Step 5: Write the configuration, repository interface and repository**

```csharp
// Bir2316InputsConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Payroll;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Payroll;

public class Bir2316InputsConfiguration : IEntityTypeConfiguration<Bir2316Inputs>
{
    public void Configure(EntityTypeBuilder<Bir2316Inputs> builder)
    {
        builder.HasKey(x => x.Id);

        // One set of inputs per employee per year.
        builder.HasIndex(x => new { x.EmployeeId, x.Year }).IsUnique();

        builder.Property(x => x.PrevEmployerTin).HasMaxLength(20);
        builder.Property(x => x.PrevEmployerName).HasMaxLength(200);
        builder.Property(x => x.PrevEmployerAddress).HasMaxLength(500);
        builder.Property(x => x.PrevEmployerZipCode).HasMaxLength(10);
        builder.Property(x => x.Item22_PrevTaxableCompensation).HasColumnType("numeric(18,2)");
        builder.Property(x => x.Item25B_PrevTaxWithheld).HasColumnType("numeric(18,2)");
        builder.Property(x => x.Item27_PeraTaxCredit).HasColumnType("numeric(18,2)");
        builder.Property(x => x.Item35_DeMinimis).HasColumnType("numeric(18,2)");
        builder.Property(x => x.Item33_HazardPayMwe).HasColumnType("numeric(18,2)");
        builder.Property(x => x.StatutoryMinWagePerDay).HasColumnType("numeric(18,2)");
        builder.Property(x => x.StatutoryMinWagePerMonth).HasColumnType("numeric(18,2)");

        builder.HasOne<PeopleCore.Domain.Entities.Employees.Employee>()
               .WithMany()
               .HasForeignKey(x => x.EmployeeId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
```

Check the maximum lengths against `Bir2316ManualInputsValidator`, and match whatever limits it enforces.

```csharp
// IBir2316InputsRepository.cs
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Domain.Entities.Payroll;

namespace PeopleCore.Application.Payroll.Interfaces;

public interface IBir2316InputsRepository
{
    Task<Bir2316Inputs?> GetAsync(Guid employeeId, int year, CancellationToken ct = default);

    /// <summary>Every saved set for the year, keyed by employee.</summary>
    Task<IReadOnlyDictionary<Guid, Bir2316Inputs>> GetForYearAsync(int year, CancellationToken ct = default);

    /// <summary>Saves the inputs for the employee and year, replacing any saved before.</summary>
    Task SaveAsync(Guid employeeId, int year, Bir2316ManualInputs inputs, CancellationToken ct = default);
}
```

```csharp
// Bir2316InputsRepository.cs
using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Domain.Entities.Payroll;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class Bir2316InputsRepository : IBir2316InputsRepository
{
    private readonly AppDbContext _context;

    public Bir2316InputsRepository(AppDbContext context) => _context = context;

    public async Task<Bir2316Inputs?> GetAsync(Guid employeeId, int year, CancellationToken ct = default)
        => await _context.Bir2316Inputs.FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.Year == year, ct);

    public async Task<IReadOnlyDictionary<Guid, Bir2316Inputs>> GetForYearAsync(int year, CancellationToken ct = default)
        => await _context.Bir2316Inputs.Where(x => x.Year == year).ToDictionaryAsync(x => x.EmployeeId, ct);

    public async Task SaveAsync(Guid employeeId, int year, Bir2316ManualInputs inputs, CancellationToken ct = default)
    {
        var row = await GetAsync(employeeId, year, ct);
        if (row is null)
        {
            row = new Bir2316Inputs { EmployeeId = employeeId, Year = year };
            _context.Bir2316Inputs.Add(row);
        }

        row.Apply(inputs);
        await _context.SaveChangesAsync(ct);
    }
}
```

Add `public DbSet<Bir2316Inputs> Bir2316Inputs => Set<Bir2316Inputs>();` to `AppDbContext`. Register `services.AddScoped<IBir2316InputsRepository, Bir2316InputsRepository>();`.

- [ ] **Step 6: Generate the migration**

Run: `DataProtection__KeyEncryptionKey=design-time-only-not-a-secret-0123456789 ASPNETCORE_ENVIRONMENT=Development dotnet ef migrations add AddBir2316Inputs --project src/PeopleCore.Infrastructure --startup-project src/PeopleCore.API`
Expected: `Done.` Open the migration. It should only create `bir2316_inputs` with its unique index and foreign key. If it touches anything else, stop and report.

- [ ] **Step 7: Run the tests and confirm they pass**

Run: the command from Step 2. Expected: all 4 pass.

- [ ] **Step 8: Commit**

```bash
git add src tests
git commit -m "feat(payroll): save each employee's BIR 2316 manual inputs per year"
```

---

### Task 2: The 2316 uses the saved inputs

**Files:**
- Modify: `src/PeopleCore.Application/Payroll/Interfaces/IBir2316Service.cs`
- Modify: `src/PeopleCore.Application/Payroll/Services/Bir2316Service.cs` (constructor; `GetPreviewAsync`; `BuildAllAsync`'s `new Bir2316ManualInputs()`; new methods)
- Modify: `src/PeopleCore.API/Controllers/Payroll/Bir2316Controller.cs` (Generate; new inputs endpoint; the doc comment on GenerateAll that says the inputs come back blank)
- Test: `tests/PeopleCore.Application.Tests/Payroll/Bir2316ServiceTests.cs` (update the constructor at line 38; add tests)
- Test: `tests/PeopleCore.Application.Tests/Payroll/Bir2316AuthorizationTests.cs` (add `GetInputs` to the action list if the class lists every action)

**Interfaces:**
- Consumes: `IBir2316InputsRepository`, `Bir2316InputsMapping.ToManualInputs()` (Task 1).
- Produces:
  - `Task<Bir2316Dto?> GenerateAsync(Guid employeeId, int year, Bir2316ManualInputs manual, CancellationToken ct = default)`: validates, builds and, when the employee has paid runs, saves the inputs. Returns null otherwise.
  - `Task<Bir2316ManualInputs> GetInputsAsync(Guid employeeId, int year, CancellationToken ct = default)`: the saved inputs, or `new Bir2316ManualInputs()`.
  - `GetPreviewAsync` and `BuildAllAsync` read saved inputs.
  - `GET api/reports/2316/inputs/{employeeId}?year=` returns `Bir2316ManualInputs`.

- [ ] **Step 1: Write the failing tests** in `Bir2316ServiceTests.cs`. Add `private readonly Mock<IBir2316InputsRepository> _inputsRepo = new();`, pass `_inputsRepo.Object` as the new last constructor argument, and add:

```csharp
    [Fact]
    public async Task GenerateAsync_SavesTheInputsItWasGenerated_With()
    {
        // Arrange an employee with a paid run in 2026, the same way the existing BuildAsync tests
        // do (reuse their setup helper).
        var inputs = new Bir2316ManualInputs { PrevEmployerName = "Old Co.", Item22_PrevTaxableCompensation = 50_000m };

        var dto = await _sut.GenerateAsync(EmployeeId, 2026, inputs);

        dto.Should().NotBeNull();
        _inputsRepo.Verify(r => r.SaveAsync(EmployeeId, 2026, inputs, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BuildAsync_SavesNothing()
    {
        await _sut.BuildAsync(EmployeeId, 2026, new Bir2316ManualInputs { Item27_PeraTaxCredit = 100m });

        _inputsRepo.Verify(r => r.SaveAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<Bir2316ManualInputs>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GenerateAsync_RefusesInvalidInputs_BeforeSavingAnything()
    {
        var act = () => _sut.GenerateAsync(EmployeeId, 2026, new Bir2316ManualInputs { PrevEmployerTin = "not-a-tin" });

        await act.Should().ThrowAsync<DomainException>();
        _inputsRepo.Verify(r => r.SaveAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<Bir2316ManualInputs>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetPreviewAsync_UsesTheSavedInputs()
    {
        _inputsRepo.Setup(r => r.GetAsync(EmployeeId, 2026, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new Bir2316Inputs { EmployeeId = EmployeeId, Year = 2026, Item22_PrevTaxableCompensation = 50_000m });

        var dto = await _sut.GetPreviewAsync(EmployeeId, 2026);

        dto!.Item22_PrevTaxableCompensation.Should().Be(50_000m);
    }

    [Fact]
    public async Task BuildAllAsync_UsesEachEmployeesSavedInputs()
    {
        _inputsRepo.Setup(r => r.GetForYearAsync(2026, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new Dictionary<Guid, Bir2316Inputs>
                   {
                       [EmployeeId] = new() { EmployeeId = EmployeeId, Year = 2026, Item25B_PrevTaxWithheld = 1_200m }
                   });

        var forms = await _sut.BuildAllAsync(2026);

        forms.Single(f => f.EmployeeTin != null).Item25B_PrevTaxWithheld.Should().Be(1_200m);
    }

    [Fact]
    public async Task GetInputsAsync_WithNothingSaved_IsEmpty()
    {
        (await _sut.GetInputsAsync(EmployeeId, 2026)).Should().BeEquivalentTo(new Bir2316ManualInputs());
    }
```

`EmployeeId` and the arrange steps are placeholders for the existing test class's own employee and paid-run setup. Read the class and reuse its helpers and constants, so each test really has a paid 2026 run. Make the TIN in `GenerateAsync_RefusesInvalidInputs...` one that `Bir2316ManualInputsValidator` actually rejects; check the validator. Moq's default `GetAsync` returns null, so unrelated existing tests keep building with empty inputs.

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test tests/PeopleCore.Application.Tests --filter "FullyQualifiedName~Bir2316" -nodeReuse:false -p:UseSharedCompilation=false`
Expected: build error; `GenerateAsync` does not exist.

- [ ] **Step 3: Implement.** In `Bir2316Service`:
  - Add the `IBir2316InputsRepository inputsRepo` constructor parameter, last.
  - Change `GetPreviewAsync` to `BuildAsync(employeeId, year, (await _inputsRepo.GetAsync(employeeId, year, ct))?.ToManualInputs() ?? new(), ct)`.
  - In `BuildAllAsync`, load `var saved = await _inputsRepo.GetForYearAsync(year, ct);` once. Replace `new Bir2316ManualInputs()` with `saved.TryGetValue(employeeId, out var s) ? s.ToManualInputs() : new Bir2316ManualInputs()`.
  - Add the two new methods:

```csharp
    /// <summary>
    /// Builds the certificate with the inputs HR entered and, when the employee was paid in the
    /// year, saves them - so the next preview, "Generate all" and the 1604-C alphalist use the same
    /// figures. <see cref="BuildAsync"/> itself saves nothing.
    /// </summary>
    public async Task<Bir2316Dto?> GenerateAsync(Guid employeeId, int year, Bir2316ManualInputs manual, CancellationToken ct = default)
    {
        var dto = await BuildAsync(employeeId, year, manual, ct);
        if (dto is not null)
            await _inputsRepo.SaveAsync(employeeId, year, manual, ct);
        return dto;
    }

    public async Task<Bir2316ManualInputs> GetInputsAsync(Guid employeeId, int year, CancellationToken ct = default)
        => (await _inputsRepo.GetAsync(employeeId, year, ct))?.ToManualInputs() ?? new Bir2316ManualInputs();
```

`BuildAsync` validates first, so invalid inputs throw before `SaveAsync`.

Add both methods to `IBir2316Service`, and update the doc comments on `GetPreviewAsync`: it now uses saved inputs, not blank ones.

In `Bir2316Controller`:
  - `Generate` calls `_service.GenerateAsync(...)` instead of `BuildAsync`.
  - Update the doc comments on `Generate` and `GenerateAll`. `GenerateAll` now uses saved inputs, not empty ones.
  - Add:

```csharp
    /// <summary>The 2316 inputs saved for the employee and year, or empty ones when none are.</summary>
    [HttpGet("inputs/{employeeId:guid}")]
    public async Task<ActionResult<Bir2316ManualInputs>> GetInputs(Guid employeeId, [FromQuery] int year, CancellationToken ct = default)
        => Ok(await _service.GetInputsAsync(employeeId, year, ct));
```

If `Bir2316AuthorizationTests.EveryAction_CarriesNoActionLevelAuthorizeOfItsOwn` lists actions by name, add `"GetInputs"`. If `PermissionEquivalenceTests` in the Web tests or elsewhere pins every API route, add the new route there as well; run the whole solution's tests to find out.

- [ ] **Step 4: Run the tests and confirm they pass**

Run: the command from Step 2, then `dotnet test PeopleCore.slnx -nodeReuse:false -p:UseSharedCompilation=false`. Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(payroll): generating a 2316 saves its inputs, and the preview and Generate all use them"
```

---

### Task 3: Report sections in the DTO and the CSV

**Files:**
- Modify: `src/PeopleCore.Application/Payroll/GovernmentReports/GovernmentReportDtos.cs`
- Modify: `src/PeopleCore.Application/Payroll/GovernmentReports/GovernmentReportCsv.cs`
- Modify: every place that constructs `GovernmentReportDto`. Find them with `grep -rn "new GovernmentReportDto(" src tests`; at minimum that is `GovernmentReportService.cs`, `GovernmentReportCsvTests.cs` and `GovernmentReportsControllerTests.cs`.
- Test: `tests/PeopleCore.Application.Tests/Payroll/GovernmentReportCsvTests.cs`

**Interfaces:**
- Produces:
  - `GovernmentReportSectionDto(string Title, IReadOnlyList<string> Columns, IReadOnlyList<GovernmentReportRowDto> Rows, IReadOnlyList<string> Totals, string EmptyMessage)`.
  - `GovernmentReportDto` gains a final parameter `IReadOnlyList<GovernmentReportSectionDto> Sections` (the monthly reports pass `[]`).
  - `GovernmentReportDto` also gains `bool IsAnnual => Month == 0`. The annual report uses `Month = 0`.
  - `GovernmentReportCsv.FileName` returns `<report>-<yyyy>.csv` when `IsAnnual`.

- [ ] **Step 1: Write the failing tests** (add to `GovernmentReportCsvTests`)

```csharp
    [Fact]
    public void Write_PutsEachSectionUnderItsTitle_WithABlankLineBetween()
    {
        var report = new GovernmentReportDto(
            "1604c", "BIR 1604-C alphalist", 2026, 0, "Paid in 2026",
            new GovernmentReportEmployerDto("Acme", null, "123-456-789-000", "050", "123-456-789-000"),
            [], [], [], [], [],
            [
                new GovernmentReportSectionDto("Terminated before December 31", ["Employee", "Tax due"],
                    [new GovernmentReportRowDto(Guid.NewGuid(), ["Cruz, Juan", "1000.00"], false)], ["Total", "1000.00"], "No employees in this group."),
                new GovernmentReportSectionDto("Employed as of December 31, with previous employer", ["Employee", "Tax due"],
                    [], [], "No employees in this group.")
            ]);

        var text = Encoding.UTF8.GetString(GovernmentReportCsv.Write(report)[3..]);

        text.Should().Contain("\r\nTerminated before December 31\r\nEmployee,Tax due\r\n\"Cruz, Juan\",1000.00\r\nTotal,1000.00\r\n\r\n");
        text.Should().EndWith("Employed as of December 31, with previous employer\r\nNo employees in this group.\r\n");
    }

    [Fact]
    public void FileName_ForAnAnnualReport_HasNoMonth()
    {
        var report = new GovernmentReportDto("1604c", "BIR 1604-C alphalist", 2026, 0, "Paid in 2026",
            new GovernmentReportEmployerDto("Acme", null, "", null, ""), [], [], [], [], [], []);

        GovernmentReportCsv.FileName(report).Should().Be("1604c-2026.csv");
    }
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test tests/PeopleCore.Application.Tests --filter "FullyQualifiedName~GovernmentReport" -nodeReuse:false -p:UseSharedCompilation=false`
Expected: build error.

- [ ] **Step 3: Implement.** Add to `GovernmentReportDtos.cs`:

```csharp
/// <summary>
/// One table of a report that has several - the 1604-C alphalist's groups. A report with a single
/// table (every monthly report) uses the top-level Columns, Rows and Totals and no sections.
/// </summary>
public sealed record GovernmentReportSectionDto(
    string Title,
    IReadOnlyList<string> Columns,
    IReadOnlyList<GovernmentReportRowDto> Rows,
    IReadOnlyList<string> Totals,
    string EmptyMessage);
```

Add `IReadOnlyList<GovernmentReportSectionDto> Sections` as the last parameter of `GovernmentReportDto`, and give the record a body with `public bool IsAnnual => Month == 0;`. Pass `[]` at every existing construction site.

In `GovernmentReportCsv`:
  - `FileName` returns `report.IsAnnual ? $"{report.Report}-{report.Year:D4}.csv" : <current>`.
  - In `Write`, write the top-level table only when `report.Columns.Count > 0`.
  - After the summary block, write each section:

```csharp
        foreach (var section in report.Sections)
        {
            Line();
            Line(section.Title);
            if (section.Rows.Count == 0)
            {
                Line(section.EmptyMessage);
                continue;
            }
            Line([.. section.Columns]);
            foreach (var row in section.Rows)
                Line([.. row.Cells]);
            Line([.. section.Totals]);
        }
```

The expected text in the first test assumes the header block is followed by the sections, with the blank line coming from `Line()`. Adjust the assertions only if the existing header layout differs, and keep what they check.

- [ ] **Step 4: Run the tests and confirm they pass**

Run: the command from Step 2. Expected: all pass, the existing CSV and service tests included.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(payroll): a government report can carry several titled tables"
```

---

### Task 4: The alphalist builder

**Files:**
- Create: `src/PeopleCore.Application/Payroll/GovernmentReports/Bir1604CAlphalist.cs`
- Test: `tests/PeopleCore.Application.Tests/Payroll/Bir1604CAlphalistTests.cs`

**Interfaces:**
- Consumes: `Bir2316Dto` (`PeopleCore.Application.Payroll.DTOs`), `GovernmentReportDto` / `GovernmentReportSectionDto` / `GovernmentReportRowDto` / `GovernmentReportEmployerDto` (Task 3), `GovernmentReportMath.Money`.
- Produces: the record `Bir1604CAlphalist.Person(Guid EmployeeId, Bir2316Dto Form, DateOnly HireDate, DateOnly? SeparationDate, decimal TaxWithheldJanToNov, decimal TaxWithheldDecember)`, and `Bir1604CAlphalist.Build(int year, GovernmentReportEmployerDto employer, IReadOnlyList<Person> people, int unpaidRuns) : GovernmentReportDto`.

The builder is pure: the service (Task 5) gathers the inputs.

- [ ] **Step 1: Write the failing tests**

```csharp
using FluentAssertions;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.GovernmentReports;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class Bir1604CAlphalistTests
{
    private static readonly GovernmentReportEmployerDto Employer = new("Acme", null, "123-456-789-000", "050", "123-456-789-000");

    private static Bir2316Dto Form(string last, string first, string tin = "111-222-333-000",
        decimal basic = 400_000m, decimal presentWithheld = 30_000m, decimal prevTaxable = 0m, decimal prevWithheld = 0m) => new()
    {
        Year = 2026, EmployeeTin = tin, EmployeeLastName = last, EmployeeFirstName = first, EmployeeMiddleName = "",
        Item39_BasicSalary = basic, Item36_SssPhicPagibigContributions = 20_000m, Item34_ThirteenthMonthAndBenefits = 33_333.33m,
        Item48_TaxableThirteenthMonth = 0m, Item25A_PresentTaxWithheld = presentWithheld,
        Item22_PrevTaxableCompensation = prevTaxable, Item25B_PrevTaxWithheld = prevWithheld
    };

    private static Bir1604CAlphalist.Person Person(Bir2316Dto form, DateOnly? separated = null, DateOnly? hired = null,
        decimal janToNov = 27_500m, decimal december = 2_500m)
        => new(Guid.NewGuid(), form, hired ?? new DateOnly(2020, 1, 6), separated, janToNov, december);

    [Fact]
    public void GroupsEmployees_TheWayBirSchedulesDo()
    {
        var left = Person(Form("Reyes", "Ana"), separated: new DateOnly(2026, 6, 30));
        var stayedToYearEnd = Person(Form("Cruz", "Juan"), separated: new DateOnly(2026, 12, 31));
        var withPrevious = Person(Form("Santos", "Maria", prevTaxable: 80_000m, prevWithheld: 4_000m));

        var report = Bir1604CAlphalist.Build(2026, Employer, [left, stayedToYearEnd, withPrevious], unpaidRuns: 0);

        report.Sections.Select(s => s.Title).Should().Equal(
            "Terminated before December 31",
            "Employed as of December 31, no previous employer",
            "Employed as of December 31, with previous employer");
        report.Sections[0].Rows.Select(r => r.Cells[1]).Should().Equal("Reyes");
        report.Sections[1].Rows.Select(r => r.Cells[1]).Should().Equal(new[] { "Cruz" }, "separated on December 31 isn't 'before'");
        report.Sections[2].Rows.Select(r => r.Cells[1]).Should().Equal("Santos");
    }

    [Fact]
    public void ARow_ShowsThe2316sFigures_TheWithheldSplit_AndWhatIsLeftToCollect()
    {
        // 2316 derived totals: gross = non-taxable (33,333.33 + 20,000) + taxable (400,000) = 453,333.33.
        var form = Form("Cruz", "Juan");
        var report = Bir1604CAlphalist.Build(2026, Employer, [Person(form)], unpaidRuns: 0);

        var columns = report.Sections[1].Columns;
        var cells = report.Sections[1].Rows.Single().Cells;
        string Cell(string column) => cells[columns.ToList().IndexOf(column)];

        Cell("TIN").Should().Be("111-222-333-000");
        Cell("Employed from").Should().Be("2026-01-01", "clamped to the year");
        Cell("Employed to").Should().Be("2026-12-31");
        Cell("Gross compensation").Should().Be(GovernmentReportMath.Money(form.Item19_GrossCompensation));
        Cell("Total non-taxable").Should().Be(GovernmentReportMath.Money(form.Item38_TotalNonTaxable));
        Cell("Basic salary").Should().Be("400000.00");
        Cell("Other taxable compensation").Should().Be(
            GovernmentReportMath.Money(form.Item52_TotalTaxableCompensation - form.Item39_BasicSalary - form.Item48_TaxableThirteenthMonth));
        Cell("Tax due").Should().Be(GovernmentReportMath.Money(form.Item24_TaxDue));
        Cell("Tax withheld, January to November").Should().Be("27500.00");
        Cell("Tax withheld, December").Should().Be("2500.00");
        Cell("Total tax withheld").Should().Be(GovernmentReportMath.Money(form.Item26_TotalTaxWithheld));
        Cell("To collect / (refund)").Should().Be(GovernmentReportMath.Money(form.Item24_TaxDue - form.Item26_TotalTaxWithheld));
    }

    [Fact]
    public void OnlyTheWithPreviousEmployerGroup_HasThePreviousEmployerColumns()
    {
        var report = Bir1604CAlphalist.Build(2026, Employer,
            [Person(Form("Santos", "Maria", prevTaxable: 80_000m, prevWithheld: 4_000m))], unpaidRuns: 0);

        report.Sections[2].Columns.Should().Contain(["Previous employer's taxable compensation", "Previous employer's tax withheld"]);
        report.Sections[1].Columns.Should().NotContain("Previous employer's taxable compensation");
        var cells = report.Sections[2].Rows.Single().Cells;
        cells[report.Sections[2].Columns.ToList().IndexOf("Previous employer's tax withheld")].Should().Be("4000.00");
    }

    [Fact]
    public void EachGroup_HasATotalsRow_AndAnEmptyGroupSaysSo()
    {
        var report = Bir1604CAlphalist.Build(2026, Employer,
            [Person(Form("Cruz", "Juan", basic: 100_000m)), Person(Form("Dizon", "Rosa", basic: 200_000m))], unpaidRuns: 0);

        var group = report.Sections[1];
        group.Totals[0].Should().Be("Total");
        group.Totals[group.Columns.ToList().IndexOf("Basic salary")].Should().Be("300000.00");
        report.Sections[0].Rows.Should().BeEmpty();
        report.Sections[0].EmptyMessage.Should().Be("No employees in this group.");
    }

    [Fact]
    public void RowsAreSortedByLastNameThenFirstName()
    {
        var report = Bir1604CAlphalist.Build(2026, Employer,
            [Person(Form("Santos", "Ana")), Person(Form("Cruz", "Juan")), Person(Form("Cruz", "Ben"))], unpaidRuns: 0);

        report.Sections[1].Rows.Select(r => $"{r.Cells[1]}, {r.Cells[2]}").Should().Equal("Cruz, Ben", "Cruz, Juan", "Santos, Ana");
    }

    [Fact]
    public void Warns_AboutMissingTins_UnpaidRuns_AndHiresWithNoPreviousEmployer()
    {
        var noTin = Person(Form("Cruz", "Juan", tin: ""));
        var hiredThisYear = Person(Form("Reyes", "Ana"), hired: new DateOnly(2026, 4, 1));

        var report = Bir1604CAlphalist.Build(2026, Employer, [noTin, hiredThisYear], unpaidRuns: 2);

        report.Sections[1].Rows.Single(r => r.Cells[1] == "Cruz").MissingNumber.Should().BeTrue();
        report.Warnings.Should().Contain("1 employee has no TIN.");
        report.Warnings.Should().Contain("2 payroll runs paid this year aren't paid yet and aren't included.");
        report.Warnings.Should().Contain(
            "1 employee hired this year has no previous employer entered. If they worked elsewhere earlier in the year, add it on their BIR 2316 so they move to the right group.");
        report.Warnings.Should().Contain(w => w.Contains("minimum wage earner"));
    }

    [Fact]
    public void TheReport_IsAnnual_WithTheAlphalistKey()
    {
        var report = Bir1604CAlphalist.Build(2026, Employer, [], unpaidRuns: 0);

        report.Report.Should().Be("1604c");
        report.IsAnnual.Should().BeTrue();
        report.Basis.Should().Be("Paid in 2026");
        report.Columns.Should().BeEmpty();
    }
}
```

The `Bir2316Dto` item properties above must match that class's real property names. They were copied from `Bir2316Dtos.cs`. If one is get-only (computed), don't set it in `Form`: set its inputs, as the test already does for Items 19, 24, 26, 38 and 52.

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test tests/PeopleCore.Application.Tests --filter "FullyQualifiedName~Bir1604CAlphalistTests" -nodeReuse:false -p:UseSharedCompilation=false`
Expected: build error; `Bir1604CAlphalist` does not exist.

- [ ] **Step 3: Implement**

```csharp
using System.Globalization;
using PeopleCore.Application.Payroll.DTOs;
using static PeopleCore.Application.Payroll.GovernmentReports.GovernmentReportMath;

namespace PeopleCore.Application.Payroll.GovernmentReports;

/// <summary>
/// The BIR 1604-C alphalist: each employee's BIR 2316 for the year, one line each, grouped the way
/// BIR's schedules group them. Built from the certificates rather than from payroll again, so the
/// two can never disagree. Minimum wage earners have their own BIR schedule, which PeopleCore
/// doesn't model yet; the report says so rather than leaving it out silently.
/// </summary>
public static class Bir1604CAlphalist
{
    public sealed record Person(Guid EmployeeId, Bir2316Dto Form, DateOnly HireDate, DateOnly? SeparationDate,
        decimal TaxWithheldJanToNov, decimal TaxWithheldDecember);

    private const string EmptyGroup = "No employees in this group.";

    private static readonly string[] Leading =
        ["TIN", "Last name", "First name", "Middle name", "Employed from", "Employed to",
         "Gross compensation", "13th month and other benefits (non-taxable)", "De minimis",
         "SSS, PhilHealth and Pag-IBIG employee shares", "Other non-taxable compensation", "Total non-taxable",
         "Basic salary", "13th month and other benefits (taxable)", "Other taxable compensation",
         "Total taxable (present employer)"];

    private static readonly string[] Previous =
        ["Previous employer's taxable compensation", "Previous employer's tax withheld"];

    private static readonly string[] Trailing =
        ["Tax due", "Tax withheld, January to November", "Tax withheld, December", "Total tax withheld",
         "To collect / (refund)"];

    public static GovernmentReportDto Build(int year, GovernmentReportEmployerDto employer,
        IReadOnlyList<Person> people, int unpaidRuns)
    {
        var yearEnd = new DateOnly(year, 12, 31);
        bool Terminated(Person p) => p.SeparationDate is { } s && s.Year == year && s < yearEnd;
        bool HasPrevious(Person p) => p.Form.Item22_PrevTaxableCompensation != 0m || p.Form.Item25B_PrevTaxWithheld != 0m;

        var sorted = people
            .OrderBy(p => p.Form.EmployeeLastName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Form.EmployeeFirstName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sections = new List<GovernmentReportSectionDto>
        {
            Section("Terminated before December 31", sorted.Where(Terminated), year, withPrevious: false),
            Section("Employed as of December 31, no previous employer",
                sorted.Where(p => !Terminated(p) && !HasPrevious(p)), year, withPrevious: false),
            Section("Employed as of December 31, with previous employer",
                sorted.Where(p => !Terminated(p) && HasPrevious(p)), year, withPrevious: true)
        };

        var warnings = new List<string>();
        int noTin = people.Count(p => string.IsNullOrWhiteSpace(p.Form.EmployeeTin));
        if (noTin > 0)
            warnings.Add(noTin == 1 ? "1 employee has no TIN." : $"{noTin} employees have no TIN.");
        if (unpaidRuns > 0)
            warnings.Add(unpaidRuns == 1
                ? "1 payroll run paid this year isn't paid yet and isn't included."
                : $"{unpaidRuns} payroll runs paid this year aren't paid yet and aren't included.");
        int hiredWithoutPrevious = people.Count(p => p.HireDate.Year == year && !HasPrevious(p) && !Terminated(p));
        if (hiredWithoutPrevious > 0)
            warnings.Add((hiredWithoutPrevious == 1
                    ? "1 employee hired this year has no previous employer entered."
                    : $"{hiredWithoutPrevious} employees hired this year have no previous employer entered.")
                + " If they worked elsewhere earlier in the year, add it on their BIR 2316 so they move to the right group.");
        warnings.Add("The minimum wage earner schedule isn't included: PeopleCore doesn't record minimum wage earners yet.");

        return new GovernmentReportDto("1604c", "BIR 1604-C alphalist", year, 0, $"Paid in {year}", employer,
            [], [], [], [], warnings, sections);
    }

    private static GovernmentReportSectionDto Section(string title, IEnumerable<Person> people, int year, bool withPrevious)
    {
        var columns = Leading.Concat(withPrevious ? Previous : []).Concat(Trailing).ToList();
        var yearStart = new DateOnly(year, 1, 1);
        var yearEnd = new DateOnly(year, 12, 31);

        var rows = new List<GovernmentReportRowDto>();
        var sums = new decimal[columns.Count];
        foreach (var p in people)
        {
            var f = p.Form;
            decimal otherTaxable = f.Item52_TotalTaxableCompensation - f.Item39_BasicSalary - f.Item48_TaxableThirteenthMonth;
            var money = new List<decimal>
            {
                f.Item19_GrossCompensation, f.Item34_ThirteenthMonthAndBenefits, f.Item35_DeMinimis,
                f.Item36_SssPhicPagibigContributions, f.Item37_SalariesOtherForms, f.Item38_TotalNonTaxable,
                f.Item39_BasicSalary, f.Item48_TaxableThirteenthMonth, otherTaxable, f.Item52_TotalTaxableCompensation
            };
            if (withPrevious)
                money.AddRange([f.Item22_PrevTaxableCompensation, f.Item25B_PrevTaxWithheld]);
            money.AddRange([f.Item24_TaxDue, p.TaxWithheldJanToNov, p.TaxWithheldDecember, f.Item26_TotalTaxWithheld,
                            f.Item24_TaxDue - f.Item26_TotalTaxWithheld]);

            var from = p.HireDate > yearStart ? p.HireDate : yearStart;
            var to = p.SeparationDate is { } s && s < yearEnd ? s : yearEnd;
            var cells = new List<string>
            {
                f.EmployeeTin, f.EmployeeLastName, f.EmployeeFirstName, f.EmployeeMiddleName,
                from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            };
            for (int i = 0; i < money.Count; i++)
            {
                cells.Add(Money(money[i]));
                sums[6 + i] += money[i];
            }
            rows.Add(new GovernmentReportRowDto(p.EmployeeId, cells, string.IsNullOrWhiteSpace(f.EmployeeTin)));
        }

        var totals = columns.Select((_, i) => i == 0 ? "Total" : i < 6 ? "" : Money(sums[i])).ToList();
        return new GovernmentReportSectionDto(title, columns, rows, rows.Count == 0 ? [] : totals, EmptyGroup);
    }
}
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: the command from Step 2. Expected: all 7 pass.

- [ ] **Step 5: Commit**

```bash
git add src/PeopleCore.Application/Payroll/GovernmentReports/Bir1604CAlphalist.cs tests/PeopleCore.Application.Tests/Payroll/Bir1604CAlphalistTests.cs
git commit -m "feat(payroll): build the BIR 1604-C alphalist from each employee's 2316"
```

---

### Task 5: The alphalist behind the API

**Files:**
- Modify: `src/PeopleCore.Application/Payroll/Interfaces/IPayrollRunRepository.cs` and `src/PeopleCore.Infrastructure/Persistence/Repositories/PayrollRunRepository.cs`: add `CountUnpaidRunsPaidInYearAsync`
- Modify: `src/PeopleCore.Application/Payroll/GovernmentReports/IGovernmentReportService.cs` and `GovernmentReportService.cs`: add `BuildAnnualAsync`
- Modify: `src/PeopleCore.API/Controllers/Payroll/GovernmentReportsController.cs`
- Test: `tests/PeopleCore.Infrastructure.Tests/Payroll/PayrollRunRepositoryMonthTests.cs` (one test), `tests/PeopleCore.Application.Tests/Payroll/GovernmentReportServiceTests.cs`, `tests/PeopleCore.Application.Tests/Payroll/GovernmentReportsControllerTests.cs`

**Interfaces:**
- Consumes: `Bir1604CAlphalist.Build` and its `Person` record (Task 4); `IBir2316Service.BuildAllAsync(year)` (Task 2); `IEmployeeRepository.GetByIdsAsync(ids)`; `IPayrollRunRepository.GetPaidRunsInYearAsync(year)`.
- Produces:
  - `Task<int> CountUnpaidRunsPaidInYearAsync(int year, CancellationToken ct = default)`: runs with `PayDate` in the year and status other than Paid.
  - `Task<GovernmentReportDto> BuildAnnualAsync(string report, int year, CancellationToken ct = default)`: `1604c` only. An unknown report throws `KeyNotFoundException`. A year outside 1-9999, or after the current Philippine year, throws `DomainException`.

- [ ] **Step 1: Write the failing tests**
  - **Repository:** a Paid run and a Draft run both paid in 2026, and a Draft paid in 2025. `CountUnpaidRunsPaidInYearAsync(2026)` is 1.
  - **Service**, in `GovernmentReportServiceTests`. The service gains two constructor parameters, `IBir2316Service` and `IEmployeeRepository`; add mocks and pass them.
    - `BuildAnnualAsync("1604c", 2026)` with `BuildAllAsync(2026)` returning one form for an employee. `GetByIdsAsync` returns that employee with `HireDate` 2020-01-06 and no separation date. `GetPaidRunsInYearAsync(2026)` returns runs paid 2026-03-20 (`WithholdingTax` 2,000) and 2026-12-18 (`WithholdingTax` 500) for them.
    - The report has 3 sections. The employee's row is in "Employed as of December 31, no previous employer", with "Tax withheld, January to November" = "2000.00" and "Tax withheld, December" = "500.00".
    - `BuildAnnualAsync("sss", 2026)` throws `KeyNotFoundException`.
    - `BuildAnnualAsync("1604c", 2027)` with the clock in 2026 throws `DomainException`.
  - **Controller:** `Get("1604c", 2026, null, null)` calls `BuildAnnualAsync("1604c", 2026)` and returns Ok. `Get("sss", 2026, null, null)` throws `DomainException` ("Choose a month"). `Get("1604c", 2026, null, "csv")` returns a file named `1604c-2026.csv`.

- [ ] **Step 2: Run the tests and confirm they fail** (build errors)

Run: `dotnet test tests/PeopleCore.Application.Tests --filter "FullyQualifiedName~GovernmentReport" -nodeReuse:false -p:UseSharedCompilation=false`

- [ ] **Step 3: Implement**

Repository:

```csharp
    public async Task<int> CountUnpaidRunsPaidInYearAsync(int year, CancellationToken ct = default)
    {
        var first = new DateOnly(year, 1, 1);
        var next = first.AddYears(1);
        return await Context.PayrollRuns.CountAsync(r => r.Status != PayrollRunStatus.Paid && r.PayDate >= first && r.PayDate < next, ct);
    }
```

Interface member doc: "Runs whose pay date falls in the year that aren't Paid yet, for the 1604-C's 'not included' note."

Service. Add the constructor parameters `IBir2316Service bir2316, IEmployeeRepository employees`, and:

```csharp
    public async Task<GovernmentReportDto> BuildAnnualAsync(string report, int year, CancellationToken ct = default)
    {
        if (!string.Equals(report, "1604c", StringComparison.OrdinalIgnoreCase))
            throw new KeyNotFoundException($"There is no annual '{report}' report.");
        if (year is < 1 or > 9999)
            throw new DomainException("Choose a year.");
        var today = DateOnly.FromDateTime(PhilippineTime.Now(_clock));
        if (year > today.Year)
            throw new DomainException($"{year} hasn't started yet, so there is nothing to report.");

        var forms = await _bir2316.BuildAllAsync(year, ct);

        // January-November and December tax withheld aren't on the 2316; they come from the same
        // year's Paid runs by pay month, and add up to its present-employer tax withheld.
        var runs = await _runs.GetPaidRunsInYearAsync(year, ct);
        var withheldByEmployee = runs
            .SelectMany(r => r.Employees.Select(e => (e.EmployeeId, December: r.PayDate.Month == 12, e.WithholdingTax)))
            .GroupBy(x => x.EmployeeId)
            .ToDictionary(g => g.Key, g => (JanToNov: g.Where(x => !x.December).Sum(x => x.WithholdingTax),
                                            December: g.Where(x => x.December).Sum(x => x.WithholdingTax)));

        var employees = (await _employees.GetByIdsAsync(forms.Select(f => f.EmployeeId), ct)).ToDictionary(e => e.Id);
        var people = forms
            .Where(f => employees.ContainsKey(f.EmployeeId))
            .Select(f =>
            {
                var e = employees[f.EmployeeId];
                var withheld = withheldByEmployee.GetValueOrDefault(f.EmployeeId);
                return new Bir1604CAlphalist.Person(f.EmployeeId, f, e.HireDate, e.SeparationDate, withheld.JanToNov, withheld.December);
            })
            .ToList();

        var company = await _companies.GetDefaultAsync(ct);
        var employer = new GovernmentReportEmployerDto(company?.Name ?? "", company?.Address, company?.TIN ?? "", company?.RdoCode, company?.TIN ?? "");
        var result = Bir1604CAlphalist.Build(year, employer, people, await _runs.CountUnpaidRunsPaidInYearAsync(year, ct));

        // Company checks, worded as the monthly reports word them.
        var companyWarnings = new List<string>();
        if (company is null)
            companyWarnings.Add("The company's details are missing. Fill in the Company page.");
        else
        {
            bool tinBlank = string.IsNullOrWhiteSpace(company.TIN), rdoBlank = string.IsNullOrWhiteSpace(company.RdoCode);
            if (tinBlank && rdoBlank) companyWarnings.Add("The company's TIN and RDO code are blank. Add them on the Company page.");
            else if (tinBlank) companyWarnings.Add("The company's TIN is blank. Add it on the Company page.");
            else if (rdoBlank) companyWarnings.Add("The company's RDO code is blank. Add it on the Company page.");
        }
        return result with { Warnings = [.. companyWarnings, .. result.Warnings] };
```

`Bir2316Dto` has no employee id today. Add `public Guid EmployeeId { get; set; }` to it, set in `Bir2316Service.BuildDto` from `employee.Id`, and add a test that `BuildAllAsync`'s forms carry it. It's an extra output field only; `Bir2316ManualInputs` is untouched.

A year with no forms: `Build` returns three empty sections, and the page shows "No payroll was paid in 2026." when every section is empty (Task 6).

Check that `GovernmentReportService`'s DI registration still resolves, since `IBir2316Service` and `IEmployeeRepository` are both registered already. Check also that there's no circular dependency: `Bir2316Service` must not depend on `IGovernmentReportService`.

Controller:

```csharp
    [HttpGet("{report}")]
    public async Task<IActionResult> Get(string report, [FromQuery] int year, [FromQuery] int? month,
        [FromQuery] string? format, CancellationToken ct)
    {
        // The alphalist is annual; every other report is for one month.
        var dto = string.Equals(report, "1604c", StringComparison.OrdinalIgnoreCase)
            ? await _service.BuildAnnualAsync(report, year, ct)
            : month is { } m
                ? await _service.BuildAsync(report, year, m, ct)
                : throw new DomainException("Choose a month.");
        ...
```

Add `using PeopleCore.Domain.Exceptions;`. Update the existing controller tests to pass `(int?)3` for the month parameter.

- [ ] **Step 4: Run the tests and confirm they pass**

Run: the Step 2 command, the new repository test, then `dotnet test PeopleCore.slnx -nodeReuse:false -p:UseSharedCompilation=false`. Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(api): the BIR 1604-C alphalist from the government reports endpoint"
```

---

### Task 6: The page

**Files:**
- Modify: `src/PeopleCore.Web/Services/ApiClient.cs`
- Modify: `src/PeopleCore.Web/Pages/Payroll/GovernmentReports.razor`
- Modify: `src/PeopleCore.Web/Pages/Payroll/Bir2316.razor`
- Test: `tests/PeopleCore.Web.Tests/Pages/Payroll/GovernmentReportsTests.cs`, `tests/PeopleCore.Web.Tests/Pages/Payroll/Bir2316Tests.cs`

**Interfaces:**
- Consumes: `GET api/reports/government/1604c?year=[&format=csv]` (Task 5); `GET api/reports/2316/inputs/{employeeId}?year=` (Task 2).
- Produces:
  - `GovernmentReportSectionDto` in the Web client.
  - `GovernmentReportDto` gains the `Sections` parameter, last.
  - `ApiClient.GetGovernmentReportAsync(string report, int year, int? month)` and `GetGovernmentReportCsvAsync(string report, int year, int? month)`, where a null month leaves `&month=` off the URL.
  - `ApiClient.GetBir2316InputsAsync(Guid employeeId, int year)`, returning the Web client's own manual-inputs shape (see Step 3).

- [ ] **Step 1: Write the failing tests**

In `GovernmentReportsTests`:
  - **Tab and year:** clicking `[data-tab='1604c']` requests `/api/reports/government/1604c?year=<last year>` with no month parameter. It shows `#report-year` and no `#report-month`, and renders three `[data-report-section]` elements, each with its title.
  - **Empty group:** a section with no rows shows its empty message inside `[data-section-empty]`.
  - **Nothing paid:** when every section is empty, `[data-report-empty]` says "No payroll was paid in <year>".
  - **CSV:** Download CSV on the 1604-C tab requests `...1604c?year=<last year>&format=csv`.

In `Bir2316Tests`, choosing Maria and a year requests `/api/reports/2316/inputs/{MariaId}?year=2025`, and `#prevTin` then holds the saved TIN.

Build the JSON bodies the same way the existing tests in those files do.

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test tests/PeopleCore.Web.Tests --filter "FullyQualifiedName~GovernmentReportsTests|FullyQualifiedName~Bir2316Tests" -nodeReuse:false -p:UseSharedCompilation=false`

- [ ] **Step 3: Implement**
  - **ApiClient:**
    - add the section record `public record GovernmentReportSectionDto(string Title, IReadOnlyList<string> Columns, IReadOnlyList<GovernmentReportRowDto> Rows, IReadOnlyList<string> Totals, string EmptyMessage);`;
    - add `IReadOnlyList<GovernmentReportSectionDto> Sections` to the Web `GovernmentReportDto`;
    - make `month` an `int?` in both report methods, building the query as `?year={year}` plus `&month={m}` when it has a value;
    - add `GetBir2316InputsAsync`, reading into whatever type the page's `ManualInputsFormModel` can be filled from. Look at how `GenerateBir2316Async` sends the manual inputs, and mirror those JSON property names.
  - **GovernmentReports.razor:**
    - add `("1604c", "BIR 1604-C")` to `Tabs`;
    - when `_tab == "1604c"`, show a number input `#report-year` (default `DateTime.Today.Year - 1`, `max` = the current year) instead of `#report-month`;
    - load with `month: null`;
    - when `_report.Sections.Count > 0`, render each section in a `<section data-report-section>` with an `<h3>` title and a table with the same markup as the top-level table (factor the table into a small `RenderFragment` or component rather than copying it). An empty section shows `<p data-section-empty>@section.EmptyMessage</p>`;
    - when every section is empty, show `No payroll was paid in @_year.` in the existing `[data-report-empty]` element;
    - for 1604-C, the CSV file name is `1604c-{year}.csv`.
  - **Bir2316.razor:** in `OnYearChanged`, after `_manual = new ManualInputsFormModel();`, call `GetBir2316InputsAsync` and copy the values into `_manual`. Keep the existing error handling around it.

- [ ] **Step 4: Run the tests and confirm they pass**

Run: the Step 2 command, then `dotnet test tests/PeopleCore.Web.Tests -nodeReuse:false -p:UseSharedCompilation=false`. Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/PeopleCore.Web tests/PeopleCore.Web.Tests
git commit -m "feat(web): BIR 1604-C alphalist tab, and the 2316 page fills in saved inputs"
```

---

### Task 7: Full verification

- [ ] **Step 1:** Run `dotnet test PeopleCore.slnx -nodeReuse:false -p:UseSharedCompilation=false`. Expected: every project passes with no compiler warnings.
- [ ] **Step 2:** Confirm the migration only adds `bir2316_inputs`: `git show --stat` on the Task 1 commit, and read the migration's `Up`.
- [ ] **Step 3:** The browser check needs a signed-in payroll account. Record it as not done if no one can sign in.
