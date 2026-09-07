# Repository Tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove the payroll and 2316 repositories return the right rows from a real PostgreSQL database.

**Architecture:** A new `tests/PeopleCore.Infrastructure.Tests` project. A Testcontainers PostgreSQL container starts once per run, the migration chain builds the schema, and every test truncates first so it starts from an empty database. All database test classes share one xUnit collection, which serialises them — they share one database and must not run concurrently.

**Tech Stack:** .NET 10, xUnit 2.9.3, FluentAssertions 8.8.0, `Testcontainers.PostgreSql` 4.15.0, PostgreSQL 17 (alpine image), EF Core 10 + Npgsql.

## Global Constraints

- **One new package, in the new project only:** `Testcontainers.PostgreSql` 4.15.0. EF Core, Npgsql and `EFCore.NamingConventions` arrive transitively from the `PeopleCore.Infrastructure` project reference — do not add them explicitly.
- **xUnit 2, not 3.** `Xunit.IAsyncLifetime` here declares `Task InitializeAsync()` and `Task DisposeAsync()`. Writing `ValueTask` (the xUnit v3 signature) will not compile.
- **Schema comes from `Database.MigrateAsync()`**, never `EnsureCreated()`. The tests must run against the schema the migrations produce, not one rebuilt from the model.
- **The truncation list is derived from `Context.Model`**, never hardcoded. A hardcoded list silently stops truncating a table the day someone adds an entity.
- **Every database test class belongs to the one collection.** xUnit runs classes in a collection sequentially; two classes truncating the same database in parallel would destroy each other's rows.
- **No production code changes.** This adds tests. If a test fails, that is a finding to report, not a licence to edit `src/`.
- **Existing tests are untouched.** The 359 Application tests must still pass, unedited.

---

### Task 1: The harness

**Files:**
- Create: `tests/PeopleCore.Infrastructure.Tests/PeopleCore.Infrastructure.Tests.csproj`
- Create: `tests/PeopleCore.Infrastructure.Tests/PostgresFixture.cs`
- Create: `tests/PeopleCore.Infrastructure.Tests/DatabaseTestBase.cs`
- Create: `tests/PeopleCore.Infrastructure.Tests/HarnessTests.cs`
- Modify: `PeopleCore.slnx`

**Interfaces:**
- Produces, and **every later task depends on these exact members**:
  - `PostgresFixture` — `AppDbContext CreateContext()`, `AppDbContext CreateContext(ICurrentUserService? currentUser)`, `Task ResetAsync()`.
  - `DatabaseCollection` — `const string Name = "postgres"`.
  - `DatabaseTestBase` — protected `AppDbContext Context`, `AppDbContext NewContext()`, and the seed helpers `AnEmployee(...)`, `ACompany(...)`, `ARun(...)`, `AnEntry(...)`.

- [ ] **Step 1: Create the project file**

Create `tests/PeopleCore.Infrastructure.Tests/PeopleCore.Infrastructure.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="FluentAssertions" Version="8.8.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="Testcontainers.PostgreSql" Version="4.15.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\PeopleCore.Infrastructure\PeopleCore.Infrastructure.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Register the project in the solution**

In `PeopleCore.slnx`, add the project inside the existing `/tests/` folder so `dotnet test` discovers it:

```xml
  <Folder Name="/tests/">
    <Project Path="tests/PeopleCore.Application.Tests/PeopleCore.Application.Tests.csproj" />
    <Project Path="tests/PeopleCore.Infrastructure.Tests/PeopleCore.Infrastructure.Tests.csproj" />
  </Folder>
```

- [ ] **Step 3: Write the fixture**

Create `tests/PeopleCore.Infrastructure.Tests/PostgresFixture.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace PeopleCore.Infrastructure.Tests;

