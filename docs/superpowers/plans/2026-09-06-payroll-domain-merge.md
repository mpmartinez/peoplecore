# Payroll Domain Merge — Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move PayZen's Philippine payroll domain into the PeopleCore solution under PeopleCore naming, with its statutory arithmetic provably unchanged.

**Architecture:** Pure statutory rules (SSS/PhilHealth/Pag-IBIG tables, BIR TRAIN brackets, DOLE premium rates) land in `PeopleCore.Domain/Payroll`. The orchestrating `PayrollComputationService` lands in `PeopleCore.Application/Payroll/Services`. Compensation splits off `Employee` into its own `EmployeeCompensation` entity. Attendance is not wired up in this phase; the computation takes a `PayrollAttendanceInput` record that Phase 2 will populate.

**Tech Stack:** .NET 10 · EF Core 10 + Npgsql (snake_case) · xUnit · Moq · FluentAssertions

**Design spec:** [`docs/superpowers/specs/2026-09-06-payroll-domain-merge-design.md`](../specs/2026-09-06-payroll-domain-merge-design.md)

**Source of the port:** `C:\M2NET PROJECTS\payzen` (read-only; do not modify it)

## Global Constraints

- Target framework `net10.0` on every project.
- Money columns are `numeric(18,2)`; rates are `numeric(8,4)`; day/hour counts are `numeric(6,2)`; `DailyRateFactor` is `numeric(8,2)`.
- Tables and columns are snake_case (configured globally by `UseSnakeCaseNamingConvention`).
- All payroll entities derive from `AuditableEntity` so they participate in audit stamping.
- All payroll endpoints carry `[Authorize(Roles = "Admin,HRManager,PayrollService")]`.
- Ported test files keep their **expected values byte-identical**. Arrange sections may change to build `EmployeeCompensation` instead of `Employee`; assertions may not change.
- Namespace rename throughout the port: `PayZen.Shared.*` and `PayZen.Server.*` become `PeopleCore.Domain.*` / `PeopleCore.Application.*` per the file's destination.
- Never edit an existing SSS schedule entry. A new circular is a new effective-dated entry.
- Do not port `AttendancePeriod`, `AttendanceRecord`, `AttendanceCustomValue`, `AttendanceColumnMapping`, `User`, `AuthService`, `PermissionDefinitions`, `PermissionService`, `RequirePermissionAttribute`, or `Employee.DailyRate` / `HourlyRate`.

**One deviation from the design spec.** The spec's architecture listing named `PhilHealthRates` and `PagIbigRates` as separate Domain files. This plan leaves `ComputePhilHealth` and `ComputePagIbig` as methods on `PayrollComputationService`, where PayZen has them, and puts only their *rate values* in Domain via `ContributionRates`. Extracting the two methods is refactoring beyond a port, and the whole phase is staked on the arithmetic not moving. Extracting them is a reasonable follow-up once the ported tests are green.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/M2NET.Core/**` | Vendored shared `Employee` base and enums |
| `src/PeopleCore.Domain/Enums/PayrollEnums.cs` | `PayFrequency`, `PayrollRunStatus`, `AllowanceType`, `LoanType` |
| `src/PeopleCore.Domain/Payroll/DolePremiumRates.cs` | `WorkDayType` + DOLE premium rate rule |
| `src/PeopleCore.Domain/Payroll/BirWithholdingTax.cs` | TRAIN annual bracket table |
| `src/PeopleCore.Domain/Payroll/ContributionRates.cs` | Rate configuration record |
| `src/PeopleCore.Domain/Payroll/SssContributionSchedule.cs` | Effective-dated SSS schedules |
| `src/PeopleCore.Domain/Entities/Payroll/*.cs` | Payroll entities |
| `src/PeopleCore.Application/Payroll/Services/PayrollComputationService.cs` | Orchestration |
| `src/PeopleCore.Application/Payroll/Services/PayrollLineBuilder.cs` | Payslip/register lines |
| `src/PeopleCore.Application/Payroll/DTOs/*.cs` | Payroll DTOs |
| `src/PeopleCore.Infrastructure/Persistence/Configurations/Payroll/*.cs` | EF configuration |
| `src/PeopleCore.API/Controllers/Payroll/*.cs` | Payroll endpoints |

---

### Task 1: Vendor M2NET.Core into the repository

**Files:**
- Create: `src/M2NET.Core/M2NET.Core.csproj`, `src/M2NET.Core/Entities/Employee.cs`, `src/M2NET.Core/Enums/EmploymentEnums.cs`
- Modify: `Directory.Build.props`, `PeopleCore.slnx`

**Interfaces:**
- Consumes: nothing
- Produces: `M2NET.Core.Entities.Employee`, `M2NET.Core.Enums.{Gender, EmploymentType, CivilStatus}` resolvable from inside the repo

- [ ] **Step 1: Copy the source files**

```bash
mkdir -p src/M2NET.Core/Entities src/M2NET.Core/Enums
cp "C:/M2NET PROJECTS/M2NET.Core/src/M2NET.Core/M2NET.Core.csproj" src/M2NET.Core/
cp "C:/M2NET PROJECTS/M2NET.Core/src/M2NET.Core/Entities/Employee.cs" src/M2NET.Core/Entities/
cp "C:/M2NET PROJECTS/M2NET.Core/src/M2NET.Core/Enums/EmploymentEnums.cs" src/M2NET.Core/Enums/
```

- [ ] **Step 2: Replace the upward search in `Directory.Build.props`**

The vendored copy is at a known path, so the ancestor search and its error target are no longer needed. Replace the whole file with:

