# Payroll Input Validation — Design

**Date:** 2026-09-07
**Status:** Approved for planning
**Follows:** the BIR 2316 exact-form phase

## Context

The Application layer has no input validation. Not "thin" validation — none. A survey of
`src/PeopleCore.Application` returns **zero** occurrences of `[Range]`, `[Required]`, `MaxLength`
or FluentValidation across all fourteen request records.

`EmployeeCompensationService.UpsertAsync` is the clearest case: the request's fields are copied
onto the entity and saved, with nothing between. A negative `BasicSalary`, or one with an extra
zero, is persisted, then computed into a payslip, then aggregated into a BIR Form 2316 — a
document an officer of the company signs under penalty of perjury.

`Bir2316ManualInputs` is the same defect on the legal document itself. Seven `decimal` fields
arrive from the request body and are stamped onto the certificate. The 2316 phase was careful
that *derived* figures can never come from the caller; the fields a caller legitimately supplies
were never bounded.

**Goal:** reject impossible and absurd input before it reaches a payslip or a tax certificate.

## Scope

### In

Every caller-supplied number that reaches money or a legal record:

- `UpsertCompensationRequest`
- `CreatePayrollRunRequest` (and its nested `PayrollRunEmployeeInput`)
- `Bir2316ManualInputs`

### Out

The other eleven request records — Attendance, Careers, Leave, Scheduling. They carry leave
reasons, shift names and timestamps: real input, but nothing that lands on a payslip or a signed
form. They deserve the same treatment later; bundling them here would triple the change for a
fraction of the risk.

Also out: removing `TaxCode` and `Dependents`, discussed under *Findings* below. Deleting a
persisted column is a migration and a separate decision.

## Approach

**Validate in the Application services and throw `DomainException`.**

This is the convention the codebase already has. `DomainException` is thrown in 43 places, and
`ExceptionHandlingMiddleware` maps it to a 400 with an `application/problem+json` body. Nothing
new is introduced — no dependency, no second idiom. `PayrollRunService.CreateAsync` already
opens with exactly this shape:

```csharp
if (request.Employees is not { Count: > 0 })
    throw new DomainException("A payroll run must include at least one employee.");
```

Because the check sits in Application rather than at the HTTP boundary, it holds for any caller:
the API today, a hosted service or an import job tomorrow.

### Why not DataAnnotations and ModelState

`[ApiController]` would return a 400 with a field-keyed error dictionary for free, which is a
genuine advantage for a form UI. It was rejected because it only fires at the HTTP boundary,
leaving the Application layer — the layer this work exists to protect — exactly as open as it is
now. `UpsertCompensationRequest` is also a positional record, so every rule would need
`[property: Range(...)]` syntax on the constructor parameters.

### Why not FluentValidation

Composable, collects all failures, and validators test in isolation. It is a new dependency and a
second validation idiom in a codebase with none, to buy behaviour that thirty lines of C# provide
here. If validation later spreads across all fourteen records and grows conditional rules, that is
the point to reconsider — not now.

## Architecture

```
src/PeopleCore.Domain/Payroll/
  PayrollInputLimits.cs           the bounds, as named constants

src/PeopleCore.Application/Payroll/Validation/
  CompensationValidator.cs
  PayrollRunRequestValidator.cs
  Bir2316ManualInputsValidator.cs
```

Each validator is a static class with one `Validate(request)` method, called on the first line of
the service method it guards. They depend on nothing — no repository, no `DbContext`, no service —
so their tests need no fixtures.

**Limits live in `PayrollInputLimits.cs`, a new file.** They belong beside the statutory tables in
`PeopleCore.Domain.Payroll`, but explicitly **not inside `StatutoryCaps.cs`**: that file and its
four siblings are byte-verified against their PayZen originals and must not be modified. These
bounds are also not statutory — they are engineering sanity limits, and mixing them in with
legislated figures would misrepresent both.

### All failures at once