/// <summary>
/// One PostgreSQL container for the whole test run.
/// <para>
/// The schema is built by running the migrations rather than by <c>EnsureCreated</c>, so the tests
/// execute against the schema a deployment actually produces - and the migration chain gets its
/// first automated proof that it applies to an empty database.
/// </para>
/// <para>
/// The image tag is a choice, not a match: nothing in this repository records which PostgreSQL
/// version production runs. Confirm that before these tests are trusted as a deployment gate.
/// </para>
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    private string _truncateStatement = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        _truncateStatement = BuildTruncateStatement(context);
    }

    public AppDbContext CreateContext() => CreateContext(currentUser: null);

    public AppDbContext CreateContext(ICurrentUserService? currentUser)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AppDbContext(options, currentUser);
    }

    /// <summary>
    /// Empties every mapped table. Isolation is not optional: CountForYearAsync and
    /// GetPaidRunsInYearAsync do not filter by employee, so rows left by one test would change
    /// another test's count - surfacing as an order-dependent flake rather than an honest failure.
    /// </summary>
    public async Task ResetAsync()
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync(_truncateStatement);
    }

    /// <summary>
    /// Built from the model, never hardcoded - a hardcoded list stops truncating a table the day
    /// someone adds an entity, quietly reintroducing the cross-test bleed it was written to stop.
    /// One TRUNCATE over every table with CASCADE, so foreign keys impose no ordering.
    /// </summary>
    private static string BuildTruncateStatement(AppDbContext context)
    {
        var tables = context.Model.GetEntityTypes()
            .Select(entity => new { Schema = entity.GetSchema() ?? "public", Table = entity.GetTableName() })
            .Where(t => t.Table is not null)
            .Select(t => $"\"{t.Schema}\".\"{t.Table}\"")
            .Distinct()
            .ToList();

        return $"TRUNCATE TABLE {string.Join(", ", tables)} RESTART IDENTITY CASCADE;";
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

/// <summary>
/// Every database test class joins this collection. xUnit runs the classes in a collection
/// sequentially, which is required here - they share one database, and two classes truncating it
/// concurrently would delete each other's rows mid-test.
/// </summary>
[CollectionDefinition(DatabaseCollection.Name)]
public sealed class DatabaseCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
```

- [ ] **Step 4: Write the test base with seed helpers**

Create `tests/PeopleCore.Infrastructure.Tests/DatabaseTestBase.cs`:

```csharp
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Organization;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Tests;

[Collection(DatabaseCollection.Name)]
public abstract class DatabaseTestBase : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;

    protected DatabaseTestBase(PostgresFixture fixture) => _fixture = fixture;

    protected AppDbContext Context { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _fixture.ResetAsync();
        Context = _fixture.CreateContext();
    }

    public async Task DisposeAsync() => await Context.DisposeAsync();

    /// <summary>
    /// A second context over the same database. Used to read back what a repository wrote without
    /// the first context's change tracker answering from memory instead of from SQL.
    /// </summary>
    protected AppDbContext NewContext() => _fixture.CreateContext();

    protected AppDbContext NewContext(ICurrentUserService? currentUser) => _fixture.CreateContext(currentUser);

    // ── Seed helpers ─────────────────────────────────────────────────────────
    // Only the columns the schema requires are set. EmployeeNumber is unique and WorkEmail is
    // required, so both are made distinct per call - two employees in one test would otherwise
    // collide on the unique index rather than failing on the behaviour under test.

    protected static Employee AnEmployee(string lastName = "Dela Cruz", string firstName = "Juan")
        => new()
        {
            EmployeeNumber = "E" + Guid.NewGuid().ToString("N")[..8],
            FirstName = firstName,
            LastName = lastName,
            WorkEmail = Guid.NewGuid().ToString("N") + "@example.com",
            HireDate = new DateOnly(2020, 1, 1),
        };

    protected static Company ACompany(string name = "PeopleCore Inc.")
        => new() { Name = name };

    protected static PayrollRun ARun(
        string runNumber,
        DateOnly periodStart,
        DateOnly periodEnd,
        DateOnly payDate,
        PayrollRunStatus status = PayrollRunStatus.Paid)
        => new()
        {
            RunNumber = runNumber,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            PayDate = payDate,
            Frequency = PayFrequency.SemiMonthly,
            Status = status,
        };

    protected static PayrollRunEmployee AnEntry(Guid runId, Guid employeeId, decimal regularPay = 30_000m)
        => new()
        {
            PayrollRunId = runId,
            EmployeeId = employeeId,
            RegularPay = regularPay,
        };
}
```

- [ ] **Step 5: Write the harness tests**

Create `tests/PeopleCore.Infrastructure.Tests/HarnessTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace PeopleCore.Infrastructure.Tests;

public class HarnessTests : DatabaseTestBase
{
    public HarnessTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task TheMigrationChainAppliesCleanly()
    {
        // Nothing else in this repository proves the ten migrations apply to an empty database.
        // If this fails, no other test in the project is meaningful.
        var applied = await Context.Database.GetAppliedMigrationsAsync();
        var pending = await Context.Database.GetPendingMigrationsAsync();

        applied.Should().NotBeEmpty();
        pending.Should().BeEmpty("the container's schema should be fully migrated");
    }