```xml
<Project>

  <!--
    M2NET.Core is vendored into this repository so the solution is self-contained. Override
    with -p:M2NetCoreProject=<path> only if consuming a copy from elsewhere.
  -->
  <PropertyGroup Condition=" '$(M2NetCoreProject)' == '' ">
    <M2NetCoreProject>$(MSBuildThisFileDirectory)src\M2NET.Core\M2NET.Core.csproj</M2NetCoreProject>
  </PropertyGroup>

</Project>
```

- [ ] **Step 3: Remove the now-unused opt-in flag**

Delete the `<UsesM2NetCore>true</UsesM2NetCore>` line from both `src/PeopleCore.Domain/PeopleCore.Domain.csproj` and `src/PeopleCore.Application/PeopleCore.Application.csproj`. Leave their `ProjectReference` to `$(M2NetCoreProject)` exactly as it is.

- [ ] **Step 4: Add the project to the solution**

Add inside the `/src/` folder element of `PeopleCore.slnx`:

```xml
    <Project Path="src/M2NET.Core/M2NET.Core.csproj" />
```

- [ ] **Step 5: Verify the whole solution still builds and passes**

Run: `dotnet test PeopleCore.slnx --nologo -v q`
Expected: `Passed! - Failed: 0, Passed: 48`

- [ ] **Step 6: Commit**

```bash
git add src/M2NET.Core Directory.Build.props PeopleCore.slnx src/PeopleCore.Domain/PeopleCore.Domain.csproj src/PeopleCore.Application/PeopleCore.Application.csproj
git commit -m "build: vendor M2NET.Core into the repository"
```

---

### Task 2: Payroll enums

**Files:**
- Create: `src/PeopleCore.Domain/Enums/PayrollEnums.cs`

**Interfaces:**
- Consumes: nothing
- Produces: `PeopleCore.Domain.Enums.{PayFrequency, PayrollRunStatus, AllowanceType, LoanType}`

- [ ] **Step 1: Create the file**

`DeductionType` from PayZen is dropped: nothing reads it, and `PayrollLineBuilder` names its lines directly.

```csharp
namespace PeopleCore.Domain.Enums;

public enum PayFrequency { Monthly, SemiMonthly }
public enum PayrollRunStatus { Draft, Processing, ForApproval, Approved, Paid }
public enum AllowanceType { Transportation, Meal, HousingAllowance, Communication, Clothing, Other }
public enum LoanType { SSSLoan, PagIbigLoan, CompanyLoan, CashAdvance, CalamityLoan, Other }
```

- [ ] **Step 2: Build**

Run: `dotnet build src/PeopleCore.Domain/PeopleCore.Domain.csproj --nologo -v q`
Expected: `0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add src/PeopleCore.Domain/Enums/PayrollEnums.cs
git commit -m "feat(payroll): add payroll enums"
```

---

### Task 3: DOLE premium rates

**Files:**
- Create: `src/PeopleCore.Domain/Payroll/DolePremiumRates.cs`
- Test: `tests/PeopleCore.Application.Tests/Payroll/DolePremiumRatesTests.cs`

**Interfaces:**
- Consumes: nothing
- Produces: `PeopleCore.Domain.Payroll.WorkDayType` (10 members), and `DolePremiumRates` with `BaseRate(WorkDayType)`, `OvertimeFactor(WorkDayType)`, `Rate(WorkDayType, bool nightShift = false, bool overtime = false)`, `Premium(WorkDayType, bool nightShift = false, bool overtime = false)`, and constants `NightShiftFactor = 1.10m`, `OrdinaryOvertimeFactor = 1.25m`, `PremiumOvertimeFactor = 1.30m`

- [ ] **Step 1: Port the tests first**

```bash
mkdir -p tests/PeopleCore.Application.Tests/Payroll
cp "C:/M2NET PROJECTS/payzen/tests/PayZen.Server.Tests/Services/DolePremiumRatesTests.cs" tests/PeopleCore.Application.Tests/Payroll/
```

Then edit only the namespace and usings in that file: `namespace PeopleCore.Application.Tests.Payroll;` and `using PeopleCore.Domain.Payroll;`. Change nothing else — all 7 tests keep their expected values.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/PeopleCore.Application.Tests/ --nologo --filter "FullyQualifiedName~DolePremiumRates"`
Expected: FAIL — `CS0246: The type or namespace name 'DolePremiumRates' could not be found`

- [ ] **Step 3: Port the implementation**

```bash
mkdir -p src/PeopleCore.Domain/Payroll
cp "C:/M2NET PROJECTS/payzen/src/PayZen.Shared/Payroll/DolePremiumRates.cs" src/PeopleCore.Domain/Payroll/
```

Change only the namespace line to `namespace PeopleCore.Domain.Payroll;`. Keep every rate, every XML doc comment and the `Rate`/`Premium` distinction exactly as written — the comment explaining why `Premium` subtracts 1.00 is load-bearing.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/PeopleCore.Application.Tests/ --nologo --filter "FullyQualifiedName~DolePremiumRates"`
Expected: `Passed! - Failed: 0, Passed: 7`

- [ ] **Step 5: Commit**

```bash
git add src/PeopleCore.Domain/Payroll/DolePremiumRates.cs tests/PeopleCore.Application.Tests/Payroll/DolePremiumRatesTests.cs
git commit -m "feat(payroll): port DOLE premium rates"
```

---

### Task 4: BIR withholding tax table

**Files:**
- Create: `src/PeopleCore.Domain/Payroll/BirWithholdingTax.cs`
- Test: `tests/PeopleCore.Application.Tests/Payroll/BirWithholdingTaxTests.cs`

**Interfaces:**
- Consumes: nothing
- Produces: `PeopleCore.Domain.Payroll.BirWithholdingTax` with `ComputeAnnualTax(decimal)` and `ComputeAnnualTaxDue(decimal)`

