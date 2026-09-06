# Payroll UI — Phase 3 Design

**Date:** 2026-09-06
**Status:** Approved for planning
**Phase:** 3 of 4

## Context

Phases 1 and 2 put a Philippine payroll engine into PeopleCore and taught it to derive its own
attendance inputs. All of it is reachable only over HTTP: creating a run, computing it and marking it
paid currently require curl, and setting an employee's salary requires SQL.

**Goal:** make payroll operable from the browser. Three screens, in the app's existing Blazor
patterns.

## Scope

### In

- `GET /api/payroll-runs` — the list endpoint that does not yet exist, plus a summary DTO for it.
- `/payroll-runs` — runs list with a create dialog.
- `/payroll-runs/{id}` — the payroll register, with Compute and Mark Paid.
- `/employees/{id}/compensation` — salary, pay frequency, tax code, dependents.
- `ApiClient` methods and client-side DTOs for the above.
- A role-gated Payroll group in the navigation.

### Out

- **Payroll settings UI.** The values are set once, already seeded with correct statutory defaults,
  and the repository guard permits only one settings row. A form would rarely be opened.
- **ESS payslip page.** `PayrollLineBuilder` already produces the earning and deduction lines a
  payslip renders, and Phase 4 builds the PDF from the same data. Building the screen now means
  designing the payslip layout twice.
- **Editing a run's entries by hand.** The per-employee override fields exist on
  `PayrollRunEmployeeInput`, but routine correction belongs with the payslip work in Phase 4, once
  there is a rendered line-by-line view to correct against.

## The list endpoint

`PayrollRunDto` carries its full `Employees` collection. Returning it from a list would ship every
line of every run — for twelve runs of 200 employees, 2,400 nested records to render twelve table
rows.

So the list gets its own projection:

```csharp
public record PayrollRunSummaryDto(
    Guid Id,
    string RunNumber,
    string PeriodLabel,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly PayDate,
    PayFrequency Frequency,
    PayrollRunStatus Status,
    int EmployeeCount,
    decimal TotalGrossPay,
    decimal TotalNetPay,
    int EmployeesMissingAttendance,
    DateTime CreatedAt);
```

`GET /api/payroll-runs?page=1&pageSize=20` returns `PagedResult<PayrollRunSummaryDto>`, matching the
convention every other list endpoint in this codebase uses. Ordered by `CreatedAt` descending —
newest first is what an operator wants.

The totals on the summary are computed properties on `PayrollRun` (`EmployeeCount`, `TotalGrossPay`,
`TotalNetPay`), which sum the `Employees` collection. The repository query must therefore still
include the entries to compute them, then project them away. That is acceptable for a paged list
and avoids duplicating the totalling logic in SQL, where it would drift from the entity's
definition.

## The screens

### `/payroll-runs`

A table of runs — number, period, pay date, status badge, employee count, total net — with a
**Create Run** dialog.

The dialog asks for period start, period end, pay date, frequency, and which employees to include,
defaulting to all active employees with a multi-select to trim.

**"All active employees" means all of them, not the first page.** `GET /api/employees` is paged with
a default `pageSize` of 20. The dialog must page through to exhaustion, or request a page size large
enough to cover the workforce and assert it got everyone. Taking page one silently produces a
payroll run missing every employee after the twentieth — a payday nobody gets paid on, with no
error.

**It does not ask for overtime, holidays or days worked.** Those are derived by the attendance
bridge from punches, approved leave, approved overtime, the holiday calendar and the shift schedule.
`PayrollRunEmployeeInput`'s override fields stay null, which is what makes the derivation happen.
The overrides exist for correction, not for routine entry, and surfacing them in the create dialog
would invite an operator to type a zero that silently beats a derived figure.

### `/payroll-runs/{id}`

The register: one row per employee with regular pay, overtime, holiday, night differential, gross,
each statutory deduction, withholding tax, and net. Run totals in a header.

Two actions, gated on status:

| Status | Compute | Mark Paid |
|---|---|---|
| `Draft`, `Processing`, `ForApproval` | offered | offered |
| `Approved` | **not offered** | offered |
| `Paid` | **not offered** | **not offered** |

`Compute` is withheld at `Approved` because `PayrollRunService.ComputeAsync` throws a
`DomainException` for both `Approved` and `Paid` — recomputing over signed-off figures would change
them. The UI matches the service rather than offering an action that will fail.

**`EmployeesMissingAttendance` is displayed prominently**, not buried. Phase 2 records it precisely
so an operator can see who had no resolvable shift schedule — and therefore had no absences derived
— *before* paying them. When it is non-zero the page shows a warning banner naming the count. A log
line does not reach the person clicking Mark Paid.

### `/employees/{id}/compensation`

Basic salary, pay frequency, tax code, dependents. Reached from a **Compensation** button on the
existing Employees page, so it needs no new endpoint — `GET` and `PUT /api/employee-compensation/{employeeId}`
already exist.

`GET` returns 404 for an employee with no compensation row yet; the page treats that as an empty
form rather than an error, and `PUT` creates the row.

## Authorization

Every page carries `@attribute [Authorize(Roles = "Admin,HRManager,PayrollService")]`, matching the
controllers behind them.

The compensation page is the most sensitive surface in the application. Its nav entry is role-gated
so it is not merely hidden but still routable, and the page attribute is what actually enforces it —
a client-side route guard is a convenience, and the API's `[Authorize]` is the real boundary.

## Testing

**This repository has no Blazor component tests.** Every existing page is untested, and introducing
bUnit would be new infrastructure and its own task. This phase does not add it.

So coverage splits:

- **The new list endpoint gets service-level tests** in the existing Moq + FluentAssertions style,
  like every other endpoint here: paging, ordering by `CreatedAt` descending, and the summary
  projection carrying correct totals.
- **The three pages are verified in a browser** against the local Postgres: create a run, compute
  it, read the register, mark it paid, and edit an employee's compensation. Screenshots record the
  result.

This is honest coverage rather than pretend coverage. The trade is stated plainly: a page regression
will not be caught by CI until component tests exist.

## Acceptance criteria

1. A payroll run can be created, computed, reviewed and marked paid entirely in the browser, with no
   curl and no SQL.
2. An employee's compensation can be set in the browser, and a run computed afterwards reflects it.
3. The runs list returns `PagedResult<PayrollRunSummaryDto>` and does not carry per-employee entries.
4. `Compute` is not offered on an `Approved` or `Paid` run, and `Mark Paid` is not offered on a
   `Paid` one.
5. A run with a non-zero `EmployeesMissingAttendance` shows a visible warning naming the count.
6. `Manager` and `Employee` cannot reach the compensation page.
7. `dotnet build` is clean and the full suite is green.
8. The Phase 1 statutory files and their tests remain untouched.