    [Fact]
    public async Task EachTestStartsFromAnEmptyDatabase()
    {
        Context.Employees.Add(AnEmployee());
        await Context.SaveChangesAsync();

        (await Context.Employees.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task TheDatabaseIsEmptyAgainForTheNextTest()
    {
        // Paired with the test above. Whichever runs second proves the reset actually happened -
        // without it, one of these two must fail, and a silent cross-test bleed becomes visible
        // here rather than as a mysterious count in a repository test.
        (await Context.Employees.CountAsync()).Should().Be(0);

        Context.Employees.Add(AnEmployee());
        await Context.SaveChangesAsync();

        (await Context.Employees.CountAsync()).Should().Be(1);
    }
}
```

- [ ] **Step 6: Run the harness tests**

```bash
dotnet test tests/PeopleCore.Infrastructure.Tests/PeopleCore.Infrastructure.Tests.csproj
```

Expected: `Passed! - Failed: 0, Passed: 3`. The first run pulls the `postgres:17-alpine` image, so allow several minutes; later runs start the container in seconds.

If this reports that Docker is unreachable, stop and report it — the harness cannot work without a container runtime, and there is no fallback that would still be testing what this exists to test.

- [ ] **Step 7: Run the whole solution**

```bash
dotnet test PeopleCore.slnx
```

Expected: two test projects discovered, `Failed: 0`, with the Application project's 359 unchanged.

- [ ] **Step 8: Commit**

```bash
git add tests/PeopleCore.Infrastructure.Tests PeopleCore.slnx
git commit -m "test(infrastructure): add a PostgreSQL repository test harness"
```

---

### Task 2: `PayrollRunRepository` read queries

**Files:**
- Create: `tests/PeopleCore.Infrastructure.Tests/Payroll/PayrollRunRepositoryQueryTests.cs`

**Interfaces:**
- Consumes: `DatabaseTestBase` and its seed helpers, and `PostgresFixture`, from Task 1.
- Consumes: `PayrollRunRepository(AppDbContext)` from `PeopleCore.Infrastructure.Persistence.Repositories`.

- [ ] **Step 1: Write the tests**

Create `tests/PeopleCore.Infrastructure.Tests/Payroll/PayrollRunRepositoryQueryTests.cs`:

```csharp
using FluentAssertions;
using PeopleCore.Domain.Enums;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Payroll;

public class PayrollRunRepositoryQueryTests : DatabaseTestBase
{
    public PayrollRunRepositoryQueryTests(PostgresFixture fixture) : base(fixture) { }

    private PayrollRunRepository Sut => new(Context);

    [Fact]
    public async Task GetPaidRunsForEmployeeInYear_AttributesIncomeByPayDateNotPeriodStart()
    {
        // The 2316's central rule, and this design's deliberate departure from the PayZen source:
        // BIR taxes compensation in the year it is PAID. A 26 December to 10 January period paid
        // on 10 January is 2027 income, not 2026 income. Until now this was asserted only against
        // mocks, which returned whatever the test handed them - so the SQL translation of
        // PayDate.Year was never exercised at all.
        var employee = AnEmployee();
        Context.Employees.Add(employee);

        var decemberPeriodPaidInJanuary = ARun(
            "PAY-2026-024",
            periodStart: new DateOnly(2026, 12, 26),
            periodEnd: new DateOnly(2027, 1, 10),
            payDate: new DateOnly(2027, 1, 10));
        var novemberRun = ARun(
            "PAY-2026-022",
            periodStart: new DateOnly(2026, 11, 1),
            periodEnd: new DateOnly(2026, 11, 15),
            payDate: new DateOnly(2026, 11, 20));

        Context.PayrollRuns.AddRange(decemberPeriodPaidInJanuary, novemberRun);
        Context.PayrollRunEmployees.AddRange(
            AnEntry(decemberPeriodPaidInJanuary.Id, employee.Id),
            AnEntry(novemberRun.Id, employee.Id));
        await Context.SaveChangesAsync();

        var in2026 = await Sut.GetPaidRunsForEmployeeInYearAsync(employee.Id, 2026);
        var in2027 = await Sut.GetPaidRunsForEmployeeInYearAsync(employee.Id, 2027);

        in2026.Should().ContainSingle().Which.RunNumber.Should().Be("PAY-2026-022");
        in2027.Should().ContainSingle().Which.RunNumber.Should().Be("PAY-2026-024");
    }

    [Fact]
    public async Task GetPaidRunsForEmployeeInYear_ExcludesRunsThatAreNotPaid()
    {
        // A computed-but-unapproved run is not income yet, and ComputeAsync resets a run to Draft
        // on every recompute - so including one would let a tax certificate move after issue.
        var employee = AnEmployee();
        Context.Employees.Add(employee);

        var paid = ARun("PAY-2026-001", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20));
        var draft = ARun("PAY-2026-002", new(2026, 2, 1), new(2026, 2, 15), new(2026, 2, 20),
            status: PayrollRunStatus.Draft);
        var approved = ARun("PAY-2026-003", new(2026, 3, 1), new(2026, 3, 15), new(2026, 3, 20),
            status: PayrollRunStatus.Approved);

        Context.PayrollRuns.AddRange(paid, draft, approved);
        Context.PayrollRunEmployees.AddRange(
            AnEntry(paid.Id, employee.Id),
            AnEntry(draft.Id, employee.Id),
            AnEntry(approved.Id, employee.Id));
        await Context.SaveChangesAsync();

        var result = await Sut.GetPaidRunsForEmployeeInYearAsync(employee.Id, 2026);

        result.Should().ContainSingle().Which.RunNumber.Should().Be("PAY-2026-001");
    }

    [Fact]
    public async Task GetPaidRunsForEmployeeInYear_ExcludesAnotherEmployeesRuns()
    {
        var mine = AnEmployee(lastName: "Santos");
        var theirs = AnEmployee(lastName: "Reyes");
        Context.Employees.AddRange(mine, theirs);

        var myRun = ARun("PAY-2026-001", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20));
        var theirRun = ARun("PAY-2026-002", new(2026, 2, 1), new(2026, 2, 15), new(2026, 2, 20));

        Context.PayrollRuns.AddRange(myRun, theirRun);
        Context.PayrollRunEmployees.AddRange(
            AnEntry(myRun.Id, mine.Id),
            AnEntry(theirRun.Id, theirs.Id));
        await Context.SaveChangesAsync();

        var result = await Sut.GetPaidRunsForEmployeeInYearAsync(mine.Id, 2026);

        result.Should().ContainSingle().Which.RunNumber.Should().Be("PAY-2026-001");
    }