- [ ] **Step 1: Port the tests first**

```bash
cp "C:/M2NET PROJECTS/payzen/tests/PayZen.Server.Tests/Services/BirWithholdingTaxTests.cs" tests/PeopleCore.Application.Tests/Payroll/
```

Edit only the namespace to `PeopleCore.Application.Tests.Payroll` and the using to `PeopleCore.Domain.Payroll`.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/PeopleCore.Application.Tests/ --nologo --filter "FullyQualifiedName~BirWithholdingTax"`
Expected: FAIL — type not found

- [ ] **Step 3: Port the implementation**

```bash
cp "C:/M2NET PROJECTS/payzen/src/PayZen.Shared/Tax/BirWithholdingTax.cs" src/PeopleCore.Domain/Payroll/
```

Change only the namespace to `PeopleCore.Domain.Payroll;`. The six annual brackets and the `ComputeAnnualTax` / `ComputeAnnualTaxDue` split must not change — Form 2316 Item 24 depends on the rounded variant.

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/PeopleCore.Application.Tests/ --nologo --filter "FullyQualifiedName~BirWithholdingTax"`
Expected: `Passed! - Failed: 0, Passed: 6`

- [ ] **Step 5: Commit**

```bash
git add src/PeopleCore.Domain/Payroll/BirWithholdingTax.cs tests/PeopleCore.Application.Tests/Payroll/BirWithholdingTaxTests.cs
git commit -m "feat(payroll): port BIR TRAIN withholding tax table"
```

---

### Task 5: Contribution rates and the SSS schedule

**Files:**
- Create: `src/PeopleCore.Domain/Payroll/ContributionRates.cs`, `src/PeopleCore.Domain/Payroll/SssContributionSchedule.cs`

**Interfaces:**
- Consumes: nothing
- Produces: `PeopleCore.Domain.Payroll.ContributionRates` (record with init-only `PhilHealthRate`, `PhilHealthMinShare`, `PhilHealthMaxShare`, `PagIbigEmployeeRate`, `PagIbigLowEmployeeRate`, `PagIbigLowRateThreshold`, `PagIbigEmployerRate`, `PagIbigMaxFundSalary`, `SSSEmployeeRate?`, `SSSEmployerRate?`), and `SssSchedule` record plus the effective-dated schedule table

- [ ] **Step 1: Extract `ContributionRates` verbatim**

Copy the `ContributionRates` record from `C:/M2NET PROJECTS/payzen/src/PayZen.Server/Services/PayrollComputationService.cs` lines 20-38 into `src/PeopleCore.Domain/Payroll/ContributionRates.cs` under `namespace PeopleCore.Domain.Payroll;`. Keep every default value and every XML doc comment.

- [ ] **Step 2: Extract the SSS schedule verbatim**

Copy the `SssSchedule` record (lines 15-18) and the `Sss2025Rows` table with its full explanatory comment block (from `// ─── SSS Schedule of Contributions` through the end of the rows array) into `src/PeopleCore.Domain/Payroll/SssContributionSchedule.cs` under `namespace PeopleCore.Domain.Payroll;`, as a `public static class SssContributionSchedule`. Expose the schedules as a public read-only collection ordered by `EffectiveFrom` descending, plus:

```csharp
    /// <summary>The schedule in force for <paramref name="asOf"/>, never simply the newest.</summary>
    public static SssSchedule ForPeriod(DateOnly asOf) =>
        Schedules.First(s => s.EffectiveFrom <= asOf);
```

Every bracket row must be copied exactly. Do not retype them by hand — copy the block.

- [ ] **Step 3: Build**

Run: `dotnet build src/PeopleCore.Domain/PeopleCore.Domain.csproj --nologo -v q`
Expected: `0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add src/PeopleCore.Domain/Payroll/ContributionRates.cs src/PeopleCore.Domain/Payroll/SssContributionSchedule.cs
git commit -m "feat(payroll): port contribution rates and the effective-dated SSS schedule"
```

---

### Task 6: Payroll entities

**Files:**
- Create: `src/PeopleCore.Domain/Entities/Payroll/EmployeeCompensation.cs`, `EmployeeAllowance.cs`, `EmployeeLoan.cs`, `PayrollRun.cs`, `PayrollRunEmployee.cs`, `PayrollLoanDeduction.cs`, `PayrollSettings.cs`
- Modify: `src/PeopleCore.Domain/Entities/Organization/Company.cs`

**Interfaces:**
- Consumes: `PeopleCore.Domain.Enums.*` (Task 2), `AuditableEntity`
- Produces: all seven payroll entity types, and `Company` with statutory identity fields

- [ ] **Step 1: Create `EmployeeCompensation`**

```csharp
using PeopleCore.Domain.Enums;

namespace PeopleCore.Domain.Entities.Payroll;

/// <summary>
/// An employee's pay basis. Deliberately separate from Employee: PeopleCore serves employee
/// data to the Manager and Employee roles through ESS, and compensation must never travel in
/// an HR DTO.
/// </summary>
public class EmployeeCompensation : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public Employees.Employee Employee { get; set; } = null!;

    public decimal BasicSalary { get; set; }
    public PayFrequency PayFrequency { get; set; } = PayFrequency.SemiMonthly;
    public string TaxCode { get; set; } = "ME";
    public int Dependents { get; set; }
}
```

- [ ] **Step 2: Create `EmployeeAllowance` and `EmployeeLoan`**

Both stay keyed by `EmployeeId`, not `EmployeeCompensationId` — see the design spec. Port from `C:/M2NET PROJECTS/payzen/src/PayZen.Server/Models/Employee.cs`, changing the base to `AuditableEntity` (which supplies `Id`), dropping the `Employee` navigation property's PayZen type, and using `LoanType` for `EmployeeLoan.LoanType` instead of `string`:

```csharp
using PeopleCore.Domain.Enums;

namespace PeopleCore.Domain.Entities.Payroll;

public class EmployeeAllowance : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public AllowanceType Type { get; set; }
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public bool IsTaxable { get; set; } = false;
}

public class EmployeeLoan : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public LoanType LoanType { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal MonthlyDeduction { get; set; }
    public decimal RemainingBalance { get; set; }
    public DateOnly StartDate { get; set; }
    public bool IsActive { get; set; } = true;
}
```

Note two deliberate changes from PayZen: `LoanType` becomes the enum (PayZen used a free-text string with the enum unused), and `StartDate` becomes `DateOnly` to match PeopleCore's convention for dates without times.

- [ ] **Step 3: Port `PayrollRun`, `PayrollRunEmployee`, `PayrollLoanDeduction`**

Copy from `C:/M2NET PROJECTS/payzen/src/PayZen.Server/Models/PayrollRun.cs` into three files under `namespace PeopleCore.Domain.Entities.Payroll;`. Apply these changes and no others:

- `PayrollRun` and `PayrollRunEmployee` and `PayrollLoanDeduction` derive from `AuditableEntity`; delete their own `Id`, `CreatedAt`, `UpdatedAt` members.
- `PayrollRunEmployee.Employee` is typed `Employees.Employee` (the person), not a payroll type.
- `PeriodStart`, `PeriodEnd`, `PayDate` become `DateOnly`.
- Keep `AttendancePeriodId` as `Guid?`. Phase 1 always writes null.

**Preserve verbatim** the computed properties (`GrossPay`, `TotalDeductions`, `NetPay`, `TotalEmployerCost`, `PeriodLabel`, `EmployeeCount`, `TotalGrossPay`, `TotalNetPay`) and every XML doc comment. Two comments are load-bearing and must survive: that `AbsenceDeduction` and `TardinessDeduction` are already netted out of `RegularPay` and must not be added to `TotalDeductions` again, and that `DailyRate`/`DailyRateFactor` are stored so a payslip cannot drift.

- [ ] **Step 4: Create `PayrollSettings`**

```csharp
namespace PeopleCore.Domain.Entities.Payroll;

/// <summary>
/// Per-company statutory rate configuration. Company identity lives on Company; only rates
/// live here. Null SSS rates mean "use the official schedule".
/// </summary>
public class PayrollSettings : AuditableEntity
{
    public Guid CompanyId { get; set; }
    public Organization.Company Company { get; set; } = null!;

    public decimal PhilHealthRate { get; set; } = 0.05m;
    public decimal PhilHealthMinShare { get; set; } = 250m;
    public decimal PhilHealthMaxShare { get; set; } = 2_500m;

    public decimal PagIbigEmployeeRate { get; set; } = 0.02m;
    public decimal PagIbigLowEmployeeRate { get; set; } = 0.01m;
    public decimal PagIbigLowRateThreshold { get; set; } = 1_500m;
    public decimal PagIbigEmployerRate { get; set; } = 0.02m;
    public decimal PagIbigMaxFundSalary { get; set; } = 10_000m;

    /// <summary>
    /// DOLE equivalent-monthly-rate factor for deriving the applicable daily rate
    /// (monthly x 12 / factor). 365 covers employees paid on unworked rest days, special days
    /// and regular holidays; 313 and 261 suit six- and five-day schedules.
    /// </summary>
    public decimal DailyRateFactor { get; set; } = 365m;

    public decimal? SSSEmployeeRate { get; set; }
    public decimal? SSSEmployerRate { get; set; }
}
```

- [ ] **Step 5: Add identity fields to `Company`**

Add these properties to the existing `Company` entity, leaving its current members alone:

```csharp
    public string TIN { get; set; } = "";
    public string SSSNumber { get; set; } = "";
    public string PhilHealthNumber { get; set; } = "";
    public string PagIbigNumber { get; set; } = "";
    public string Address { get; set; } = "";
    public string City { get; set; } = "";
    public string ContactNumber { get; set; } = "";
    public string Email { get; set; } = "";
    public byte[]? Logo { get; set; }
```

- [ ] **Step 6: Build**

Run: `dotnet build src/PeopleCore.Domain/PeopleCore.Domain.csproj --nologo -v q`
Expected: `0 Error(s)`

- [ ] **Step 7: Commit**

```bash
git add src/PeopleCore.Domain/Entities/Payroll src/PeopleCore.Domain/Entities/Organization/Company.cs
git commit -m "feat(payroll): add payroll entities and company statutory identity"
```

---

### Task 7: Payroll computation service

**Files:**
- Create: `src/PeopleCore.Application/Payroll/DTOs/PayrollAttendanceInput.cs`, `src/PeopleCore.Application/Payroll/Services/PayrollComputationService.cs`
- Test: `tests/PeopleCore.Application.Tests/Payroll/PayrollComputationServiceTests.cs`

**Interfaces:**
- Consumes: `DolePremiumRates`, `BirWithholdingTax`, `ContributionRates`, `SssContributionSchedule` (Tasks 3-5); `EmployeeCompensation`, `PayrollRun`, `PayrollRunEmployee` (Task 6)
- Produces: `PayrollComputationService` with
  - `(decimal Employee, decimal Employer) ComputeSSS(decimal monthlySalary, ContributionRates? rates = null, DateOnly? asOf = null)`
  - `(decimal Employee, decimal Employer) ComputePhilHealth(decimal monthlySalary, ContributionRates? rates = null)`
  - `(decimal Employee, decimal Employer) ComputePagIbig(decimal monthlySalary, ContributionRates? rates = null)`
  - `decimal ComputeWithholdingTax(decimal semiMonthlyTaxableIncome)`
  - `PayrollRunEmployee Compute(EmployeeCompensation compensation, PayrollRun run, decimal daysWorked = 0, decimal overtimeHours = 0, decimal holidayDays = 0, bool includeThirteenthMonth = false, ContributionRates? rates = null, PayrollAttendanceInput? attendance = null, decimal? dailyRateFactor = null)`