Each validator accumulates problems into a list and throws a single `DomainException` naming every
one, rather than returning at the first. A user correcting a compensation form one field per round
trip is the experience that makes validation resented. `DomainException` carries only a message, so
the message holds the list; a field-keyed structure would mean a new exception type and middleware
changes, which is more than this needs.

## Rules

### `UpsertCompensationRequest`

| Field | Rule | Why |
|---|---|---|
| `BasicSalary` | `0 <= x <= 10,000,000` | A **monthly** figure regardless of `PayFrequency` — `PayrollComputationService` derives both `dailyRate = BasicSalary * 12 / factor` and `basePeriodPay = BasicSalary / periodsPerMonth` from it. Zero is allowed: an unpaid position is possible, a negative salary is not. The ceiling catches the real failure mode, a fat-fingered extra zero. |
| `BasicSalary` | at most 2 decimal places | The column is `numeric(18,2)`. A third decimal is silently rounded on save today, so the stored salary differs from the submitted one with no error anywhere. |
| `PayFrequency` | a defined enum value | JSON binds an out-of-range number to an enum without complaint, and `PayrollComputationService` treats anything that is not `SemiMonthly` as monthly — so an unrecognised value silently halves or doubles a period's pay. |
| `Dependents` | `0 <= x <= 20` | Negative dependents are impossible. |
| `TaxCode` | non-empty, at most 8 characters | Matches `EmployeeCompensationConfiguration`'s `HasMaxLength(8)`, so an over-long value fails as validation rather than as a database error. Deliberately **not** checked against a status-code list — see *Findings*. |

### `CreatePayrollRunRequest`

| Field | Rule | Why |
|---|---|---|
| `PeriodEnd` | `>= PeriodStart` | An inverted period produces a run whose every derived figure is meaningless. |
| `PayDate` | `>= PeriodStart` | Paying before the period opens is incoherent. Paying between start and end is not — advances are real — so the bound stops at `PeriodStart`. |
| period length | `<= 16` days semi-monthly, `<= 31` monthly | A frequency that contradicts its own date range is a data-entry error, and the 2316 attributes income by pay date, so a 300-day "semi-monthly" run corrupts a tax year. |
| `Frequency` | a defined enum value | As above. |
| `Employees` | at least one | Already enforced; moves into the validator so every rule for this request is in one place. |
| `Employees` | no duplicate `EmployeeId` | The same employee twice in one run pays them twice. |
| `DaysWorked` | `0 <= x <= 31` when supplied | Manual override of a bridge-derived figure. |
| `OvertimeHours` | `0 <= x <= 744` when supplied | 31 x 24 — the hours a month physically contains. |
| `HolidayDays` | `0 <= x <= 31` when supplied | As above. |

Nullable overrides are checked **only when supplied**. Their nullability is deliberate — null means
"use what the attendance bridge derived" — and validation must not disturb that.

### `Bir2316ManualInputs`

| Field | Rule | Why |
|---|---|---|
| all 7 decimals | `0 <= x <= 120,000,000`, at most 2 decimal places | Annual figures, so the ceiling is 12x the monthly salary ceiling. Negative money on a tax certificate is indefensible. |
| `Item25B_PrevTaxWithheld` | `<= Item22_PrevTaxableCompensation` | Tax withheld cannot exceed the compensation it was withheld from. The one cross-field rule here, and the only one that catches a *plausible* mistake — two figures that are individually reasonable and jointly impossible. |
| `PrevEmployerTin` | 9 or 12 digits ignoring separators, when supplied | Philippine TINs are 9 digits, or 12 with a branch code. The form prints them into fixed digit cells. |
| `PrevEmployerZipCode` | exactly 4 digits, when supplied | `Bir2316FieldMap` draws this into 4 cells; anything else overflows or under-fills them. |
| `PrevEmployerName`, `PrevEmployerAddress` | at most 200 characters each | 200 matches `CompanyConfiguration`'s `HasMaxLength(200)` on `Company.Name`, so the same kind of value is bounded the same way in both places. Note this is an absurdity bound, **not** overflow protection: `Bir2316Stamper.FitToWidth` already shrinks the font to `MinFontSize` and only then truncates with an ellipsis, so an over-long name degrades visibly rather than silently. The rule exists because a 5,000-character employer name is not a name. |