    [Fact]
    public async Task GetPaidRunsForEmployeeInYear_IsOrderedByPayDate()
    {
        var employee = AnEmployee();
        Context.Employees.Add(employee);

        var later = ARun("PAY-2026-002", new(2026, 2, 1), new(2026, 2, 15), new(2026, 2, 20));
        var earlier = ARun("PAY-2026-001", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20));

        Context.PayrollRuns.AddRange(later, earlier);
        Context.PayrollRunEmployees.AddRange(
            AnEntry(later.Id, employee.Id),
            AnEntry(earlier.Id, employee.Id));
        await Context.SaveChangesAsync();

        var result = await Sut.GetPaidRunsForEmployeeInYearAsync(employee.Id, 2026);

        result.Select(r => r.RunNumber).Should().ContainInOrder("PAY-2026-001", "PAY-2026-002");
    }

    [Fact]
    public async Task CountForYear_CountsByPeriodStartNotPayDate()
    {
        // CountForYearAsync is deliberately different from the queries above: it numbers runs
        // (PAY-{year}-{sequence}), which follows the period, not the pay date. Pinning the
        // difference stops someone "fixing" the inconsistency and renumbering every run.
        var straddling = ARun("PAY-2026-024", new(2026, 12, 26), new(2027, 1, 10), new(2027, 1, 10));
        var ordinary = ARun("PAY-2026-001", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20));

        Context.PayrollRuns.AddRange(straddling, ordinary);
        await Context.SaveChangesAsync();

        (await Sut.CountForYearAsync(2026)).Should().Be(2);
        (await Sut.CountForYearAsync(2027)).Should().Be(0);
    }

    [Fact]
    public async Task GetPaidYearsForEmployee_IsDistinctAndDescending()
    {
        var employee = AnEmployee();
        Context.Employees.Add(employee);

        var a2026 = ARun("PAY-2026-001", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20));
        var b2026 = ARun("PAY-2026-002", new(2026, 2, 1), new(2026, 2, 15), new(2026, 2, 20));
        var a2027 = ARun("PAY-2027-001", new(2027, 1, 1), new(2027, 1, 15), new(2027, 1, 20));

        Context.PayrollRuns.AddRange(a2026, b2026, a2027);
        Context.PayrollRunEmployees.AddRange(
            AnEntry(a2026.Id, employee.Id),
            AnEntry(b2026.Id, employee.Id),
            AnEntry(a2027.Id, employee.Id));
        await Context.SaveChangesAsync();

        var years = await Sut.GetPaidYearsForEmployeeAsync(employee.Id);

        years.Should().Equal(2027, 2026);
    }

    [Fact]
    public async Task GetEmployeeIdsWithPaidRunsInYear_DeduplicatesAndOrdersByName()
    {
        // SelectMany into entries, Distinct over an anonymous type carrying the employee's name,
        // order by last then first, then project the id back out. A non-obvious translation, and
        // an employee in two paid runs must appear once.
        var santos = AnEmployee(lastName: "Santos", firstName: "Ana");
        var cruz = AnEmployee(lastName: "Cruz", firstName: "Bea");
        Context.Employees.AddRange(santos, cruz);

        var january = ARun("PAY-2026-001", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20));
        var february = ARun("PAY-2026-002", new(2026, 2, 1), new(2026, 2, 15), new(2026, 2, 20));

        Context.PayrollRuns.AddRange(january, february);
        Context.PayrollRunEmployees.AddRange(
            AnEntry(january.Id, santos.Id),
            AnEntry(january.Id, cruz.Id),
            AnEntry(february.Id, santos.Id));
        await Context.SaveChangesAsync();

        var ids = await Sut.GetEmployeeIdsWithPaidRunsInYearAsync(2026);

        ids.Should().Equal(cruz.Id, santos.Id);
    }

    [Fact]
    public async Task GetWithEntries_LoadsBothIncludeChains()
    {
        // AsSplitQuery issues several round trips; this proves entries arrive with their employee
        // AND their loan deduction lines, not just whichever chain the last query happened to run.
        var employee = AnEmployee();
        Context.Employees.Add(employee);

        var run = ARun("PAY-2026-001", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20));
        Context.PayrollRuns.Add(run);

        var entry = AnEntry(run.Id, employee.Id);
        Context.PayrollRunEmployees.Add(entry);
        Context.Set<PeopleCore.Domain.Entities.Payroll.PayrollLoanDeduction>().Add(new()
        {
            PayrollRunEmployeeId = entry.Id,
            EmployeeLoanId = Guid.NewGuid(),
            LoanType = "SSSLoan",
            Amount = 1_500m,
        });
        await Context.SaveChangesAsync();

        await using var reader = NewContext();
        var loaded = await new PayrollRunRepository(reader).GetWithEntriesAsync(run.Id);

        loaded.Should().NotBeNull();
        var loadedEntry = loaded!.Employees.Should().ContainSingle().Subject;
        loadedEntry.Employee.Should().NotBeNull();
        loadedEntry.Employee!.LastName.Should().Be("Dela Cruz");
        loadedEntry.LoanDeductionLines.Should().ContainSingle().Which.Amount.Should().Be(1_500m);
    }

    [Fact]
    public async Task GetPaged_PagesAndReportsTheUnpagedTotal()
    {
        for (var i = 1; i <= 5; i++)
        {
            Context.PayrollRuns.Add(ARun(
                $"PAY-2026-{i:D3}", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20)));
            await Context.SaveChangesAsync();
        }

        var (items, total) = await Sut.GetPagedAsync(page: 2, pageSize: 2);

        total.Should().Be(5, "the count must ignore paging");
        items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetPaidRunsInYear_ReturnsEveryEmployeesPaidRunsOrderedByPayDate()
    {
        // The company-wide counterpart of GetPaidRunsForEmployeeInYearAsync, used by the
        // generate-all path. It takes no employee id, so it is one of the global queries that
        // makes per-test truncation mandatory rather than tidy.
        var santos = AnEmployee(lastName: "Santos");
        var reyes = AnEmployee(lastName: "Reyes");
        Context.Employees.AddRange(santos, reyes);

        var february = ARun("PAY-2026-002", new(2026, 2, 1), new(2026, 2, 15), new(2026, 2, 20));
        var january = ARun("PAY-2026-001", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20));
        var draft = ARun("PAY-2026-003", new(2026, 3, 1), new(2026, 3, 15), new(2026, 3, 20),
            status: PayrollRunStatus.Draft);
        var nextYear = ARun("PAY-2027-001", new(2027, 1, 1), new(2027, 1, 15), new(2027, 1, 20));

        Context.PayrollRuns.AddRange(february, january, draft, nextYear);
        Context.PayrollRunEmployees.AddRange(
            AnEntry(february.Id, santos.Id),
            AnEntry(january.Id, reyes.Id),
            AnEntry(draft.Id, santos.Id),
            AnEntry(nextYear.Id, santos.Id));
        await Context.SaveChangesAsync();

        var result = await Sut.GetPaidRunsInYearAsync(2026);

        result.Select(r => r.RunNumber).Should().Equal("PAY-2026-001", "PAY-2026-002");
    }

    [Fact]
    public async Task GetRunsForEmployee_IsOrderedByPayDateDescending()
    {
        var employee = AnEmployee();
        Context.Employees.Add(employee);

        var earlier = ARun("PAY-2026-001", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20));
        var later = ARun("PAY-2026-002", new(2026, 2, 1), new(2026, 2, 15), new(2026, 2, 20));

        Context.PayrollRuns.AddRange(earlier, later);
        Context.PayrollRunEmployees.AddRange(
            AnEntry(earlier.Id, employee.Id),
            AnEntry(later.Id, employee.Id));
        await Context.SaveChangesAsync();

        var result = await Sut.GetRunsForEmployeeAsync(employee.Id);

        result.Select(r => r.RunNumber).Should().ContainInOrder("PAY-2026-002", "PAY-2026-001");
    }
}
```

- [ ] **Step 2: Run them**

```bash
dotnet test tests/PeopleCore.Infrastructure.Tests/PeopleCore.Infrastructure.Tests.csproj --filter "FullyQualifiedName~PayrollRunRepositoryQueryTests"
```

Expected: `Passed! - Failed: 0, Passed: 11`.

If a test fails, **the finding is the deliverable** — do not edit `src/`. Report which query returned what, and stop.

- [ ] **Step 3: Commit**

```bash
git add tests/PeopleCore.Infrastructure.Tests/Payroll/PayrollRunRepositoryQueryTests.cs
git commit -m "test(payroll): cover PayrollRunRepository's queries against PostgreSQL"
```

---

### Task 3: `PayrollRunRepository` write paths

**Files:**
- Create: `tests/PeopleCore.Infrastructure.Tests/Payroll/PayrollRunRepositoryWriteTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1 and 2's dependencies. No new types.