- Produces: `PayrollAttendanceInput` — the Phase 2 seam

- [ ] **Step 1: Create the attendance input record**

PayZen's `Compute` takes its own `AttendanceRecord?`, which is not being ported. This record carries exactly the fields the computation reads, so the computation body stays untouched and Phase 2 has one place to fill.

```csharp
namespace PeopleCore.Application.Payroll.DTOs;

/// <summary>
/// Attendance totals for one employee over one payroll period. Phase 1 leaves this null and
/// treats the employee as fully present; Phase 2 populates it from PeopleCore's punches,
/// approved leave and approved overtime.
/// </summary>
public record PayrollAttendanceInput
{
    public decimal LateMinutes { get; init; }
    public decimal AbsenceDays { get; init; }
    public decimal OvertimeHours { get; init; }
    public decimal UndertimeMinutes { get; init; }
    public decimal HolidayRegularDays { get; init; }
    public decimal HolidaySpecialDays { get; init; }
    public decimal NightDiffHours { get; init; }
    public decimal RestDayOTHours { get; init; }
}
```

- [ ] **Step 2: Port the tests first**

```bash
cp "C:/M2NET PROJECTS/payzen/tests/PayZen.Server.Tests/Services/PayrollComputationServiceTests.cs" tests/PeopleCore.Application.Tests/Payroll/
```

Edit the namespace to `PeopleCore.Application.Tests.Payroll` and the usings. In the arrange sections, replace construction of PayZen's `Employee { BasicSalary = ..., PayFrequency = ... }` with `new EmployeeCompensation { BasicSalary = ..., PayFrequency = ... }`, and any `AttendanceRecord` with `PayrollAttendanceInput`. **Change no assertion and no expected value** — including `result.DailyRate.Should().Be(723.29m)`.

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/PeopleCore.Application.Tests/ --nologo --filter "FullyQualifiedName~PayrollComputationService"`
Expected: FAIL — `PayrollComputationService` not found

- [ ] **Step 4: Port the implementation**

Copy `C:/M2NET PROJECTS/payzen/src/PayZen.Server/Services/PayrollComputationService.cs` to `src/PeopleCore.Application/Payroll/Services/PayrollComputationService.cs`, then:

- Set `namespace PeopleCore.Application.Payroll.Services;` and add `using PeopleCore.Domain.Payroll;`, `using PeopleCore.Domain.Entities.Payroll;`, `using PeopleCore.Domain.Enums;`, `using PeopleCore.Application.Payroll.DTOs;`.
- **Remove** the `SssSchedule` record, `ContributionRates` record and `Sss2025Rows` table — they now live in Domain (Task 5). Reference `SssContributionSchedule.ForPeriod(...)` instead of the local table.
- Change `Compute`'s first parameter from `Employee employee` to `EmployeeCompensation compensation`, and rename `employee.BasicSalary` / `employee.PayFrequency` to `compensation.BasicSalary` / `compensation.PayFrequency` throughout.
- Change the `attendance` parameter's type from `AttendanceRecord?` to `PayrollAttendanceInput?`. Every field it reads (`AbsenceDays`, `LateMinutes`, `UndertimeMinutes`, `OvertimeHours`, `RestDayOTHours`, `NightDiffHours`, `HolidayRegularDays`, `HolidaySpecialDays`) exists on the new record with the same name and type, so the body needs no other edit.
- Set `EmployeeId = compensation.EmployeeId` on the returned `PayrollRunEmployee`.
- Keep `DefaultDailyRateFactor = 365m` and every rounding call exactly as written. `Math.Round(compensation.BasicSalary * 12m / factor, 2)` must not become anything else.

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test tests/PeopleCore.Application.Tests/ --nologo --filter "FullyQualifiedName~PayrollComputationService"`
Expected: `Passed! - Failed: 0, Passed: 24`

- [ ] **Step 6: Commit**

```bash
git add src/PeopleCore.Application/Payroll tests/PeopleCore.Application.Tests/Payroll/PayrollComputationServiceTests.cs
git commit -m "feat(payroll): port the payroll computation service"
```

---

### Task 8: Payroll line builder and DTOs

**Files:**
- Create: `src/PeopleCore.Application/Payroll/DTOs/PayrollLineDtos.cs`, `src/PeopleCore.Application/Payroll/Services/PayrollLineBuilder.cs`
- Test: `tests/PeopleCore.Application.Tests/Payroll/PayrollLineBuilderTests.cs`

**Interfaces:**
- Consumes: `PayrollRunEmployee` (Task 6)
- Produces: `PayrollEarningLineDto(string Label, decimal Amount, bool IsTaxable = true)`, `PayrollDeductionLineDto(string Label, decimal Amount, bool IsEmployer = false)`, and `PayrollLineBuilder.Earnings(PayrollRunEmployee)` / `.Deductions(PayrollRunEmployee)`

- [ ] **Step 1: Create the line DTOs**

```csharp
namespace PeopleCore.Application.Payroll.DTOs;

public record PayrollEarningLineDto(string Label, decimal Amount, bool IsTaxable = true);
public record PayrollDeductionLineDto(string Label, decimal Amount, bool IsEmployer = false);
```

- [ ] **Step 2: Port the tests first**

