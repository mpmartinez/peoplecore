# Government Remittance Reports Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Government Reports page under Payroll showing each month's SSS, PhilHealth, Pag-IBIG and BIR 1601-C figures per employee, with totals, missing-number warnings and a CSV download.

**Architecture:** One application service turns a month's Paid payroll runs into a report DTO: a table (columns, rows, totals), plus summary lines for 1601-C, and warnings. The arithmetic lives in pure static helpers. A thin controller returns the DTO as JSON or as CSV, and a Blazor page renders it. Nothing new is stored.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core (Npgsql), Blazor WebAssembly, xUnit, FluentAssertions, Moq, bUnit, Testcontainers Postgres (`DatabaseTestBase`).

**Spec:** `docs/superpowers/specs/2026-09-21-government-remittance-reports-design.md`

## Global Constraints

- Only `PayrollRunStatus.Paid` runs count.
- SSS, PhilHealth and Pag-IBIG: a run counts toward the month of its `PeriodEnd`. BIR 1601-C: the month of its `PayDate`.
- Employee ID numbers come from `Employee.GovernmentIds` (`EmployeeGovernmentId.IdType` / `IdNumber`). Never use the M2NET.Core base fields `TIN`, `SSSNumber` and so on, which are unmapped.
- Employer header from `ICompanyRepository.GetDefaultAsync`.
- Guarded by `Permissions.PayrollManage` (`PeopleCore.Application.Common.Authorization`).
- Route: `api/reports/government/{report}?year=&month=[&format=csv]`, where `{report}` is `sss`, `philhealth`, `pagibig` or `1601c`.
- Unknown report → `KeyNotFoundException` (the middleware maps it to 404). A bad month, or a month after the current Philippine month → `DomainException` (400).
- CSV file name: `<report>-<yyyy>-<MM>.csv`, content type `text/csv`, UTF-8 with a BOM.
- Money in DTO cells and CSV is formatted invariant `0.00` (no thousands separator).
- 13th-month exemption: `StatutoryCaps.ThirteenthMonthExemption` (90,000).
- To avoid the stuck shared build nodes seen on this machine, run builds and tests with `-nodeReuse:false -p:UseSharedCompilation=false`.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/PeopleCore.Application/Payroll/GovernmentReports/GovernmentReportDtos.cs` (new) | Report DTO records |
| `src/PeopleCore.Application/Payroll/GovernmentReports/GovernmentReportMath.cs` (new) | Pure arithmetic: SSS credit and EC, the non-taxable 13th month |
| `src/PeopleCore.Application/Payroll/GovernmentReports/IGovernmentReportService.cs` (new) | Service interface |
| `src/PeopleCore.Application/Payroll/GovernmentReports/GovernmentReportService.cs` (new) | Builds the four reports from runs |
| `src/PeopleCore.Application/Payroll/GovernmentReports/GovernmentReportCsv.cs` (new) | DTO → CSV bytes |
| `src/PeopleCore.Application/Payroll/Interfaces/IPayrollRunRepository.cs` (modify) | Three month queries |
| `src/PeopleCore.Infrastructure/Persistence/Repositories/PayrollRunRepository.cs` (modify) | Their EF implementations |
| `src/PeopleCore.API/Controllers/Payroll/GovernmentReportsController.cs` (new) | HTTP endpoint |
| `src/PeopleCore.API/Extensions/ServiceExtensions.cs` (modify) | DI registration |
| `src/PeopleCore.Web/Services/ApiClient.cs` (modify) | Client methods and DTO copies |
| `src/PeopleCore.Web/Pages/Payroll/GovernmentReports.razor` (new) | The page |
| `src/PeopleCore.Web/Layout/NavMenu.razor` (modify) | Nav entry |
| Tests under `tests/PeopleCore.Application.Tests/Payroll/`, `tests/PeopleCore.Infrastructure.Tests/Payroll/`, `tests/PeopleCore.Web.Tests/Pages/Payroll/` | One test file per unit |

---

### Task 1: DTOs and report arithmetic

**Files:**
- Create: `src/PeopleCore.Application/Payroll/GovernmentReports/GovernmentReportDtos.cs`
- Create: `src/PeopleCore.Application/Payroll/GovernmentReports/GovernmentReportMath.cs`
- Test: `tests/PeopleCore.Application.Tests/Payroll/GovernmentReportMathTests.cs`

**Interfaces:**
- Produces: the records `GovernmentReportDto`, `GovernmentReportRowDto`, `GovernmentReportLineDto` and `GovernmentReportEmployerDto`, and `GovernmentReportMath.SssCredit(decimal) : (decimal? Msc, decimal? Ec)`, `GovernmentReportMath.NonTaxableThirteenthMonth(decimal thisMonth, decimal paidEarlierInYear) : decimal`, `GovernmentReportMath.Money(decimal) : string`.

- [ ] **Step 1: Write the failing tests**

```csharp
using FluentAssertions;
using PeopleCore.Application.Payroll.GovernmentReports;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class GovernmentReportMathTests
{
    // Circular 2024-006: the employee pays 5% of the MSC; EC is 10.00 below an MSC of 15,000 and
    // 30.00 from 15,000 up.
    [Theory]
    [InlineData(250.00, 5_000, 10)]      // the floor
    [InlineData(725.00, 14_500, 10)]     // last 10.00 bracket
    [InlineData(750.00, 15_000, 30)]     // EC steps up
    [InlineData(1_750.00, 35_000, 30)]   // the ceiling
    public void SssCredit_WorksBackTheMscAndEcFromTheEmployeeShare(decimal share, decimal msc, decimal ec)
    {
        GovernmentReportMath.SssCredit(share).Should().Be((msc, ec));
    }

    [Fact]
    public void SssCredit_AddsTwoCutoffsBeforeWorkingBack()
    {
        // Two semi-monthly halves of 512.50 are one 1,025.00 month: MSC 20,500.
        GovernmentReportMath.SssCredit(512.50m + 512.50m).Should().Be((20_500m, 30m));
    }

    [Fact]
    public void SssCredit_IsBlankWithNothingDeducted()
    {
        GovernmentReportMath.SssCredit(0m).Should().Be(((decimal?)null, (decimal?)null));
    }

    [Theory]
    [InlineData(30_000, 0, 30_000)]        // nothing used yet
    [InlineData(40_000, 60_000, 30_000)]   // 30,000 of the exemption left
    [InlineData(40_000, 90_000, 0)]        // all used earlier in the year
    [InlineData(40_000, 120_000, 0)]       // more than the exemption paid earlier
    public void NonTaxableThirteenthMonth_IsWhatIsLeftOfThe90kExemption(decimal thisMonth, decimal earlier, decimal expected)
    {
        GovernmentReportMath.NonTaxableThirteenthMonth(thisMonth, earlier).Should().Be(expected);
    }

    [Fact]
    public void Money_IsInvariantWithTwoDecimalsAndNoSeparator()
    {
        GovernmentReportMath.Money(12_345.5m).Should().Be("12345.50");
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test tests/PeopleCore.Application.Tests --filter "FullyQualifiedName~GovernmentReportMathTests" -nodeReuse:false -p:UseSharedCompilation=false`
Expected: build error, `GovernmentReportMath` does not exist.

- [ ] **Step 3: Write the DTOs**

`src/PeopleCore.Application/Payroll/GovernmentReports/GovernmentReportDtos.cs`:

```csharp
namespace PeopleCore.Application.Payroll.GovernmentReports;

/// <summary>
/// One agency's report for one month, shaped as a table so the page and the CSV render every
/// report the same way. <see cref="Summary"/> carries BIR 1601-C's form lines and is empty for
/// the others.
/// </summary>
public sealed record GovernmentReportDto(
    string Report,
    string Title,
    int Year,
    int Month,
    string Basis,
    GovernmentReportEmployerDto Employer,
    IReadOnlyList<string> Columns,
    IReadOnlyList<GovernmentReportRowDto> Rows,
    IReadOnlyList<string> Totals,
    IReadOnlyList<GovernmentReportLineDto> Summary,
    IReadOnlyList<string> Warnings);

/// <param name="MissingNumber">The employee has no ID number for this agency.</param>
public sealed record GovernmentReportRowDto(Guid EmployeeId, IReadOnlyList<string> Cells, bool MissingNumber);

public sealed record GovernmentReportLineDto(string Label, decimal Amount);

/// <param name="AgencyNumber">The employer's number with this report's agency; the TIN for 1601-C.</param>
public sealed record GovernmentReportEmployerDto(string Name, string? Address, string Tin, string? RdoCode, string AgencyNumber);
```

- [ ] **Step 4: Write the arithmetic**

`src/PeopleCore.Application/Payroll/GovernmentReports/GovernmentReportMath.cs`:

```csharp
using System.Globalization;
using PeopleCore.Domain.Payroll;

namespace PeopleCore.Application.Payroll.GovernmentReports;

/// <summary>The figures the reports work out that payroll entries do not store.</summary>
public static class GovernmentReportMath
{
    /// <summary>
    /// The monthly salary credit and EC behind a month's SSS employee share, under Circular
    /// 2024-006: the employee pays 5% of the MSC, and EC is 10.00 below an MSC of 15,000 and 30.00
    /// from there up. Blank when nothing was deducted.
    /// </summary>
    public static (decimal? Msc, decimal? Ec) SssCredit(decimal monthlyEmployeeShare)
    {
        if (monthlyEmployeeShare <= 0m)
            return (null, null);

        decimal msc = Math.Round(monthlyEmployeeShare / 0.05m / 500m, MidpointRounding.AwayFromZero) * 500m;
        return (msc, msc < 15_000m ? 10m : 30m);
    }

    /// <summary>
    /// The part of a month's 13th month pay within the 90,000 exemption, after the 13th month paid
    /// earlier in the year has used its share, the same way payroll withheld tax on it.
    /// </summary>
    public static decimal NonTaxableThirteenthMonth(decimal thisMonth, decimal paidEarlierInYear)
        => Math.Min(thisMonth, Math.Max(0m, StatutoryCaps.ThirteenthMonthExemption - paidEarlierInYear));

    public static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
}
```

- [ ] **Step 5: Run the tests and confirm they pass**

Run: the command from Step 2. Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add src/PeopleCore.Application/Payroll/GovernmentReports tests/PeopleCore.Application.Tests/Payroll/GovernmentReportMathTests.cs
git commit -m "feat(payroll): government report DTOs and the figures they work out"
```

---

### Task 2: Month queries on the payroll run repository

**Files:**
- Modify: `src/PeopleCore.Application/Payroll/Interfaces/IPayrollRunRepository.cs` (add three members after `GetPaidRunsInYearAsync`)
- Modify: `src/PeopleCore.Infrastructure/Persistence/Repositories/PayrollRunRepository.cs` (add three methods after `GetPaidRunsInYearAsync`)
- Test: `tests/PeopleCore.Infrastructure.Tests/Payroll/PayrollRunRepositoryMonthTests.cs`

**Interfaces:**
- Produces:
  - `Task<IReadOnlyList<PayrollRun>> GetPaidRunsByPeriodEndMonthAsync(int year, int month, CancellationToken ct = default)`
  - `Task<IReadOnlyList<PayrollRun>> GetPaidRunsByPayMonthAsync(int year, int month, CancellationToken ct = default)`
  - `Task<int> CountUnpaidRunsAsync(int year, int month, bool byPayDate, CancellationToken ct = default)`
  - The first two load `Employees → Employee → GovernmentIds`.

- [ ] **Step 1: Write the failing tests**

```csharp
using FluentAssertions;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Enums;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Payroll;

public class PayrollRunRepositoryMonthTests : DatabaseTestBase
{
    public PayrollRunRepositoryMonthTests(PostgresFixture fixture) : base(fixture) { }

    private PayrollRunRepository Sut => new(NewContext());

    private async Task<Guid> SeedAsync()
    {
        var employee = AnEmployee();
        employee.GovernmentIds.Add(new EmployeeGovernmentId { IdType = GovernmentIdType.SSS, IdNumber = "34-1234567-8" });
        Context.Employees.Add(employee);

        // December's second cutoff paid in January, a March run paid in March, a draft March run.
        var decJan = ARun("PAY-2025-024", new(2025, 12, 16), new(2025, 12, 31), new(2026, 1, 5));
        var march = ARun("PAY-2026-005", new(2026, 3, 1), new(2026, 3, 15), new(2026, 3, 20));
        var draft = ARun("PAY-2026-006", new(2026, 3, 16), new(2026, 3, 31), new(2026, 4, 5), PayrollRunStatus.Draft);
        foreach (var run in new[] { decJan, march, draft })
        {
            Context.PayrollRuns.Add(run);
            Context.PayrollRunEmployees.Add(AnEntry(run.Id, employee.Id));
        }
        await Context.SaveChangesAsync();
        return employee.Id;
    }

    [Fact]
    public async Task ByPeriodEndMonth_CountsTheMonthThePayWasEarned_PaidOnly()
    {
        await SeedAsync();

        (await Sut.GetPaidRunsByPeriodEndMonthAsync(2025, 12)).Select(r => r.RunNumber).Should().Equal("PAY-2025-024");
        (await Sut.GetPaidRunsByPeriodEndMonthAsync(2026, 1)).Should().BeEmpty();
        (await Sut.GetPaidRunsByPeriodEndMonthAsync(2026, 3)).Select(r => r.RunNumber).Should().Equal("PAY-2026-005");
    }

    [Fact]
    public async Task ByPayMonth_CountsTheMonthItWasPaid_PaidOnly()
    {
        await SeedAsync();

        (await Sut.GetPaidRunsByPayMonthAsync(2026, 1)).Select(r => r.RunNumber).Should().Equal("PAY-2025-024");
        (await Sut.GetPaidRunsByPayMonthAsync(2025, 12)).Should().BeEmpty();
    }

    [Fact]
    public async Task TheRuns_ComeWithEachEmployeesGovernmentIds()
    {
        await SeedAsync();

        var run = (await Sut.GetPaidRunsByPeriodEndMonthAsync(2026, 3)).Single();

        run.Employees.Single().Employee!.GovernmentIds.Single().IdNumber.Should().Be("34-1234567-8");
    }

    [Fact]
    public async Task CountUnpaidRuns_CountsRunsNotYetPaid_OnTheReportsBasis()
    {
        await SeedAsync();

        (await Sut.CountUnpaidRunsAsync(2026, 3, byPayDate: false)).Should().Be(1);
        (await Sut.CountUnpaidRunsAsync(2026, 4, byPayDate: true)).Should().Be(1);
        (await Sut.CountUnpaidRunsAsync(2026, 3, byPayDate: true)).Should().Be(0);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test tests/PeopleCore.Infrastructure.Tests --filter "FullyQualifiedName~PayrollRunRepositoryMonthTests" -nodeReuse:false -p:UseSharedCompilation=false`
Expected: build error, the methods don't exist.

- [ ] **Step 3: Add the interface members** after `GetPaidRunsInYearAsync` in `IPayrollRunRepository.cs`:

```csharp
    /// <summary>
    /// Every Paid run whose period ends in the month, each loaded with its entries, their employees
    /// and those employees' government IDs. SSS, PhilHealth and Pag-IBIG contributions are for the
    /// month the pay was earned, so a Dec 16-31 cutoff paid on 5 January belongs to December.
    /// </summary>
    Task<IReadOnlyList<PayrollRun>> GetPaidRunsByPeriodEndMonthAsync(int year, int month, CancellationToken ct = default);

    /// <summary>
    /// Every Paid run paid in the month, loaded as <see cref="GetPaidRunsByPeriodEndMonthAsync"/>.
    /// BIR 1601-C reports tax withheld in the month compensation was paid.
    /// </summary>
    Task<IReadOnlyList<PayrollRun>> GetPaidRunsByPayMonthAsync(int year, int month, CancellationToken ct = default);

    /// <summary>
    /// Runs in the month, on the given basis, that are not Paid yet, so a report can say what it
    /// leaves out.
    /// </summary>
    Task<int> CountUnpaidRunsAsync(int year, int month, bool byPayDate, CancellationToken ct = default);
```

- [ ] **Step 4: Implement them** after `GetPaidRunsInYearAsync` in `PayrollRunRepository.cs`:

```csharp
    public async Task<IReadOnlyList<PayrollRun>> GetPaidRunsByPeriodEndMonthAsync(int year, int month, CancellationToken ct = default)
        => await WithEmployeesAndIds(Context.PayrollRuns
                .Where(r => r.Status == PayrollRunStatus.Paid && r.PeriodEnd.Year == year && r.PeriodEnd.Month == month))
            .OrderBy(r => r.PeriodEnd)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<PayrollRun>> GetPaidRunsByPayMonthAsync(int year, int month, CancellationToken ct = default)
        => await WithEmployeesAndIds(Context.PayrollRuns
                .Where(r => r.Status == PayrollRunStatus.Paid && r.PayDate.Year == year && r.PayDate.Month == month))
            .OrderBy(r => r.PayDate)
            .ToListAsync(ct);

    public async Task<int> CountUnpaidRunsAsync(int year, int month, bool byPayDate, CancellationToken ct = default)
        => byPayDate
            ? await Context.PayrollRuns.CountAsync(r => r.Status != PayrollRunStatus.Paid
                                                        && r.PayDate.Year == year && r.PayDate.Month == month, ct)
            : await Context.PayrollRuns.CountAsync(r => r.Status != PayrollRunStatus.Paid
                                                        && r.PeriodEnd.Year == year && r.PeriodEnd.Month == month, ct);

    private static IQueryable<PayrollRun> WithEmployeesAndIds(IQueryable<PayrollRun> runs)
        => runs.Include(r => r.Employees).ThenInclude(e => e.Employee!).ThenInclude(p => p.GovernmentIds)
               .AsSplitQuery();
```

- [ ] **Step 5: Run the tests and confirm they pass**

Run: the command from Step 2. Expected: all 4 pass.

- [ ] **Step 6: Commit**

```bash
git add src/PeopleCore.Application/Payroll/Interfaces/IPayrollRunRepository.cs src/PeopleCore.Infrastructure/Persistence/Repositories/PayrollRunRepository.cs tests/PeopleCore.Infrastructure.Tests/Payroll/PayrollRunRepositoryMonthTests.cs
git commit -m "feat(payroll): find a month's paid runs by when the pay was earned or paid"
```

---

### Task 3: The report service

**Files:**
- Create: `src/PeopleCore.Application/Payroll/GovernmentReports/IGovernmentReportService.cs`
- Create: `src/PeopleCore.Application/Payroll/GovernmentReports/GovernmentReportService.cs`
- Test: `tests/PeopleCore.Application.Tests/Payroll/GovernmentReportServiceTests.cs`

**Interfaces:**
- Consumes: Task 1 (DTOs, `GovernmentReportMath`) and Task 2 (repository methods). Also `ICompanyRepository.GetDefaultAsync`, `IPayrollSettingsRepository.GetDefaultAsync` (`PayrollSettings.SSSEmployeeRate` / `SSSEmployerRate`), `IPayrollRunRepository.GetPaidRunsInYearAsync`, and `PhilippineTime.Now(TimeProvider)` (`PeopleCore.Application.Common.Time`).
- Produces: `IGovernmentReportService.BuildAsync(string report, int year, int month, CancellationToken ct = default) : Task<GovernmentReportDto>`, and the report keys `sss`, `philhealth`, `pagibig` and `1601c`.

- [ ] **Step 1: Write the failing tests**

```csharp
using FluentAssertions;
using Moq;
using PeopleCore.Application.Organization.Interfaces;
using PeopleCore.Application.Payroll.GovernmentReports;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Organization;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class GovernmentReportServiceTests
{
    private readonly Mock<IPayrollRunRepository> _runs = new();
    private readonly Mock<ICompanyRepository> _companies = new();
    private readonly Mock<IPayrollSettingsRepository> _settings = new();
    private readonly GovernmentReportService _sut;

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    public GovernmentReportServiceTests()
    {
        _companies.Setup(c => c.GetDefaultAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new Company
        {
            Name = "Acme Inc.", TIN = "123-456-789-000", SSSNumber = "03-9999999-1",
            PhilHealthNumber = "20-000000001-2", PagIbigNumber = "2000-0000-0001", RdoCode = "050"
        });
        _runs.Setup(r => r.GetPaidRunsByPeriodEndMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _runs.Setup(r => r.GetPaidRunsByPayMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _runs.Setup(r => r.GetPaidRunsInYearAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _sut = new GovernmentReportService(_runs.Object, _companies.Object, _settings.Object,
            new FixedClock(new DateTimeOffset(2026, 4, 10, 0, 0, 0, TimeSpan.Zero)));
    }

    private static Employee Person(string last, string first, params (GovernmentIdType Type, string Number)[] ids)
    {
        var e = new Employee { LastName = last, FirstName = first, DateOfBirth = new DateOnly(1990, 5, 1) };
        foreach (var (type, number) in ids)
            e.GovernmentIds.Add(new EmployeeGovernmentId { EmployeeId = e.Id, IdType = type, IdNumber = number });
        return e;
    }

    private static PayrollRun Run(DateOnly periodEnd, DateOnly payDate, params PayrollRunEmployee[] entries)
    {
        var run = new PayrollRun { RunNumber = "PAY", PeriodStart = periodEnd.AddDays(-14), PeriodEnd = periodEnd,
                                   PayDate = payDate, Status = PayrollRunStatus.Paid };
        run.Employees.AddRange(entries);
        return run;
    }

    private static PayrollRunEmployee Entry(Employee e, decimal sssEe = 0, decimal sssEr = 0, decimal regularPay = 0,
        decimal thirteenth = 0, decimal tax = 0, decimal phEe = 0, decimal piEe = 0, decimal nonTaxAllow = 0)
        => new() { EmployeeId = e.Id, Employee = e, SSSEmployee = sssEe, SSSEmployer = sssEr, RegularPay = regularPay,
                   ThirteenthMonth = thirteenth, WithholdingTax = tax, PhilHealthEmployee = phEe, PagIbigEmployee = piEe,
                   NonTaxableAllowances = nonTaxAllow };

    [Fact]
    public async Task Sss_CombinesAMonthsCutoffsIntoOneRow_AndWorksBackTheMscAndEc()
    {
        var juan = Person("Cruz", "Juan", (GovernmentIdType.SSS, "34-1234567-8"));
        _runs.Setup(r => r.GetPaidRunsByPeriodEndMonthAsync(2026, 3, It.IsAny<CancellationToken>())).ReturnsAsync([
            Run(new(2026, 3, 15), new(2026, 3, 20), Entry(juan, sssEe: 500m, sssEr: 1_015m)),
            Run(new(2026, 3, 31), new(2026, 4, 5), Entry(juan, sssEe: 500m, sssEr: 1_015m))
        ]);

        var report = await _sut.BuildAsync("sss", 2026, 3);

        report.Columns.Should().Equal("Employee", "SSS number", "MSC", "Employee share", "Employer share", "EC", "Employer total", "Total");
        report.Rows.Should().ContainSingle().Which.Cells.Should().Equal(
            "Cruz, Juan", "34-1234567-8", "20000.00", "1000.00", "2000.00", "30.00", "2030.00", "3030.00");
        report.Totals.Should().Equal("Total", "", "", "1000.00", "2000.00", "30.00", "2030.00", "3030.00");
        report.Employer.AgencyNumber.Should().Be("03-9999999-1");
    }

    [Fact]
    public async Task Sss_LeavesMscAndEcBlank_WhenTheCompanyOverridesTheRates()
    {
        _settings.Setup(s => s.GetDefaultAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new PayrollSettings { SSSEmployeeRate = 0.045m, SSSEmployerRate = 0.095m });
        var juan = Person("Cruz", "Juan", (GovernmentIdType.SSS, "34-1234567-8"));
        _runs.Setup(r => r.GetPaidRunsByPeriodEndMonthAsync(2026, 3, It.IsAny<CancellationToken>()))
             .ReturnsAsync([Run(new(2026, 3, 31), new(2026, 4, 5), Entry(juan, sssEe: 900m, sssEr: 1_900m))]);

        var report = await _sut.BuildAsync("sss", 2026, 3);

        report.Rows.Single().Cells.Should().Equal("Cruz, Juan", "34-1234567-8", "", "900.00", "", "", "1900.00", "2800.00");
        report.Warnings.Should().Contain(w => w.Contains("override"));
    }

    [Fact]
    public async Task AnEmployeeWithoutTheAgencysNumber_IsListedAndCounted()
    {
        var ana = Person("Reyes", "Ana");
        _runs.Setup(r => r.GetPaidRunsByPeriodEndMonthAsync(2026, 3, It.IsAny<CancellationToken>()))
             .ReturnsAsync([Run(new(2026, 3, 31), new(2026, 4, 5), Entry(ana, phEe: 500m))]);

        var report = await _sut.BuildAsync("philhealth", 2026, 3);

        report.Rows.Single().MissingNumber.Should().BeTrue();
        report.Warnings.Should().Contain("1 employee has no PhilHealth number.");
    }

    [Fact]
    public async Task Bir1601C_CountsTheMonthPaid_AndItsLinesAddUp()
    {
        var juan = Person("Cruz", "Juan", (GovernmentIdType.TIN, "111-222-333-000"));
        // Paid in April for a March period: 1601-C for April, not March.
        _runs.Setup(r => r.GetPaidRunsByPayMonthAsync(2026, 4, It.IsAny<CancellationToken>())).ReturnsAsync([
            Run(new(2026, 3, 31), new(2026, 4, 5),
                Entry(juan, regularPay: 50_000m, thirteenth: 100_000m, tax: 9_000m,
                      sssEe: 875m, phEe: 625m, piEe: 100m, nonTaxAllow: 1_000m))
        ]);

        var report = await _sut.BuildAsync("1601c", 2026, 4);

        // Compensation 151,000 = 50,000 + 100,000 13th month + 1,000 allowances. Non-taxable:
        // 90,000 of the 13th month + 1,600 employee shares + 1,000 allowances = 92,600.
        report.Summary.Should().ContainInOrder(
            new GovernmentReportLineDto("Total amount of compensation", 151_000m),
            new GovernmentReportLineDto("13th month pay and other benefits", 90_000m),
            new GovernmentReportLineDto("SSS, PhilHealth and Pag-IBIG employee shares", 1_600m),
            new GovernmentReportLineDto("Other non-taxable compensation", 1_000m),
            new GovernmentReportLineDto("Total non-taxable compensation", 92_600m),
            new GovernmentReportLineDto("Total taxable compensation", 58_400m),
            new GovernmentReportLineDto("Total taxes withheld", 9_000m));
        report.Rows.Single().Cells[1].Should().Be("111-222-333-000");
    }

    [Fact]
    public async Task Bir1601C_Uses13thMonthPaidEarlierInTheYearAgainstTheExemption()
    {
        var juan = Person("Cruz", "Juan", (GovernmentIdType.TIN, "111-222-333-000"));
        var mayAdvance = Run(new(2026, 5, 15), new(2026, 5, 20), Entry(juan, thirteenth: 60_000m));
        var december = Run(new(2026, 12, 15), new(2026, 12, 18), Entry(juan, thirteenth: 60_000m));
        _runs.Setup(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync([mayAdvance, december]);
        _runs.Setup(r => r.GetPaidRunsByPayMonthAsync(2026, 12, It.IsAny<CancellationToken>())).ReturnsAsync([december]);
        var clock = new FixedClock(new DateTimeOffset(2027, 1, 10, 0, 0, 0, TimeSpan.Zero));
        var sut = new GovernmentReportService(_runs.Object, _companies.Object, _settings.Object, clock);

        var report = await sut.BuildAsync("1601c", 2026, 12);

        report.Summary.Should().Contain(new GovernmentReportLineDto("13th month pay and other benefits", 30_000m));
    }

    [Fact]
    public async Task PagIbig_SplitsTheNameAndShowsTheDateOfBirth()
    {
        var juan = Person("Cruz", "Juan", (GovernmentIdType.PagIbig, "1234-5678-9012"));
        juan.MiddleName = "Santos";
        _runs.Setup(r => r.GetPaidRunsByPeriodEndMonthAsync(2026, 3, It.IsAny<CancellationToken>()))
             .ReturnsAsync([Run(new(2026, 3, 31), new(2026, 4, 5), Entry(juan, piEe: 200m))]);

        var report = await _sut.BuildAsync("pagibig", 2026, 3);

        report.Rows.Single().Cells.Take(5).Should().Equal("Cruz", "Juan", "Santos", "1990-05-01", "1234-5678-9012");
    }

    [Fact]
    public async Task UnpaidRunsForTheMonth_AreMentioned()
    {
        _runs.Setup(r => r.CountUnpaidRunsAsync(2026, 3, false, It.IsAny<CancellationToken>())).ReturnsAsync(2);

        var report = await _sut.BuildAsync("sss", 2026, 3);

        report.Warnings.Should().Contain("2 payroll runs for this month aren't paid yet and aren't included.");
    }

    [Fact]
    public async Task AnUnknownReport_IsNotFound()
    {
        var act = () => _sut.BuildAsync("gsis", 2026, 3);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Theory]
    [InlineData(2026, 13)]
    [InlineData(2026, 0)]
    [InlineData(2026, 5)]   // after April 2026, the clock's month
    public async Task AMonthThatIsInvalidOrStillAhead_IsRefused(int year, int month)
    {
        var act = () => _sut.BuildAsync("sss", year, month);

        await act.Should().ThrowAsync<DomainException>();
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test tests/PeopleCore.Application.Tests --filter "FullyQualifiedName~GovernmentReportServiceTests" -nodeReuse:false -p:UseSharedCompilation=false`
Expected: build error, `GovernmentReportService` does not exist.

- [ ] **Step 3: Write the interface**

```csharp
namespace PeopleCore.Application.Payroll.GovernmentReports;

public interface IGovernmentReportService
{
    /// <summary>
    /// One agency's report for a month: <c>sss</c>, <c>philhealth</c>, <c>pagibig</c> or <c>1601c</c>.
    /// Throws <see cref="KeyNotFoundException"/> for any other name, and
    /// <see cref="Domain.Exceptions.DomainException"/> for a month outside 1-12 or still ahead.
    /// </summary>
    Task<GovernmentReportDto> BuildAsync(string report, int year, int month, CancellationToken ct = default);
}
```

- [ ] **Step 4: Write the service**

```csharp
using System.Globalization;
using PeopleCore.Application.Common.Time;
using PeopleCore.Application.Organization.Interfaces;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using static PeopleCore.Application.Payroll.GovernmentReports.GovernmentReportMath;

namespace PeopleCore.Application.Payroll.GovernmentReports;

/// <summary>
/// The monthly SSS, PhilHealth, Pag-IBIG and BIR 1601-C reports, built from Paid payroll runs as
/// they stand. SSS, PhilHealth and Pag-IBIG count a run toward the month its period ends in, the
/// month the pay was earned; 1601-C toward the month it was paid, as BIR taxes compensation when
/// paid. Nothing here is stored, so a report cannot disagree with the payslips it summarises.
/// </summary>
public sealed class GovernmentReportService : IGovernmentReportService
{
    private readonly IPayrollRunRepository _runs;
    private readonly ICompanyRepository _companies;
    private readonly IPayrollSettingsRepository _settings;
    private readonly TimeProvider _clock;

    public GovernmentReportService(IPayrollRunRepository runs, ICompanyRepository companies,
        IPayrollSettingsRepository settings, TimeProvider clock)
    {
        _runs = runs;
        _companies = companies;
        _settings = settings;
        _clock = clock;
    }

    public async Task<GovernmentReportDto> BuildAsync(string report, int year, int month, CancellationToken ct = default)
    {
        var key = report.ToLowerInvariant();
        if (key is not ("sss" or "philhealth" or "pagibig" or "1601c"))
            throw new KeyNotFoundException($"There is no '{report}' report.");

        if (month is < 1 or > 12)
            throw new DomainException("Choose a month from 1 to 12.");
        var today = DateOnly.FromDateTime(PhilippineTime.Now(_clock));
        if (new DateOnly(year, month, 1) > new DateOnly(today.Year, today.Month, 1))
            throw new DomainException($"{MonthName(year, month)} hasn't started yet, so there is nothing to report.");

        bool byPayDate = key == "1601c";
        var runs = byPayDate
            ? await _runs.GetPaidRunsByPayMonthAsync(year, month, ct)
            : await _runs.GetPaidRunsByPeriodEndMonthAsync(year, month, ct);
        var people = runs.SelectMany(r => r.Employees)
            .GroupBy(e => e.EmployeeId)
            .Select(g => (Employee: g.First().Employee!, Entries: g.ToList()))
            .OrderBy(p => p.Employee.LastName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Employee.FirstName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var company = await _companies.GetDefaultAsync(ct);
        var warnings = new List<string>();
        var basis = byPayDate ? $"Paid in {MonthName(year, month)}" : $"Pay earned in {MonthName(year, month)}";

        var (title, idType, agency, employerNumber) = key switch
        {
            "sss" => ("SSS contributions", GovernmentIdType.SSS, "SSS", company?.SSSNumber),
            "philhealth" => ("PhilHealth premiums", GovernmentIdType.PhilHealth, "PhilHealth", company?.PhilHealthNumber),
            "pagibig" => ("Pag-IBIG contributions", GovernmentIdType.PagIbig, "Pag-IBIG", company?.PagIbigNumber),
            _ => ("BIR 1601-C", GovernmentIdType.TIN, "TIN", company?.TIN)
        };

        if (company is null)
            warnings.Add("The company's details are missing. Fill in the Company page.");
        else if (string.IsNullOrWhiteSpace(employerNumber))
            warnings.Add($"The company's {agency} number is blank. Add it on the Company page.");

        (IReadOnlyList<string> columns, List<GovernmentReportRowDto> rows, IReadOnlyList<string> totals,
         IReadOnlyList<GovernmentReportLineDto> summary) = key switch
        {
            "sss" => await SssAsync(people, warnings, ct),
            "philhealth" => PhilHealth(people),
            "pagibig" => PagIbig(people),
            _ => await Bir1601CAsync(people, year, month, ct)
        };

        int missing = rows.Count(r => r.MissingNumber);
        if (missing > 0)
            warnings.Add(missing == 1
                ? $"1 employee has no {agency} number."
                : $"{missing} employees have no {agency} number.");

        int unpaid = await _runs.CountUnpaidRunsAsync(year, month, byPayDate, ct);
        if (unpaid > 0)
            warnings.Add(unpaid == 1
                ? "1 payroll run for this month isn't paid yet and isn't included."
                : $"{unpaid} payroll runs for this month aren't paid yet and aren't included.");

        var employer = new GovernmentReportEmployerDto(
            company?.Name ?? "", company?.Address, company?.TIN ?? "", company?.RdoCode, employerNumber ?? "");

        return new GovernmentReportDto(key, title, year, month, basis, employer, columns, rows, totals, summary, warnings);
    }

    private async Task<(IReadOnlyList<string>, List<GovernmentReportRowDto>, IReadOnlyList<string>, IReadOnlyList<GovernmentReportLineDto>)>
        SssAsync(List<(Employee Employee, List<PayrollRunEmployee> Entries)> people, List<string> warnings, CancellationToken ct)
    {
        // Working the MSC and EC back only holds under the statutory schedule, which is what
        // ComputeSSS uses unless both rates are overridden.
        var settings = await _settings.GetDefaultAsync(ct);
        bool overridden = settings?.SSSEmployeeRate is not null && settings.SSSEmployerRate is not null;
        if (overridden)
            warnings.Add("The company's payroll settings override the SSS rates, so the MSC and EC can't be worked out and are left blank.");

        var rows = new List<GovernmentReportRowDto>();
        decimal ee = 0, erSs = 0, ec = 0, er = 0;
        foreach (var (employee, entries) in people)
        {
            decimal employeeShare = entries.Sum(e => e.SSSEmployee);
            decimal employerTotal = entries.Sum(e => e.SSSEmployer);
            var (msc, credit) = overridden ? ((decimal?)null, (decimal?)null) : SssCredit(employeeShare);
            decimal? employerSs = credit is { } c ? employerTotal - c : null;

            ee += employeeShare; er += employerTotal; ec += credit ?? 0; erSs += employerSs ?? 0;
            var number = Id(employee, GovernmentIdType.SSS);
            rows.Add(new(employee.Id, [
                FullName(employee), number ?? "", Blank(msc), Money(employeeShare), Blank(employerSs), Blank(credit),
                Money(employerTotal), Money(employeeShare + employerTotal)
            ], number is null));
        }

        return (["Employee", "SSS number", "MSC", "Employee share", "Employer share", "EC", "Employer total", "Total"],
                rows,
                ["Total", "", "", Money(ee), overridden ? "" : Money(erSs), overridden ? "" : Money(ec), Money(er), Money(ee + er)],
                []);
    }

    private static (IReadOnlyList<string>, List<GovernmentReportRowDto>, IReadOnlyList<string>, IReadOnlyList<GovernmentReportLineDto>)
        PhilHealth(List<(Employee Employee, List<PayrollRunEmployee> Entries)> people)
    {
        var rows = new List<GovernmentReportRowDto>();
        decimal ee = 0, er = 0;
        foreach (var (employee, entries) in people)
        {
            decimal employeeShare = entries.Sum(e => e.PhilHealthEmployee);
            decimal employerShare = entries.Sum(e => e.PhilHealthEmployer);
            ee += employeeShare; er += employerShare;
            var number = Id(employee, GovernmentIdType.PhilHealth);
            rows.Add(new(employee.Id, [FullName(employee), number ?? "", Money(employeeShare), Money(employerShare),
                                       Money(employeeShare + employerShare)], number is null));
        }

        return (["Employee", "PhilHealth number", "Employee share", "Employer share", "Total"], rows,
                ["Total", "", Money(ee), Money(er), Money(ee + er)], []);
    }

    private static (IReadOnlyList<string>, List<GovernmentReportRowDto>, IReadOnlyList<string>, IReadOnlyList<GovernmentReportLineDto>)
        PagIbig(List<(Employee Employee, List<PayrollRunEmployee> Entries)> people)
    {
        var rows = new List<GovernmentReportRowDto>();
        decimal ee = 0, er = 0;
        foreach (var (employee, entries) in people)
        {
            decimal employeeShare = entries.Sum(e => e.PagIbigEmployee);
            decimal employerShare = entries.Sum(e => e.PagIbigEmployer);
            ee += employeeShare; er += employerShare;
            var number = Id(employee, GovernmentIdType.PagIbig);
            rows.Add(new(employee.Id, [
                employee.LastName, employee.FirstName, employee.MiddleName ?? "",
                employee.DateOfBirth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), number ?? "",
                Money(employeeShare), Money(employerShare), Money(employeeShare + employerShare)
            ], number is null));
        }

        return (["Last name", "First name", "Middle name", "Date of birth", "Pag-IBIG number", "Employee share", "Employer share", "Total"],
                rows, ["Total", "", "", "", "", Money(ee), Money(er), Money(ee + er)], []);
    }

    private async Task<(IReadOnlyList<string>, List<GovernmentReportRowDto>, IReadOnlyList<string>, IReadOnlyList<GovernmentReportLineDto>)>
        Bir1601CAsync(List<(Employee Employee, List<PayrollRunEmployee> Entries)> people, int year, int month, CancellationToken ct)
    {
        // 13th month paid earlier in the year has used its share of the exemption first, as it did
        // when payroll withheld tax on this month's.
        var monthStart = new DateOnly(year, month, 1);
        var earlierThirteenth = (await _runs.GetPaidRunsInYearAsync(year, ct))
            .Where(r => r.PayDate < monthStart)
            .SelectMany(r => r.Employees)
            .GroupBy(e => e.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Sum(e => e.ThirteenthMonth));

        var rows = new List<GovernmentReportRowDto>();
        decimal gross = 0, thirteenth = 0, shares = 0, allowances = 0, taxable = 0, tax = 0;
        foreach (var (employee, entries) in people)
        {
            decimal g = entries.Sum(e => e.GrossPay);
            decimal t13 = NonTaxableThirteenthMonth(entries.Sum(e => e.ThirteenthMonth),
                                                    earlierThirteenth.GetValueOrDefault(employee.Id));
            decimal s = entries.Sum(e => e.SSSEmployee + e.PhilHealthEmployee + e.PagIbigEmployee);
            decimal a = entries.Sum(e => e.NonTaxableAllowances);
            decimal tx = g - t13 - s - a;
            decimal w = entries.Sum(e => e.WithholdingTax);

            gross += g; thirteenth += t13; shares += s; allowances += a; taxable += tx; tax += w;
            var tin = Id(employee, GovernmentIdType.TIN);
            rows.Add(new(employee.Id, [FullName(employee), tin ?? "", Money(g), Money(t13), Money(s), Money(a), Money(tx), Money(w)],
                         tin is null));
        }

        decimal nonTaxable = thirteenth + shares + allowances;
        return (["Employee", "TIN", "Compensation", "13th month (non-taxable)", "Employee shares", "Other non-taxable", "Taxable", "Tax withheld"],
                rows,
                ["Total", "", Money(gross), Money(thirteenth), Money(shares), Money(allowances), Money(taxable), Money(tax)],
                [
                    new("Total amount of compensation", gross),
                    new("Statutory minimum wage (MWEs)", 0m),
                    new("Holiday, overtime, night differential and hazard pay (MWEs)", 0m),
                    new("13th month pay and other benefits", thirteenth),
                    new("De minimis benefits", 0m),
                    new("SSS, PhilHealth and Pag-IBIG employee shares", shares),
                    new("Other non-taxable compensation", allowances),
                    new("Total non-taxable compensation", nonTaxable),
                    new("Total taxable compensation", gross - nonTaxable),
                    new("Total taxes withheld", tax)
                ]);
    }

    private static string? Id(Employee employee, GovernmentIdType type)
        => employee.GovernmentIds.FirstOrDefault(g => g.IdType == type && !string.IsNullOrWhiteSpace(g.IdNumber))?.IdNumber;

    private static string FullName(Employee e)
        => string.IsNullOrWhiteSpace(e.MiddleName) ? $"{e.LastName}, {e.FirstName}" : $"{e.LastName}, {e.FirstName} {e.MiddleName}";

    private static string Blank(decimal? amount) => amount is { } a ? Money(a) : "";

    private static string MonthName(int year, int month)
        => new DateOnly(year, month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);
}
```

Note the expected cells in `Sss_CombinesAMonthsCutoffsIntoOneRow...`: employee share 1,000 → MSC 20,000 → EC 30; employer total 2,030 → employer SS 2,000.

- [ ] **Step 5: Run the tests and confirm they pass**

Run: the command from Step 2. Expected: all pass. If `Employee.MiddleName` is non-nullable in the PeopleCore entity, `?? ""` is harmless; keep it.

- [ ] **Step 6: Commit**

```bash
git add src/PeopleCore.Application/Payroll/GovernmentReports tests/PeopleCore.Application.Tests/Payroll/GovernmentReportServiceTests.cs
git commit -m "feat(payroll): build the monthly SSS, PhilHealth, Pag-IBIG and 1601-C reports"
```

---

### Task 4: CSV

**Files:**
- Create: `src/PeopleCore.Application/Payroll/GovernmentReports/GovernmentReportCsv.cs`
- Test: `tests/PeopleCore.Application.Tests/Payroll/GovernmentReportCsvTests.cs`

**Interfaces:**
- Consumes: `GovernmentReportDto` (Task 1).
- Produces: `GovernmentReportCsv.Write(GovernmentReportDto) : byte[]`, `GovernmentReportCsv.FileName(GovernmentReportDto) : string`, `GovernmentReportCsv.ContentType = "text/csv"`.

The Application project has no CsvHelper reference; a small writer with RFC 4180 quoting is enough, like `AttendanceImportTemplate.Csv()`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text;
using FluentAssertions;
using PeopleCore.Application.Payroll.GovernmentReports;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class GovernmentReportCsvTests
{
    private static GovernmentReportDto Report(IReadOnlyList<GovernmentReportLineDto>? summary = null) => new(
        "sss", "SSS contributions", 2026, 3, "Pay earned in March 2026",
        new GovernmentReportEmployerDto("Acme, Inc.", null, "123-456-789-000", "050", "03-9999999-1"),
        ["Employee", "SSS number", "Total"],
        [new GovernmentReportRowDto(Guid.NewGuid(), ["Cruz, Juan", "34-1234567-8", "3030.00"], false)],
        ["Total", "", "3030.00"],
        summary ?? [],
        []);

    private static string Text(byte[] bytes)
    {
        bytes.Take(3).Should().Equal(Encoding.UTF8.GetPreamble(), "Excel needs the BOM to read UTF-8");
        return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
    }

    [Fact]
    public void Write_PutsTheEmployerAboveTheTable_AndQuotesCommas()
    {
        var lines = Text(GovernmentReportCsv.Write(Report())).Split("\r\n");

        lines.Should().StartWith([
            "\"Acme, Inc.\"",
            "SSS contributions,Pay earned in March 2026",
            "TIN,123-456-789-000",
            "Employer number,03-9999999-1",
            "",
            "Employee,SSS number,Total",
            "\"Cruz, Juan\",34-1234567-8,3030.00",
            "Total,,3030.00"
        ]);
    }

    [Fact]
    public void Write_Adds1601CsLinesBelowTheTable()
    {
        var text = Text(GovernmentReportCsv.Write(Report([new("Total taxes withheld", 9_000m)])));

        text.Should().EndWith("\r\nTotal taxes withheld,9000.00\r\n");
    }

    [Fact]
    public void FileName_IsTheReportAndMonth()
    {
        GovernmentReportCsv.FileName(Report()).Should().Be("sss-2026-03.csv");
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test tests/PeopleCore.Application.Tests --filter "FullyQualifiedName~GovernmentReportCsvTests" -nodeReuse:false -p:UseSharedCompilation=false`
Expected: build error, `GovernmentReportCsv` does not exist.

- [ ] **Step 3: Implement**

```csharp
using System.Text;

namespace PeopleCore.Application.Payroll.GovernmentReports;

/// <summary>A government report as CSV: the employer, a blank line, the table, then any form lines.</summary>
public static class GovernmentReportCsv
{
    public const string ContentType = "text/csv";

    public static string FileName(GovernmentReportDto report) => $"{report.Report}-{report.Year:D4}-{report.Month:D2}.csv";

    public static byte[] Write(GovernmentReportDto report)
    {
        var text = new StringBuilder();
        void Line(params string[] cells) => text.Append(string.Join(",", cells.Select(Quote))).Append("\r\n");

        Line(report.Employer.Name);
        Line(report.Title, report.Basis);
        Line("TIN", report.Employer.Tin);
        Line("Employer number", report.Employer.AgencyNumber);
        Line();
        Line([.. report.Columns]);
        foreach (var row in report.Rows)
            Line([.. row.Cells]);
        Line([.. report.Totals]);

        if (report.Summary.Count > 0)
        {
            Line();
            foreach (var line in report.Summary)
                Line(line.Label, GovernmentReportMath.Money(line.Amount));
        }

        // With a BOM, so Excel opens it as UTF-8.
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }

    private static string Quote(string cell)
        => cell.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{cell.Replace("\"", "\"\"")}\"" : cell;
}
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: the command from Step 2. Expected: all 3 pass.

- [ ] **Step 5: Commit**

```bash
git add src/PeopleCore.Application/Payroll/GovernmentReports/GovernmentReportCsv.cs tests/PeopleCore.Application.Tests/Payroll/GovernmentReportCsvTests.cs
git commit -m "feat(payroll): download a government report as CSV"
```

---

### Task 5: API endpoint

**Files:**
- Create: `src/PeopleCore.API/Controllers/Payroll/GovernmentReportsController.cs`
- Modify: `src/PeopleCore.API/Extensions/ServiceExtensions.cs` (after `services.AddScoped<IBir2316Renderer, Bir2316Renderer>();`, about line 223)
- Test: `tests/PeopleCore.Application.Tests/Payroll/GovernmentReportsControllerTests.cs`

**Interfaces:**
- Consumes: `IGovernmentReportService` (Task 3), `GovernmentReportCsv` (Task 4).
- Produces: `GET api/reports/government/{report}?year=&month=[&format=csv]`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PeopleCore.API.Authorization;
using PeopleCore.API.Controllers.Payroll;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Application.Payroll.GovernmentReports;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class GovernmentReportsControllerTests
{
    private readonly Mock<IGovernmentReportService> _service = new();

    private static readonly GovernmentReportDto Report = new(
        "sss", "SSS contributions", 2026, 3, "Pay earned in March 2026",
        new GovernmentReportEmployerDto("Acme", null, "", null, ""), ["Employee"], [], ["Total"], [], []);

    [Fact]
    public void RequiresPayrollManage()
    {
        typeof(GovernmentReportsController).GetCustomAttribute<RequirePermissionAttribute>()!
            .AnyOf.Should().BeEquivalentTo([Permissions.PayrollManage]);
    }

    [Fact]
    public async Task Get_ReturnsTheReport()
    {
        _service.Setup(s => s.BuildAsync("sss", 2026, 3, It.IsAny<CancellationToken>())).ReturnsAsync(Report);

        var result = await new GovernmentReportsController(_service.Object).Get("sss", 2026, 3, null, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(Report);
    }

    [Fact]
    public async Task Get_WithCsv_ReturnsTheFile()
    {
        _service.Setup(s => s.BuildAsync("sss", 2026, 3, It.IsAny<CancellationToken>())).ReturnsAsync(Report);

        var result = await new GovernmentReportsController(_service.Object).Get("sss", 2026, 3, "csv", CancellationToken.None);

        var file = result.Should().BeOfType<FileContentResult>().Subject;
        file.ContentType.Should().Be("text/csv");
        file.FileDownloadName.Should().Be("sss-2026-03.csv");
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test tests/PeopleCore.Application.Tests --filter "FullyQualifiedName~GovernmentReportsControllerTests" -nodeReuse:false -p:UseSharedCompilation=false`
Expected: build error, `GovernmentReportsController` does not exist.

- [ ] **Step 3: Write the controller**

```csharp
using Microsoft.AspNetCore.Mvc;
using PeopleCore.API.Authorization;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Application.Payroll.GovernmentReports;

namespace PeopleCore.API.Controllers.Payroll;

/// <summary>
/// The monthly government remittance reports. An unknown report name is a 404 and a bad or future
/// month a 400, both raised by the service and mapped by the exception middleware.
/// </summary>
[ApiController]
[Route("api/reports/government")]
[RequirePermission(Permissions.PayrollManage)]
public class GovernmentReportsController : ControllerBase
{
    private readonly IGovernmentReportService _service;

    public GovernmentReportsController(IGovernmentReportService service) => _service = service;

    [HttpGet("{report}")]
    public async Task<IActionResult> Get(string report, [FromQuery] int year, [FromQuery] int month,
        [FromQuery] string? format, CancellationToken ct)
    {
        var dto = await _service.BuildAsync(report, year, month, ct);
        return string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)
            ? File(GovernmentReportCsv.Write(dto), GovernmentReportCsv.ContentType, GovernmentReportCsv.FileName(dto))
            : Ok(dto);
    }
}
```

- [ ] **Step 4: Register the service** in `ServiceExtensions.cs` after the `IBir2316Renderer` line:

```csharp
        services.AddScoped<IGovernmentReportService, GovernmentReportService>();
```

Add `using PeopleCore.Application.Payroll.GovernmentReports;` at the top of the file.

- [ ] **Step 5: Run the tests and confirm they pass**

Run: the command from Step 2. Expected: all 3 pass. Also run `dotnet build src/PeopleCore.API -nodeReuse:false -p:UseSharedCompilation=false`. Expected: build succeeded.

- [ ] **Step 6: Commit**

```bash
git add src/PeopleCore.API tests/PeopleCore.Application.Tests/Payroll/GovernmentReportsControllerTests.cs
git commit -m "feat(api): government remittance reports endpoint, as JSON or CSV"
```

---

### Task 6: Government Reports page

**Files:**
- Modify: `src/PeopleCore.Web/Services/ApiClient.cs` (the methods go after the BIR 2316 block, about line 590; the records go at the end of the file with the other records)
- Create: `src/PeopleCore.Web/Pages/Payroll/GovernmentReports.razor`
- Modify: `src/PeopleCore.Web/Layout/NavMenu.razor:67` (add after the BIR 2316 entry)
- Test: `tests/PeopleCore.Web.Tests/Pages/Payroll/GovernmentReportsTests.cs`

**Interfaces:**
- Consumes: the endpoint from Task 5.
- Produces: `ApiClient.GetGovernmentReportAsync(string report, int year, int month) : Task<GovernmentReportDto?>` and `ApiClient.GetGovernmentReportCsvAsync(string report, int year, int month) : Task<byte[]>`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PeopleCore.Web.Pages.Payroll;
using PeopleCore.Web.Services;
using PeopleCore.Web.Tests.TestSupport;

namespace PeopleCore.Web.Tests.Pages.Payroll;

public class GovernmentReportsTests : BunitContext
{
    private readonly StubHttpHandler _api = new();
    private static readonly DateTime LastMonth = DateTime.Today.AddMonths(-1);

    public GovernmentReportsTests()
    {
        Services.AddSingleton(new ApiClient(StubHttpHandler.ClientFor(_api)));
        JSInterop.SetupVoid("downloadFileFromBytes", _ => true);
    }

    private static string Path(string report, DateTime month) =>
        $"/api/reports/government/{report}?year={month.Year}&month={month.Month}";

    private static string Report(string report, string warnings = "[]", string rows = """[{"employeeId":"11111111-1111-1111-1111-111111111111","cells":["Cruz, Juan","34-1234567-8","3030.00"],"missingNumber":false}]""") =>
        $$"""
        {"report":"{{report}}","title":"SSS contributions","year":2026,"month":3,"basis":"Pay earned in March 2026",
         "employer":{"name":"Acme","address":null,"tin":"123","rdoCode":"050","agencyNumber":"03-9999999-1"},
         "columns":["Employee","SSS number","Total"],"rows":{{rows}},"totals":["Total","","3030.00"],
         "summary":[],"warnings":{{warnings}}}
        """;

    [Fact]
    public void OpensOnLastMonthsSssList()
    {
        _api.On(HttpMethod.Get, Path("sss", LastMonth), HttpStatusCode.OK, Report("sss"));

        var cut = Render<GovernmentReports>();

        cut.WaitForAssertion(() => cut.Find("[data-report-table]").TextContent.Should().Contain("Cruz, Juan"));
        cut.Find("[data-report-totals]").TextContent.Should().Contain("3030.00");
    }

    [Fact]
    public void SwitchingTab_LoadsThatAgencysReport()
    {
        _api.On(HttpMethod.Get, Path("sss", LastMonth), HttpStatusCode.OK, Report("sss"))
            .On(HttpMethod.Get, Path("philhealth", LastMonth), HttpStatusCode.OK, Report("philhealth"));
        var cut = Render<GovernmentReports>();
        cut.WaitForElement("[data-report-table]");

        cut.Find("[data-tab='philhealth']").Click();

        cut.WaitForAssertion(() => _api.Requests.Should().Contain(r => r.RequestUri!.PathAndQuery == Path("philhealth", LastMonth)));
    }

    [Fact]
    public void ShowsTheWarnings()
    {
        _api.On(HttpMethod.Get, Path("sss", LastMonth), HttpStatusCode.OK,
            Report("sss", warnings: """["1 employee has no SSS number."]"""));

        var cut = Render<GovernmentReports>();

        cut.WaitForAssertion(() => cut.Find("[data-report-warnings]").TextContent.Should().Contain("1 employee has no SSS number."));
    }

    [Fact]
    public void AMonthWithNothingPaid_SaysSo()
    {
        _api.On(HttpMethod.Get, Path("sss", LastMonth), HttpStatusCode.OK, Report("sss", rows: "[]"));

        var cut = Render<GovernmentReports>();

        cut.WaitForAssertion(() => cut.Find("[data-report-empty]").TextContent.Should().Contain("No payroll was paid"));
    }

    [Fact]
    public void DownloadCsv_RequestsTheCsv()
    {
        _api.On(HttpMethod.Get, Path("sss", LastMonth), HttpStatusCode.OK, Report("sss"))
            .On(HttpMethod.Get, Path("sss", LastMonth) + "&format=csv", HttpStatusCode.OK, "Acme\r\n");
        var cut = Render<GovernmentReports>();
        cut.WaitForElement("[data-report-table]");

        cut.Find("[data-download-csv]").Click();

        cut.WaitForAssertion(() => JSInterop.VerifyInvoke("downloadFileFromBytes"));
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test tests/PeopleCore.Web.Tests --filter "FullyQualifiedName~GovernmentReportsTests" -nodeReuse:false -p:UseSharedCompilation=false`
Expected: build error, `GovernmentReports` does not exist.

- [ ] **Step 3: Add the client methods and records** to `ApiClient.cs`. The methods go after `GenerateAllBir2316Async`:

```csharp
    // Government remittance reports
    public async Task<GovernmentReportDto?> GetGovernmentReportAsync(string report, int year, int month)
        => await GetJsonAsync<GovernmentReportDto>($"api/reports/government/{report}?year={year}&month={month}");

    public async Task<byte[]> GetGovernmentReportCsvAsync(string report, int year, int month)
    {
        var response = await _http.GetAsync($"api/reports/government/{report}?year={year}&month={month}&format=csv");
        await EnsureSuccessAsync(response);
        return await response.Content.ReadAsByteArrayAsync();
    }
```

The records go at the end of the file:

```csharp
public record GovernmentReportDto(string Report, string Title, int Year, int Month, string Basis,
    GovernmentReportEmployerDto Employer, IReadOnlyList<string> Columns, IReadOnlyList<GovernmentReportRowDto> Rows,
    IReadOnlyList<string> Totals, IReadOnlyList<GovernmentReportLineDto> Summary, IReadOnlyList<string> Warnings);
public record GovernmentReportRowDto(Guid EmployeeId, IReadOnlyList<string> Cells, bool MissingNumber);
public record GovernmentReportLineDto(string Label, decimal Amount);
public record GovernmentReportEmployerDto(string Name, string? Address, string Tin, string? RdoCode, string AgencyNumber);
```

Check first that `GetJsonAsync<T>` throws on a non-success status with the API's detail, as the other callers rely on (see `ApiClient.cs:601`).

- [ ] **Step 4: Write the page**

`src/PeopleCore.Web/Pages/Payroll/GovernmentReports.razor`:

```razor
@page "/government-reports"
@attribute [RequirePermission(Permissions.PayrollManage)]
@inject ApiClient Api
@inject IJSRuntime JS

<PageTitle>Government Reports</PageTitle>

<PageHeader Title="Government Reports" Section="Payroll"
            Description="Each month's SSS, PhilHealth, Pag-IBIG and BIR 1601-C figures, from paid payroll runs.">
    <Actions>
        @if (_report is { Rows.Count: > 0 })
        {
            <Button Variant="outline" OnClick="DownloadCsv" Loading="@_downloading" Disabled="@_downloading" data-download-csv>
                <i class="bi bi-download mr-1"></i> Download CSV
            </Button>
        }
    </Actions>
</PageHeader>

<Card Class="mb-4">
    <CardContent Class="pt-6 flex flex-wrap items-end gap-4">
        <div>
            <Label For="report-month">Month</Label>
            <input id="report-month" type="month" class="border rounded-md px-3 py-2 bg-background"
                   value="@_month.ToString("yyyy-MM")" @onchange="ChangeMonth" />
        </div>
        <div class="flex gap-2" role="tablist">
            @foreach (var (key, label) in Tabs)
            {
                <Button Size="sm" Variant="@(key == _tab ? "primary" : "outline")" data-tab="@key"
                        OnClick="@(() => SelectTab(key))">@label</Button>
            }
        </div>
    </CardContent>
</Card>

@if (_error is not null)
{
    <Alert Variant="destructive" Class="mb-4">@_error</Alert>
}
else if (_loading)
{
    <p class="text-sm text-muted-foreground">Loading…</p>
}
else if (_report is not null)
{
    <div class="mb-4 text-sm">
        <p class="font-semibold">@_report.Employer.Name</p>
        <p class="text-muted-foreground">@_report.Title · @_report.Basis · TIN @_report.Employer.Tin
            @if (_report.Report != "1601c") { <span> · Employer no. @_report.Employer.AgencyNumber</span> }</p>
    </div>

    @if (_report.Warnings.Count > 0)
    {
        <Alert Class="mb-4" data-report-warnings>
            <ul class="list-disc pl-4">@foreach (var w in _report.Warnings) { <li>@w</li> }</ul>
        </Alert>
    }

    @if (_report.Rows.Count == 0)
    {
        <p class="text-sm text-muted-foreground" data-report-empty>No payroll was paid for @_month.ToString("MMMM yyyy").</p>
    }
    else
    {
        <div class="overflow-x-auto rounded-md border">
            <table class="w-full text-sm" data-report-table>
                <thead><tr>@foreach (var c in _report.Columns) { <th class="px-3 py-2 text-left">@c</th> }</tr></thead>
                <tbody>
                    @foreach (var row in _report.Rows)
                    {
                        <tr class="@(row.MissingNumber ? "bg-destructive/10" : "")">
                            @foreach (var cell in row.Cells) { <td class="px-3 py-2">@cell</td> }
                        </tr>
                    }
                </tbody>
                <tfoot><tr class="font-semibold" data-report-totals>@foreach (var t in _report.Totals) { <td class="px-3 py-2">@t</td> }</tr></tfoot>
            </table>
        </div>

        @if (_report.Summary.Count > 0)
        {
            <table class="mt-6 text-sm" data-report-summary>
                @foreach (var line in _report.Summary)
                {
                    <tr><td class="pr-6 py-1">@line.Label</td><td class="text-right">@line.Amount.ToString("N2")</td></tr>
                }
            </table>
        }
    }
}

@code {
    private static readonly (string Key, string Label)[] Tabs =
        [("sss", "SSS"), ("philhealth", "PhilHealth"), ("pagibig", "Pag-IBIG"), ("1601c", "BIR 1601-C")];

    // Remittances are for the month just ended.
    private DateTime _month = new(DateTime.Today.AddMonths(-1).Year, DateTime.Today.AddMonths(-1).Month, 1);
    private string _tab = "sss";
    private GovernmentReportDto? _report;
    private bool _loading, _downloading;
    private string? _error;

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task SelectTab(string key)
    {
        _tab = key;
        await LoadAsync();
    }

    private async Task ChangeMonth(ChangeEventArgs e)
    {
        if (DateTime.TryParse($"{e.Value}-01", out var month))
        {
            _month = month;
            await LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        _loading = true;
        _error = null;
        try
        {
            _report = await Api.GetGovernmentReportAsync(_tab, _month.Year, _month.Month);
        }
        catch (HttpRequestException ex)
        {
            _report = null;
            _error = ex.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task DownloadCsv()
    {
        _downloading = true;
        try
        {
            var csv = await Api.GetGovernmentReportCsvAsync(_tab, _month.Year, _month.Month);
            await JS.InvokeVoidAsync("downloadFileFromBytes", Convert.ToBase64String(csv),
                $"{_tab}-{_month:yyyy-MM}.csv", "text/csv");
        }
        catch (HttpRequestException ex)
        {
            _error = ex.Message;
        }
        finally
        {
            _downloading = false;
        }
    }
}
```

Match the component names (`Card`, `CardContent`, `Button`, `Alert`, `Label`, `PageHeader`) and their parameters against `Pages/Payroll/Bir2316.razor`. If `Button` has no `data-*` passthrough (check for `AdditionalAttributes` / `CaptureUnmatchedValues`), use a plain `<button>` with the same classes for the tab and download buttons, so the tests can find them.

- [ ] **Step 5: Add the nav entry** in `NavMenu.razor` after the BIR 2316 line:

```csharp
            new("/bir-2316", "BIR Form 2316", "bi-file-earmark-text"),
            new("/government-reports", "Government Reports", "bi-bank")
```

(add the comma after the BIR 2316 entry).

- [ ] **Step 6: Run the tests and confirm they pass**

Run: the command from Step 2. Expected: all 5 pass.

- [ ] **Step 7: Commit**

```bash
git add src/PeopleCore.Web tests/PeopleCore.Web.Tests/Pages/Payroll/GovernmentReportsTests.cs
git commit -m "feat(web): Government Reports page with a tab per agency and CSV download"
```

---

### Task 7: Full verification

- [ ] **Step 1: Correct the spec's CSV line.** In `docs/superpowers/specs/2026-09-21-government-remittance-reports-design.md`, change "writes each report DTO as CSV with CsvHelper" to "writes each report DTO as CSV (a small writer; the Application project doesn't reference CsvHelper)".

- [ ] **Step 2: Run the whole suite**

Run: `dotnet test PeopleCore.slnx -nodeReuse:false -p:UseSharedCompilation=false`
Expected: every project passes. If `RolesTests.WhenThePermissionListFailsToLoad...` fails alone, re-run it; it is a known timing test being fixed in another session.

- [ ] **Step 3: Check it in the browser.** Start the app via the preview tools. Log in as an HR or payroll user and open `/government-reports`. Confirm that each tab loads, a month with paid runs shows rows and totals, and Download CSV saves a file that opens in Excel.

- [ ] **Step 4: Commit**

```bash
git add docs/superpowers/specs/2026-09-21-government-remittance-reports-design.md
git commit -m "docs(spec): the government report CSV is a small writer, not CsvHelper"
```