- [ ] **Step 1: Write the tests**

Create `tests/PeopleCore.Infrastructure.Tests/Payroll/PayrollRunRepositoryWriteTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Payroll;

public class PayrollRunRepositoryWriteTests : DatabaseTestBase
{
    public PayrollRunRepositoryWriteTests(PostgresFixture fixture) : base(fixture) { }

    private PayrollRunRepository Sut => new(Context);

    [Fact]
    public async Task AddWithEntries_PersistsTheRunAndItsEntries()
    {
        // Entries are added to both DbSets explicitly, because the navigation alone is not enough
        // once Compute has assigned each entry an Id. Whether that actually persists both rows is
        // a database question, and this is the first thing to ask it.
        var employee = AnEmployee();
        Context.Employees.Add(employee);
        await Context.SaveChangesAsync();

        var run = ARun("PAY-2026-001", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20));
        run.Employees = [AnEntry(run.Id, employee.Id, regularPay: 30_000m)];

        await Sut.AddWithEntriesAsync(run);

        await using var reader = NewContext();
        (await reader.PayrollRuns.CountAsync()).Should().Be(1);
        var entry = await reader.PayrollRunEmployees.SingleAsync();
        entry.PayrollRunId.Should().Be(run.Id);
        entry.RegularPay.Should().Be(30_000m);
    }

    [Fact]
    public async Task ReplaceEntries_SwapsTheEntriesAndLeavesTheRunIntact()
    {
        var employee = AnEmployee();
        Context.Employees.Add(employee);

        var run = ARun("PAY-2026-001", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20));
        Context.PayrollRuns.Add(run);
        Context.PayrollRunEmployees.Add(AnEntry(run.Id, employee.Id, regularPay: 30_000m));
        await Context.SaveChangesAsync();

        var reloaded = await Context.PayrollRuns
            .Include(r => r.Employees)
            .SingleAsync(r => r.Id == run.Id);

        await Sut.ReplaceEntriesAsync(reloaded, [AnEntry(run.Id, employee.Id, regularPay: 45_000m)]);

        await using var reader = NewContext();
        (await reader.PayrollRuns.CountAsync()).Should().Be(1, "replacing entries must not delete the run");
        var entry = await reader.PayrollRunEmployees.SingleAsync();
        entry.RegularPay.Should().Be(45_000m);
    }

    [Fact]
    public async Task ReplaceEntries_CascadesToLoanDeductionLines()
    {
        // ExecuteDeleteAsync issues a DELETE the change tracker never sees, so the cascade has to
        // come from the database's own ON DELETE CASCADE rather than from EF. Orphaned deduction
        // lines would otherwise survive and be summed into a later payslip.
        var employee = AnEmployee();
        Context.Employees.Add(employee);

        var run = ARun("PAY-2026-001", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20));
        Context.PayrollRuns.Add(run);

        var entry = AnEntry(run.Id, employee.Id);
        Context.PayrollRunEmployees.Add(entry);
        Context.Set<PayrollLoanDeduction>().Add(new()
        {
            PayrollRunEmployeeId = entry.Id,
            EmployeeLoanId = Guid.NewGuid(),
            LoanType = "SSSLoan",
            Amount = 1_500m,
        });
        await Context.SaveChangesAsync();

        var reloaded = await Context.PayrollRuns
            .Include(r => r.Employees)
            .SingleAsync(r => r.Id == run.Id);

        await Sut.ReplaceEntriesAsync(reloaded, [AnEntry(run.Id, employee.Id, regularPay: 45_000m)]);

        await using var reader = NewContext();
        (await reader.Set<PayrollLoanDeduction>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ReplaceEntries_LeavesTheContextUsableAfterwards()
    {
        // The detach loop exists so EF does not issue updates against rows ExecuteDeleteAsync has
        // already removed. Without it the NEXT SaveChangesAsync throws a concurrency exception -
        // so saving again is exactly the assertion that proves the detach happened.
        var employee = AnEmployee();
        Context.Employees.Add(employee);

        var run = ARun("PAY-2026-001", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20));
        Context.PayrollRuns.Add(run);
        Context.PayrollRunEmployees.Add(AnEntry(run.Id, employee.Id));
        await Context.SaveChangesAsync();

        var reloaded = await Context.PayrollRuns
            .Include(r => r.Employees)
            .SingleAsync(r => r.Id == run.Id);

        await Sut.ReplaceEntriesAsync(reloaded, [AnEntry(run.Id, employee.Id, regularPay: 45_000m)]);

        reloaded.Status = PeopleCore.Domain.Enums.PayrollRunStatus.Approved;
        var saveAgain = async () => await Context.SaveChangesAsync();

        await saveAgain.Should().NotThrowAsync();
    }
}
```