```bash
cp "C:/M2NET PROJECTS/payzen/tests/PayZen.Server.Tests/Services/PayrollLineBuilderTests.cs" tests/PeopleCore.Application.Tests/Payroll/
```

Edit namespace and usings only.

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/PeopleCore.Application.Tests/ --nologo --filter "FullyQualifiedName~PayrollLineBuilder"`
Expected: FAIL — type not found

- [ ] **Step 4: Port the implementation**

Copy `C:/M2NET PROJECTS/payzen/src/PayZen.Server/Services/PayrollLineBuilder.cs` to `src/PeopleCore.Application/Payroll/Services/PayrollLineBuilder.cs`, changing only the namespace and usings. Preserve the class comment and the `basicForPeriod = e.RegularPay + e.AbsenceDeduction + e.TardinessDeduction` reconstruction — the payslip prints `GrossPay` as the column total, so these lines must foot to it.

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test tests/PeopleCore.Application.Tests/ --nologo --filter "FullyQualifiedName~PayrollLineBuilder"`
Expected: `Passed! - Failed: 0, Passed: 6`

- [ ] **Step 6: Commit**

```bash
git add src/PeopleCore.Application/Payroll tests/PeopleCore.Application.Tests/Payroll/PayrollLineBuilderTests.cs
git commit -m "feat(payroll): port the payroll line builder"
```

---

### Task 9: EF configuration and migration

**Files:**
- Create: `src/PeopleCore.Infrastructure/Persistence/Configurations/Payroll/` — one configuration class per entity
- Modify: `src/PeopleCore.Infrastructure/Persistence/AppDbContext.cs`

**Interfaces:**
- Consumes: all Task 6 entities
- Produces: `AppDbContext.EmployeeCompensations`, `.EmployeeAllowances`, `.EmployeeLoans`, `.PayrollRuns`, `.PayrollRunEmployees`, `.PayrollLoanDeductions`, `.PayrollSettings`

- [ ] **Step 1: Add the DbSets**

Add to `AppDbContext`, following the existing grouping-comment style:

```csharp
    // Payroll
    public DbSet<EmployeeCompensation> EmployeeCompensations => Set<EmployeeCompensation>();
    public DbSet<EmployeeAllowance> EmployeeAllowances => Set<EmployeeAllowance>();
    public DbSet<EmployeeLoan> EmployeeLoans => Set<EmployeeLoan>();
    public DbSet<PayrollRun> PayrollRuns => Set<PayrollRun>();
    public DbSet<PayrollRunEmployee> PayrollRunEmployees => Set<PayrollRunEmployee>();
    public DbSet<PayrollLoanDeduction> PayrollLoanDeductions => Set<PayrollLoanDeduction>();
    public DbSet<PayrollSettings> PayrollSettings => Set<PayrollSettings>();
```

- [ ] **Step 2: Write the configurations**

One `IEntityTypeConfiguration<T>` per entity in `Configurations/Payroll/`, matching the existing configuration style in `Configurations/Leave/`. `EmployeeCompensationConfiguration.cs` in full, as the pattern for the rest:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Payroll;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Payroll;

public class EmployeeCompensationConfiguration : IEntityTypeConfiguration<EmployeeCompensation>
{
    public void Configure(EntityTypeBuilder<EmployeeCompensation> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.BasicSalary).HasColumnType("numeric(18,2)");
        builder.Property(x => x.TaxCode).HasMaxLength(8);

        // One compensation row per employee.
        builder.HasIndex(x => x.EmployeeId).IsUnique();