`StatutoryMinWagePerDay` and `StatutoryMinWagePerMonth` are covered by the all-decimals rule. No
relationship between them is asserted: `IsMinimumWageEarner` is deliberately not exposed, so
neither field currently reaches a rendered certificate, and inventing a day-to-month rule for
fields nothing consumes would be guessing.

## Findings

Two things surfaced while measuring this that are not defects to fix here, but should be recorded.

**`TaxCode` and `Dependents` are written and never read.** A search across `src` finds no consumer
— they are stored, returned in a DTO, and consulted by nothing. This is consistent with the tax
law: TRAIN (RA 10963, effective 2018) abolished personal and additional exemptions, which is
exactly what tax status codes and dependent counts encoded. They are vestigial. This is why the
rule above bounds `TaxCode`'s *length* rather than checking it against `S`/`ME`/`S1`..`ME4`:
validating against that list would enforce an obsolete rule with no consequence, and would imply
to the next reader that the field still drives withholding. Removing both is a reasonable follow-up
and needs a migration.

**`PayrollComputationService` has a dead custom-earnings branch.** `customEarnings` and
`customDeductions` are declared `0m` and never assigned, so `OtherDeductions` can never be
non-zero. Its comment says Phase 2 will fill them from "PeopleCore's own custom attendance fields";
Phase 2 shipped, and no `AttendanceCustomField` type exists anywhere in the repository. Out of
scope here, noted so it is not rediscovered a third time.

## Testing

One test class per validator, in the existing `tests/PeopleCore.Application.Tests/Payroll/`.

- **Every rule gets two tests:** one value that must be rejected, and the boundary value that must
  be accepted. A ceiling of 10,000,000 is only pinned if `10,000,000` passes and `10,000,000.01`
  fails; asserting only the rejection lets the bound drift inward unnoticed.
- **The accumulation behaviour is asserted**, not assumed: a request with three problems produces
  one exception naming all three. This is the requirement most likely to be quietly lost to a
  later refactor that returns early.
- **The 2316 cross-field rule gets its own test**, with two individually-valid figures that are
  jointly impossible.
- **One test per validator proves a fully valid request passes**, guarding against a rule that
  rejects everything.

Validators take no dependencies, so none of these need a mock, a repository or a fixture.

Existing tests must stay green unchanged. The one to watch is any test constructing a
`CreatePayrollRunRequest` or `UpsertCompensationRequest` with placeholder values that the new rules
would reject — those are not failures of the rules, they are fixtures that need real values, and
a rule must not be loosened to accommodate one.

## Risks

**A bound rejects a legitimate figure.** The salary ceiling is the exposed one: ₱10,000,000 per
month is far above any realistic Philippine payroll, but "far above realistic" is a judgement, not
a fact. The mitigation is that the failure is loud and its message states the limit, so it is
diagnosed in seconds and changed in one constant — unlike the silent corruption it replaces.

**Validation is invoked by convention, not enforced.** A new service method can forget to call its
validator. Accepted for three call sites; if this spreads to all fourteen records, a pipeline
behaviour that cannot be skipped becomes the better shape.

## Acceptance criteria

1. `UpsertCompensationRequest`, `CreatePayrollRunRequest` and `Bir2316ManualInputs` are validated
   in the Application layer before any entity is touched.
2. An invalid request throws `DomainException` and surfaces as a 400, via the existing middleware
   with no changes to it.
3. One exception names every problem in a request, asserted by test.
4. Every rule has both a rejecting test and an accepting boundary test.
5. Bounds live in `PayrollInputLimits.cs`; `StatutoryCaps.cs` and the other four Phase 1 statutory
   files are untouched.
6. No new package reference.
7. `dotnet build` is clean and the full suite is green.