- [ ] **Step 2: Run them**

```bash
dotnet test tests/PeopleCore.Infrastructure.Tests/PeopleCore.Infrastructure.Tests.csproj --filter "FullyQualifiedName~PayrollRunRepositoryWriteTests"
```

Expected: `Passed! - Failed: 0, Passed: 4`.

- [ ] **Step 3: Commit**

```bash
git add tests/PeopleCore.Infrastructure.Tests/Payroll/PayrollRunRepositoryWriteTests.cs
git commit -m "test(payroll): cover PayrollRunRepository's write paths against PostgreSQL"
```

---

### Task 4: Compensation, settings, and audit stamping

**Files:**
- Create: `tests/PeopleCore.Infrastructure.Tests/Payroll/EmployeeCompensationRepositoryTests.cs`
- Create: `tests/PeopleCore.Infrastructure.Tests/Payroll/PayrollSettingsRepositoryTests.cs`
- Create: `tests/PeopleCore.Infrastructure.Tests/AuditStampingTests.cs`

**Interfaces:**
- Consumes: `DatabaseTestBase`, `PostgresFixture` from Task 1; `EmployeeCompensationRepository(AppDbContext)` and `PayrollSettingsRepository(AppDbContext)`.

- [ ] **Step 1: Write the compensation tests**

Create `tests/PeopleCore.Infrastructure.Tests/Payroll/EmployeeCompensationRepositoryTests.cs`:

```csharp
using FluentAssertions;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Payroll;

public class EmployeeCompensationRepositoryTests : DatabaseTestBase
{
    public EmployeeCompensationRepositoryTests(PostgresFixture fixture) : base(fixture) { }

    private EmployeeCompensationRepository Sut => new(Context);

    private static EmployeeCompensation ACompensation(Guid employeeId, decimal basicSalary)
        => new()
        {
            EmployeeId = employeeId,
            BasicSalary = basicSalary,
            PayFrequency = PayFrequency.SemiMonthly,
            TaxCode = "ME",
        };

    [Fact]
    public async Task ANumericColumnSilentlyDropsAThirdDecimalPlace()
    {
        // The premise the validation phase relied on when it started rejecting a third decimal:
        // the column is numeric(18,2), so the stored salary would otherwise differ from the
        // submitted one with nothing reporting it. Asserted here for the first time.
        var employee = AnEmployee();
        Context.Employees.Add(employee);
        await Context.SaveChangesAsync();

        await Sut.AddAsync(ACompensation(employee.Id, 30_000.567m));

        await using var reader = NewContext();
        var stored = await new EmployeeCompensationRepository(reader).GetByEmployeeIdAsync(employee.Id);

        stored!.BasicSalary.Should().Be(30_000.57m, "numeric(18,2) rounds to two places on save");
    }

    [Fact]
    public async Task ATwoDecimalSalaryRoundTripsExactly()
    {
        var employee = AnEmployee();
        Context.Employees.Add(employee);
        await Context.SaveChangesAsync();

        await Sut.AddAsync(ACompensation(employee.Id, 30_000.12m));

        await using var reader = NewContext();
        var stored = await new EmployeeCompensationRepository(reader).GetByEmployeeIdAsync(employee.Id);

        stored!.BasicSalary.Should().Be(30_000.12m);
    }

    [Fact]
    public async Task GetByEmployeeId_ReturnsNullWhenThereIsNoCompensationRow()
    {
        (await Sut.GetByEmployeeIdAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task GetByEmployeeIds_ReturnsOnlyTheRequestedEmployees()
    {
        var wanted = AnEmployee(lastName: "Santos");
        var other = AnEmployee(lastName: "Reyes");
        Context.Employees.AddRange(wanted, other);
        await Context.SaveChangesAsync();

        await Sut.AddAsync(ACompensation(wanted.Id, 30_000m));
        await Sut.AddAsync(ACompensation(other.Id, 40_000m));

        var result = await Sut.GetByEmployeeIdsAsync([wanted.Id]);

        result.Should().ContainSingle().Which.EmployeeId.Should().Be(wanted.Id);
    }
}
```

- [ ] **Step 2: Write the settings tests**

Create `tests/PeopleCore.Infrastructure.Tests/Payroll/PayrollSettingsRepositoryTests.cs`:

```csharp
using FluentAssertions;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Payroll;

public class PayrollSettingsRepositoryTests : DatabaseTestBase
{
    public PayrollSettingsRepositoryTests(PostgresFixture fixture) : base(fixture) { }

    private PayrollSettingsRepository Sut => new(Context);

    [Fact]
    public async Task GetDefault_ReturnsNullWhenNoSettingsExist()
    {
        (await Sut.GetDefaultAsync()).Should().BeNull();
    }

    [Fact]
    public async Task GetDefault_ReturnsTheOnlyRow()
    {
        var company = ACompany();
        Context.Companies.Add(company);
        Context.PayrollSettings.Add(new PayrollSettings { CompanyId = company.Id, DailyRateFactor = 313m });
        await Context.SaveChangesAsync();

        var settings = await Sut.GetDefaultAsync();

        settings.Should().NotBeNull();
        settings!.DailyRateFactor.Should().Be(313m);
    }

    [Fact]
    public async Task GetDefault_ThrowsWhenASecondCompanyHasSettings()
    {
        // The whole multi-company safety story. PayrollRun carries no CompanyId, so rate
        // resolution would otherwise use whichever row EF returned first - and a second company's
        // DailyRateFactor or SSS overrides would wrong every computed wage with no error at all.
        // Failing loudly is the deliberate behaviour; this is the first test to prove it does.
        var first = ACompany("First Company");
        var second = ACompany("Second Company");
        Context.Companies.AddRange(first, second);
        Context.PayrollSettings.AddRange(
            new PayrollSettings { CompanyId = first.Id },
            new PayrollSettings { CompanyId = second.Id });
        await Context.SaveChangesAsync();

        var act = async () => await Sut.GetDefaultAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*PayrollRun carries no CompanyId*");
    }

    [Fact]
    public async Task TheUniqueIndexOnCompanyIdIsEnforcedByTheDatabase()
    {
        // Declared by UniquePayrollSettingsCompany. A configuration class saying IsUnique proves
        // nothing until a migration has actually built the index.
        var company = ACompany();
        Context.Companies.Add(company);
        Context.PayrollSettings.Add(new PayrollSettings { CompanyId = company.Id });
        await Context.SaveChangesAsync();

        await using var second = NewContext();
        second.PayrollSettings.Add(new PayrollSettings { CompanyId = company.Id });

        var act = async () => await second.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task GetByCompanyId_ReturnsThatCompanysSettings()
    {
        var first = ACompany("First Company");
        var second = ACompany("Second Company");
        Context.Companies.AddRange(first, second);
        Context.PayrollSettings.AddRange(
            new PayrollSettings { CompanyId = first.Id, DailyRateFactor = 313m },
            new PayrollSettings { CompanyId = second.Id, DailyRateFactor = 261m });
        await Context.SaveChangesAsync();

        var settings = await Sut.GetByCompanyIdAsync(second.Id);

        settings!.DailyRateFactor.Should().Be(261m);
    }
}
```