        builder.HasOne(x => x.Employee)
               .WithOne()
               .HasForeignKey<EmployeeCompensation>(x => x.EmployeeId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
```

Carry over from PayZen's `AppDbContext.OnModelCreating` for the remaining entities:

- Every `numeric(18,2)` money column, `numeric(8,4)` rate column, `numeric(6,2)` day/hour column, and `DailyRateFactor` as `numeric(8,2)`.
- `Ignore` the computed properties: `PayrollRun.PeriodLabel`, `.EmployeeCount`, `.TotalGrossPay`, `.TotalDeductions`, `.TotalNetPay`; `PayrollRunEmployee.GrossPay`, `.TotalDeductions`, `.NetPay`, `.TotalEmployerCost`.
- `PayrollRun.RunNumber` unique index.
- `EmployeeCompensation.EmployeeId` **unique** index (1:1 with Employee).
- `PayrollLoanDeduction.LoanType` max length 64 and an index on `EmployeeLoanId`.
- Cascade delete from `PayrollRun` to `PayrollRunEmployee` and from `PayrollRunEmployee` to `LoanDeductionLines`; `Restrict` from `Employee` to `PayrollRunEmployee` so an employee with payroll history cannot be deleted.

- [ ] **Step 3: Create the migration**

Run:

```bash
dotnet ef migrations add AddPayrollDomain --project src/PeopleCore.Infrastructure --startup-project src/PeopleCore.API
```

Expected: a new migration under `src/PeopleCore.Infrastructure/Persistence/Migrations/`.

- [ ] **Step 4: Inspect the generated migration**

Open the generated `*_AddPayrollDomain.cs` and confirm it creates seven tables in snake_case, adds nine columns to `companies`, and creates a unique index on `employee_compensations.employee_id`. Confirm it does **not** drop or alter any existing HR table.

- [ ] **Step 5: Apply and verify**

Run:

```bash
dotnet ef database update --project src/PeopleCore.Infrastructure --startup-project src/PeopleCore.API
docker exec m2net-postgres psql -U postgres -d peoplecore -c "\dt payroll*"
```

Expected: `payroll_runs`, `payroll_run_employees`, `payroll_loan_deductions`, `payroll_settings` listed.

- [ ] **Step 6: Commit**

```bash
git add src/PeopleCore.Infrastructure
git commit -m "feat(payroll): add EF configuration and migration for the payroll domain"
```

---

### Task 10: Repositories and payroll run service

**Files:**
- Create: `src/PeopleCore.Application/Payroll/Interfaces/IPayrollRunRepository.cs`, `IEmployeeCompensationRepository.cs`, `IPayrollSettingsRepository.cs`, `IPayrollRunService.cs`, `IEmployeeCompensationService.cs`, `IPayrollSettingsService.cs`; `src/PeopleCore.Application/Payroll/Services/PayrollRunService.cs`, `EmployeeCompensationService.cs`, `PayrollSettingsService.cs`; matching repositories in `src/PeopleCore.Infrastructure/Persistence/Repositories/`
- Test: `tests/PeopleCore.Application.Tests/Payroll/PayrollRunServiceTests.cs`

**Interfaces:**
- Consumes: `PayrollComputationService` (Task 7), entities (Task 6)
- Produces:
  - `IPayrollRunService`: `Task<PayrollRunDto> CreateAsync(CreatePayrollRunRequest, CancellationToken)`, `Task ComputeAsync(Guid runId, CancellationToken)`, `Task MarkPaidAsync(Guid runId, CancellationToken)`, `Task<PayrollRunDto?> GetAsync(Guid runId, CancellationToken)`
  - `IEmployeeCompensationService`: `Task<EmployeeCompensationDto?> GetByEmployeeAsync(Guid employeeId, CancellationToken)`, `Task<EmployeeCompensationDto> UpsertAsync(Guid employeeId, UpsertCompensationRequest, CancellationToken)`
  - `IPayrollSettingsService`: `Task<PayrollSettingsDto> GetAsync(Guid companyId, CancellationToken)`, `Task UpdateAsync(Guid companyId, PayrollSettingsDto, CancellationToken)`

- [ ] **Step 1: Write the failing tests**

Port the behaviour covered by PayZen's `PayrollRunsControllerMarkPaidTests` (4) and `PayrollRunsControllerRecomputeTests` (6) as service-level tests with mocked repositories, following the Moq + FluentAssertions style already used in `tests/PeopleCore.Application.Tests/Leave/LeaveRequestServiceTests.cs`. The two load-bearing behaviours, in full:

```csharp
[Fact]
public async Task MarkPaidAsync_RetiresLoanBalanceFromTheDeductionLine()
{
    var loan = new EmployeeLoan
    {
        EmployeeId = Guid.NewGuid(),
        LoanType = LoanType.SSSLoan,
        TotalAmount = 10_000m,
        MonthlyDeduction = 2_000m,   // deliberately different from the line below
        RemainingBalance = 5_000m,
        IsActive = true
    };

    var entry = new PayrollRunEmployee { EmployeeId = loan.EmployeeId, LoanDeductions = 1_200m };
    entry.LoanDeductionLines.Add(new PayrollLoanDeduction
    {
        EmployeeLoanId = loan.Id,
        LoanType = loan.LoanType.ToString(),
        Amount = 1_200m
    });

    var run = new PayrollRun { RunNumber = "PR-0001", Status = PayrollRunStatus.Approved };
    run.Employees.Add(entry);

    _runRepo.Setup(r => r.GetWithEntriesAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
    _loanRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync([loan]);

    await _sut.MarkPaidAsync(run.Id, CancellationToken.None);

    // 5,000 - 1,200 taken from the line, not 5,000 - 2,000 re-derived from the schedule.
    loan.RemainingBalance.Should().Be(3_800m);
    run.Status.Should().Be(PayrollRunStatus.Paid);
}

[Fact]
public async Task ComputeAsync_WhenRunIsAlreadyPaid_ThrowsDomainException()
{
    var run = new PayrollRun { RunNumber = "PR-0002", Status = PayrollRunStatus.Paid };
    _runRepo.Setup(r => r.GetWithEntriesAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);

    var act = () => _sut.ComputeAsync(run.Id, CancellationToken.None);

    // Recomputing a paid run would silently change what an employee was already paid.
    await act.Should().ThrowAsync<DomainException>();
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/PeopleCore.Application.Tests/ --nologo --filter "FullyQualifiedName~PayrollRunService"`
Expected: FAIL — `PayrollRunService` not found

- [ ] **Step 3: Implement the repositories and service**

Follow the existing repository pattern in `src/PeopleCore.Infrastructure/Persistence/Repositories/LeaveRequestRepository.cs`. `MarkPaidAsync` must retire loan balances from `LoanDeductionLines`, never from `EmployeeLoan.MonthlyDeduction`. `ComputeAsync` must throw `DomainException` when `Status == PayrollRunStatus.Paid`.

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/PeopleCore.Application.Tests/ --nologo --filter "FullyQualifiedName~PayrollRunService"`
Expected: all pass

- [ ] **Step 5: Commit**

```bash
git add src/PeopleCore.Application/Payroll src/PeopleCore.Infrastructure/Persistence/Repositories tests/PeopleCore.Application.Tests/Payroll
git commit -m "feat(payroll): add payroll repositories and run service"
```

---

### Task 11: API controllers and DI registration

**Files:**
- Create: `src/PeopleCore.API/Controllers/Payroll/PayrollRunsController.cs`, `PayrollSettingsController.cs`, `EmployeeCompensationController.cs`
- Modify: `src/PeopleCore.API/Extensions/ServiceExtensions.cs`

**Interfaces:**
- Consumes: `IPayrollRunService`, `IPayrollSettingsService`, `IEmployeeCompensationService` (Task 10)
- Produces: `/api/payroll-runs`, `/api/payroll-settings`, `/api/employee-compensation`

- [ ] **Step 1: Write the controllers**

Follow the shape of `src/PeopleCore.API/Controllers/Leave/LeaveController.cs`. Every action carries:

```csharp
[Authorize(Roles = "Admin,HRManager,PayrollService")]
```

Set it at class level on all three controllers. `EmployeeCompensationController` must not expose any endpoint that returns compensation joined onto an employee list.

- [ ] **Step 2: Register the services**

In `ServiceExtensions.AddInfrastructure`, following the existing grouping-comment style:

```csharp
        // Payroll
        services.AddScoped<IPayrollRunRepository, PayrollRunRepository>();
        services.AddScoped<IEmployeeCompensationRepository, EmployeeCompensationRepository>();
        services.AddScoped<IPayrollSettingsRepository, PayrollSettingsRepository>();
        services.AddScoped<PayrollComputationService>();
        services.AddScoped<IPayrollRunService, PayrollRunService>();
        services.AddScoped<IEmployeeCompensationService, EmployeeCompensationService>();
        services.AddScoped<IPayrollSettingsService, PayrollSettingsService>();
```

- [ ] **Step 3: Verify the app starts and payroll endpoints are protected**

Run:

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/PeopleCore.API --urls http://localhost:5188 &
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5188/api/payroll-runs
```

Expected: `401`

- [ ] **Step 4: Write the authorization test**

Compensation is the tightest surface in the application, so assert the refusal rather than trusting the attribute. Add `tests/PeopleCore.Application.Tests/Payroll/CompensationAuthorizationTests.cs`:

```csharp
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using PeopleCore.API.Controllers.Payroll;

namespace PeopleCore.Application.Tests.Payroll;

public class CompensationAuthorizationTests
{
    public static TheoryData<Type> PayrollControllers => new()
    {
        typeof(EmployeeCompensationController),
        typeof(PayrollRunsController),
        typeof(PayrollSettingsController)
    };

    [Theory]
    [MemberData(nameof(PayrollControllers))]
    public void PayrollController_AdmitsOnlyPayrollRoles(Type controller)
    {
        var attribute = controller.GetCustomAttribute<AuthorizeAttribute>();

        attribute.Should().NotBeNull("every payroll controller must be role-restricted");

        var roles = attribute!.Roles!.Split(',', StringSplitOptions.TrimEntries);
        roles.Should().BeEquivalentTo(["Admin", "HRManager", "PayrollService"]);
        roles.Should().NotContain("Manager");
        roles.Should().NotContain("Employee");
    }
}
```

This requires the test project to reference the API project. Add to `tests/PeopleCore.Application.Tests/PeopleCore.Application.Tests.csproj`:

```xml
    <ProjectReference Include="..\..\src\PeopleCore.API\PeopleCore.API.csproj" />
```

- [ ] **Step 5: Run the authorization test**

Run: `dotnet test tests/PeopleCore.Application.Tests/ --nologo --filter "FullyQualifiedName~CompensationAuthorization"`
Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 6: Verify no compensation field leaks into HR DTOs**

Run: `grep -rn "BasicSalary\|PayFrequency\|TaxCode" src/PeopleCore.Application/Employees/`
Expected: no output.

- [ ] **Step 7: Commit**

```bash
git add src/PeopleCore.API
git commit -m "feat(payroll): add payroll API endpoints"
```

---

### Task 12: Seed payroll defaults

**Files:**
- Modify: `src/PeopleCore.API/Program.cs`

**Interfaces:**
- Consumes: `PayrollSettings` (Task 6)
- Produces: one `PayrollSettings` row per company on first run

- [ ] **Step 1: Extend the existing seed block**

In the seeding scope in `Program.cs`, after the company seed:

```csharp
    // Every company needs statutory rates before payroll can run; defaults come from the
    // entity so there is one definition of the current statutory position.
    var companiesWithoutSettings = await dbContext.Companies
        .Where(c => !dbContext.PayrollSettings.Any(s => s.CompanyId == c.Id))
        .ToListAsync();

    foreach (var company in companiesWithoutSettings)
        dbContext.PayrollSettings.Add(new PayrollSettings { CompanyId = company.Id });

    if (companiesWithoutSettings.Count > 0)
        await dbContext.SaveChangesAsync();
```

- [ ] **Step 2: Verify**

Run:

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/PeopleCore.API --urls http://localhost:5188 &
docker exec m2net-postgres psql -U postgres -d peoplecore -c "SELECT company_id, daily_rate_factor FROM payroll_settings;"
```

Expected: one row, `daily_rate_factor` = `365.00`

- [ ] **Step 3: Run the full suite**

Run: `dotnet test PeopleCore.slnx --nologo -v q`
Expected: `Failed: 0`, with at least 43 more tests than the 48 at the start of this plan.

- [ ] **Step 4: Commit**

```bash
git add src/PeopleCore.API/Program.cs
git commit -m "feat(payroll): seed default payroll settings per company"
```

---

## Phase exit criteria

1. Ported statutory tests pass with unchanged expected values: `DolePremiumRates` 7, `BirWithholdingTax` 1, `PayrollComputationService` 24, `PayrollLineBuilder` 6. Test-method counts expand under `[Theory]`/`[InlineData]`; assert the values, not a total.
2. `dotnet test PeopleCore.slnx` is green with 0 failures.
3. A payroll run can be created, computed and marked paid through the API, with loan balances retiring from `LoanDeductionLines`.
4. `grep -rn "BasicSalary" src/PeopleCore.Application/Employees/` returns nothing, and `Manager` / `Employee` receive 403 from compensation endpoints.
5. `M2NET.Core` builds from inside the repository with no external path dependency.
