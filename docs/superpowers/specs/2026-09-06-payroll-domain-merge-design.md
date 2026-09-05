# Merging PayZen's Payroll Domain into PeopleCore — Phase 1 Design

**Date:** 2026-09-06
**Status:** Approved for planning
**Phase:** 1 of 4

## Context

PeopleCore (HR) and PayZen (Philippine payroll) are separate .NET 10 applications that already
share `M2NET.Core.Entities.Employee` as a base class. Their extensions of it are complementary
rather than overlapping: PeopleCore adds HR fields, PayZen adds compensation. PeopleCore already
exposes `/api/payroll-export/*` and seeds a `PayrollService` role for an integration that was
designed but never connected.

Neither application holds production data. PayZen's database contains 8 seeded employees
(`EMP-0001` to `EMP-0008`), one payroll run, no attendance records and one user; PeopleCore's is
empty apart from Identity roles. The usual reason to keep two systems loosely coupled — not
disturbing a running system — does not apply.

**Goal:** one HRIS. PayZen's payroll domain moves into the PeopleCore solution, under PeopleCore
naming, in `C:\M2NET PROJECTS\peoplecore`. PayZen is a source, not a destination.

## Scope

### In

- Vendor `M2NET.Core` into the PeopleCore repository so it is self-contained.
- Payroll entities, enums, statutory calculators, computation orchestrator.
- EF configurations, one migration, repositories.
- Payroll API endpoints under PeopleCore's existing Identity roles.
- The ported computation tests, unchanged in their expected values.

### Out (later phases)

| Phase | Covers |
|---|---|
| 2 | Attendance bridge — aggregate PeopleCore punches, approved leave and approved OT into the period totals payroll consumes |
| 3 | Blazor payroll UI |
| 4 | Reports — `PayslipDocument`, `BIR2316Document` (QuestPDF) |

In Phase 1 `DaysWorked`, `OvertimeHours` and `HolidayDays` remain **explicit inputs** to a payroll
run, which is already how PayZen behaves when an employee has no attendance record. Payroll
computes correctly end to end; it is simply not yet fed from PeopleCore's punches. This keeps the
riskiest work (statutory arithmetic) isolated from the second riskiest (attendance aggregation
semantics).

### Not ported at all

- `AttendancePeriod`, `AttendanceRecord`, `AttendanceCustomValue` and the CSV column-mapping
  machinery. PeopleCore already owns attendance, `AttendanceDevice`, `/api/attendance/sync` and
  CSV import. Porting PayZen's would create a second attendance system.
- `User`, `AuthService`, `PermissionDefinitions`, `PermissionService`, `RequirePermissionAttribute`.
  PeopleCore uses ASP.NET Identity; payroll endpoints map onto the existing `Admin`, `HRManager`
  and `PayrollService` roles.
- `Employee.DailyRate => BasicSalary / 22m` and `HourlyRate`. These are display-only, EF-ignored,
  and contradict the DOLE equivalent-monthly-rate factor the computation actually uses — verified
  unused by `PayrollComputationService`, whose tests assert `657.53` as "20,000 x 12 / 365". Where
  a display daily rate is needed it derives from `PayrollSettings.DailyRateFactor`, so there is one
  definition.

## Architecture

```
src/M2NET.Core/                          vendored; Directory.Build.props reference simplifies
src/PeopleCore.Domain/
  Entities/Payroll/                      EmployeeCompensation, EmployeeAllowance, EmployeeLoan,
                                         PayrollRun, PayrollRunEmployee, PayrollLoanDeduction,
                                         PayrollSettings
  Enums/                                 PayFrequency, PayrollRunStatus, AllowanceType, LoanType
  Payroll/                               SssContributionSchedule, PhilHealthRates, PagIbigRates,
                                         BirWithholdingTax, DolePremiumRates
src/PeopleCore.Application/Payroll/
  DTOs/                                  PayrollRunDto, PayrollEarningLineDto, ...
  Interfaces/                            IPayrollRunService, IPayrollSettingsService,
                                         IEmployeeCompensationService, repository interfaces
  Services/                              PayrollComputationService, PayrollRunService,
                                         PayrollLineBuilder
src/PeopleCore.Infrastructure/Persistence/
  Configurations/Payroll/                EF configurations
  Repositories/                          payroll repositories
src/PeopleCore.API/Controllers/Payroll/  PayrollRunsController, PayrollSettingsController,
                                         EmployeeCompensationController
tests/PeopleCore.Application.Tests/Payroll/
```

### Domain versus Application

PayZen keeps statutory tables and orchestration together in one 444-line
`PayrollComputationService`. The port separates them along the dependency boundary that already
exists in the code:

- **Domain** holds the pure statutory rules — SSS contribution schedules, PhilHealth and Pag-IBIG
  rates, BIR TRAIN withholding brackets, DOLE premium rates. These are functions over primitives
  with no infrastructure dependency.
- **Application** holds `PayrollComputationService`, which reaches for employees, loans and
  settings and assembles `PayrollRunEmployee` rows.

This follows PeopleCore's existing convention (Domain holds entities, enums and pure rules;
Application holds services) and makes the statutory tables independently testable.

SSS schedules stay **effective-dated**, as PayZen has them: a run is computed under the schedule in
force for its period, never the newest. Registering a new circular adds an entry; it never edits an
existing one.

## Entity model

### EmployeeCompensation (1:1 with Employee)