Add `using Microsoft.EntityFrameworkCore;` to the top of that file for `DbUpdateException`.

- [ ] **Step 3: Write the audit tests**

Create `tests/PeopleCore.Infrastructure.Tests/AuditStampingTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using PeopleCore.Application.Common.Interfaces;

namespace PeopleCore.Infrastructure.Tests;

public class AuditStampingTests : DatabaseTestBase
{
    public AuditStampingTests(PostgresFixture fixture) : base(fixture) { }

    private static ICurrentUserService AUserService(Guid userId)
    {
        var mock = new Mock<ICurrentUserService>();
        mock.SetupGet(u => u.UserId).Returns(userId);
        return mock.Object;
    }

    [Fact]
    public async Task Insert_StampsCreatedByFromTheCurrentUser()
    {
        // These columns going unpopulated was a real defect, fixed earlier in this work and
        // verified by nothing since.
        var userId = Guid.NewGuid();
        await using var context = NewContext(AUserService(userId));

        var run = ARun("PAY-2026-001", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20));
        context.PayrollRuns.Add(run);
        await context.SaveChangesAsync();

        await using var reader = NewContext();
        var stored = await reader.PayrollRuns.SingleAsync();

        stored.CreatedBy.Should().Be(userId);
        stored.UpdatedBy.Should().Be(userId);
    }

    [Fact]
    public async Task Insert_OverwritesTheTimestampTheEntityWasConstructedWith()
    {
        // AuditableEntity sets CreatedAt to UtcNow in its initialiser, so a test that only checked
        // "CreatedAt is roughly now" would pass even if StampAuditColumns never ran. Setting a
        // distinctive value first is what makes this test able to fail.
        await using var context = NewContext(AUserService(Guid.NewGuid()));

        var run = ARun("PAY-2026-001", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20));
        run.CreatedAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        context.PayrollRuns.Add(run);
        await context.SaveChangesAsync();

        await using var reader = NewContext();
        var stored = await reader.PayrollRuns.SingleAsync();

        stored.CreatedAt.Should().BeAfter(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Update_StampsUpdatedByAndLeavesCreatedByAlone()
    {
        var creator = Guid.NewGuid();
        var editor = Guid.NewGuid();

        await using (var first = NewContext(AUserService(creator)))
        {
            first.PayrollRuns.Add(ARun("PAY-2026-001", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20)));
            await first.SaveChangesAsync();
        }

        await using (var second = NewContext(AUserService(editor)))
        {
            var run = await second.PayrollRuns.SingleAsync();
            run.Status = PeopleCore.Domain.Enums.PayrollRunStatus.Approved;
            await second.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var stored = await reader.PayrollRuns.SingleAsync();

        stored.CreatedBy.Should().Be(creator, "an update must not rewrite who created the row");
        stored.UpdatedBy.Should().Be(editor);
    }

    [Fact]
    public async Task ANullCurrentUserServiceStampsTimestampsAndLeavesTheUserColumnsNull()
    {
        // The constructor parameter is optional, and background work - the leave accrual hosted
        // service, migrations, the seeder - resolves a context with no user. That must save
        // rather than throw.
        await using var context = NewContext(currentUser: null);

        var save = async () =>
        {
            context.PayrollRuns.Add(ARun("PAY-2026-001", new(2026, 1, 1), new(2026, 1, 15), new(2026, 1, 20)));
            await context.SaveChangesAsync();
        };

        await save.Should().NotThrowAsync();

        await using var reader = NewContext();
        var stored = await reader.PayrollRuns.SingleAsync();

        stored.CreatedBy.Should().BeNull();
        stored.CreatedAt.Should().BeAfter(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }
}
```

This file uses Moq, which the new project does not reference. Add it:

```xml
    <PackageReference Include="Moq" Version="4.20.72" />
```

- [ ] **Step 4: Run the whole new project**

```bash
dotnet test tests/PeopleCore.Infrastructure.Tests/PeopleCore.Infrastructure.Tests.csproj
```

Expected: `Passed! - Failed: 0, Passed: 31` — 3 harness + 11 queries + 4 writes + 4 compensation + 5 settings + 4 audit.

- [ ] **Step 5: Run the whole solution**

```bash
dotnet test PeopleCore.slnx
```

Expected: `Failed: 0` across both projects, with the Application project still reporting 359.

- [ ] **Step 6: Commit**

```bash
git add tests/PeopleCore.Infrastructure.Tests
git commit -m "test(payroll): cover compensation, settings and audit stamping against PostgreSQL"
```

---

## Definition of done

- [ ] `tests/PeopleCore.Infrastructure.Tests` exists and is listed in `PeopleCore.slnx`.
- [ ] `dotnet test PeopleCore.slnx` discovers two test projects and reports `Failed: 0`.
- [ ] The Application project still reports 359 tests, and no existing test file was edited.
- [ ] No file under `src/` was modified.
- [ ] `Testcontainers.PostgreSql` and `Moq` are referenced only by the new test project.
- [ ] The schema is created by `MigrateAsync`; the string `EnsureCreated` appears nowhere.
- [ ] The truncation list is built from `Context.Model`, with no table name written by hand.