PayZen hangs `BasicSalary`, `PayFrequency`, `TaxCode` and `Dependents` directly on `Employee`. That
is safe in PayZen, which has a single admin user. It is not safe in PeopleCore, where ESS exists,
every employee can reach `/api/employees` and `MyProfile`, and `EmployeeListDto` is served to the
`Manager` and `Employee` roles. Compensation on that entity is one careless DTO mapping away from a
salary leak.

`EmployeeCompensation` is therefore a separate entity with its own repository, service and
authorization, and is **never** projected into any HR DTO.

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | from `AuditableEntity` |
| `EmployeeId` | `Guid` | unique index |
| `BasicSalary` | `decimal(18,2)` | |
| `PayFrequency` | `PayFrequency` | Monthly, SemiMonthly |
| `TaxCode` | `string` | default `"ME"` |
| `Dependents` | `int` | |

`EmployeeAllowance` and `EmployeeLoan` port from PayZen unchanged in shape and stay keyed by
`EmployeeId`, not by `EmployeeCompensationId`. They are payroll-module entities behind the same
authorization, but re-keying them would be schema churn beyond the split and would force a
compensation row to exist before a loan could be recorded. Both gain `AuditableEntity` so they
participate in audit stamping.

`PayrollRunEmployee.EmployeeId` likewise continues to reference `Employee`, the person — not
`EmployeeCompensation`. The computation reads compensation separately when it builds the row, and
the resulting figures are snapshotted onto `PayrollRunEmployee`, so a later salary change cannot
alter a historical run.

### PayrollSettings and Company

PayZen's `CompanySettings` mixes company identity (name, TIN, SSS/PhilHealth/Pag-IBIG numbers,
address, contact, logo) with statutory rate configuration. PeopleCore already has
`Organization/Company`, so porting it whole would create two competing company concepts.

- Identity fields fold into the existing `Company` entity, which today carries only `Name`. It
  gains `TIN`, `SSSNumber`, `PhilHealthNumber`, `PagIbigNumber`, `Address`, `City`,
  `ContactNumber`, `Email` and `Logo`.
- Rate configuration becomes `PayrollSettings`, keyed per company: `PhilHealthRate`,
  `PhilHealthMinShare`, `PhilHealthMaxShare`, `PagIbigEmployeeRate`, `PagIbigLowEmployeeRate`,
  `PagIbigLowRateThreshold`, `PagIbigEmployerRate`, `PagIbigMaxFundSalary`, `DailyRateFactor`,
  `SSSEmployeeRate?`, `SSSEmployerRate?`.

Null SSS rates continue to mean "use the official schedule", as in PayZen.

### PayrollRun, PayrollRunEmployee, PayrollLoanDeduction

Port with their shape and their invariants intact. Two of those invariants are load-bearing and
must survive review:

1. `AbsenceDeduction` and `TardinessDeduction` are **already netted out of `RegularPay`**. They are
   recorded for the payslip and must never be added to `TotalDeductions` again.
2. `DailyRate` and `DailyRateFactor` are **stored per run**, so a payslip cannot drift when settings
   change later.

`PayrollRunEmployee` keeps `LoanDeductionLines`: marking a run paid retires balances from those rows
rather than re-deriving the instalment, so what comes off a loan is exactly what came off the
employee's pay.

`AttendancePeriodId` is retained as a nullable column. Phase 1 always writes null; Phase 2 populates
it.

## Data flow

```
EmployeeCompensation + allowances + loans
        +  PayrollSettings (rates, DailyRateFactor)
        +  run inputs (DaysWorked, OvertimeHours, HolidayDays, 13th month flag)
                    |
                    v
       PayrollComputationService  (Application)
         - derives daily rate = BasicSalary x 12 / DailyRateFactor
         - calls Domain statutory calculators
                    |
                    v
       PayrollRunEmployee rows  ->  PayrollRun totals
                    |
                    v
       PayrollLineBuilder -> earning / deduction lines for payslip and register
```

## Authorization

All payroll endpoints require `Admin`, `HRManager` or `PayrollService`. Compensation endpoints are
the tightest surface in the application and get explicit tests asserting that `Manager` and
`Employee` are refused.

## Data migration

None. PayZen's 8 seeded employees and single payroll run are demo data and are not migrated.
PeopleCore's existing seeder gains payroll defaults: one `PayrollSettings` row per company with
statutory defaults.

## Testing

Approximately 43 PayZen tests cover exactly the code whose behaviour must not change:

| Suite | Tests |
|---|---|
| `PayrollComputationServiceTests` | 24 |
| `DolePremiumRatesTests` | 7 |
| `BirWithholdingTaxTests` | 6 |
| `PayrollLineBuilderTests` | 6 |

**These port first, before the code they cover, and their expected values stay byte-identical.** A
moved peso means the port is wrong. This is the acceptance criterion for the phase.

`AttendanceDrivenPayrollTests` (18) depends on PayZen's attendance shape and moves to Phase 2 with
the attendance bridge.

New tests cover what the port introduces: the `EmployeeCompensation` split, `PayrollSettings`
resolution, and the authorization matrix for compensation endpoints.

## Acceptance criteria

1. The ported statutory tests pass with unchanged expected values.
2. `dotnet build` is clean and the full suite is green.
3. A payroll run can be created, computed and marked paid through the API, with loan balances
   retiring from `LoanDeductionLines`.
4. No compensation field appears in any HR DTO; `Manager` and `Employee` are refused on compensation
   endpoints, asserted by test.
5. `M2NET.Core` builds from inside the repository with no external path dependency.
