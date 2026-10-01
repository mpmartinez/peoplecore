# Payroll Opening Balances Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** HR records what each employee was paid in a year before PeopleCore went live, and every "earlier this year" figure adds it in.

**Architecture:** A `PayrollOpeningBalance` record per employee and year, managed through a service, endpoints and a CSV import. A single `PayrollYearToDate` service returns the year's Paid-run sums plus the opening balance; every place that summed the year's Paid runs switches to it. The 2316 adds the balance item by item, so the 1604-C and final pay's tax settle follow.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core (Npgsql, snake_case), Blazor WebAssembly, xUnit, FluentAssertions, Moq, bUnit, Postgres test fixture.

**Spec:** `docs/superpowers/specs/2026-09-30-payroll-opening-balances-design.md` - the record's fields, what adds them in, the warnings, the CSV format and the messages are all there and bind every task.

## Global Constraints

- Everything in the spec's "The record", "What adds them in", "Double-count warning", "Editing" and "CSV import" sections, with the exact messages.
- The 1601-C's monthly columns don't change, but the 13th month it treats as already exempt earlier in the year adds the balance's `ThirteenthMonthPaid + OtherBenefitsPaid`.
- Endpoints use `Permissions.PayrollManage` and are pinned in `PermissionEquivalenceTests`.
- Build/test: `MSBUILDDISABLENODEREUSE=1 dotnet test PeopleCore.slnx -nodeReuse:false -p:UseSharedCompilation=false`. Migrations need `DataProtection__KeyEncryptionKey=design-time-only-not-a-secret-0123456789` and `ASPNETCORE_ENVIRONMENT=Development`.
- Commit messages end with a blank line, then `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

---

### Task 1: Storage and the record's service

**Files:** `src/PeopleCore.Domain/Entities/Payroll/PayrollOpeningBalance.cs`, its EF configuration, `AppDbContext`; `src/PeopleCore.Application/Payroll/OpeningBalances/` (repository interface, service, DTOs); the repository in Infrastructure; `PayrollOpeningBalancesController`; migration `AddPayrollOpeningBalances`.

**Produces:** `IPayrollOpeningBalanceRepository` with `GetAsync(employeeId, year)`, `GetForEmployeesAsync(employeeIds, year)` and `GetForYearAsync(year)` (with `Employee`); `IPayrollOpeningBalanceService` with list, get, create, update and delete; `OpeningBalanceDto` (every field, the employee's name and number, and `IReadOnlyList<string> Warnings`).

- [ ] Failing tests: the entity round-trips on Postgres and the unique (employee, year) index refuses a duplicate; each validation message; the double-count warning on the list; the edit warning after saving; CRUD through the controller; the endpoints pinned.
- [ ] Run and confirm they fail; implement; run the whole solution. The migration's `Up` holds only the table, its unique index and the foreign key.
- [ ] Commit `feat(payroll): record what an employee was paid before PeopleCore`.

---

### Task 2: One source for "earlier this year"

**Files:** `src/PeopleCore.Application/Payroll/Services/PayrollYearToDate.cs` (with its interface and `YearToDate` record), registered in DI; `PayrollRunService`, `FinalPayService`, `LeavePayout` (its de minimis days helper), `GovernmentReportService` (the 1601-C's earlier 13th month).

**Produces:** `IPayrollYearToDate.ForAsync(IReadOnlyCollection<Guid> employeeIds, int year, Guid? excludeRunId, CancellationToken ct)` returning, per employee, `BasicEarned`, `ThirteenthMonthPaid`, `ExemptUsed` and `DeMinimisLeaveDaysUsed`: the same Paid-run sums each site computes today, plus the opening balance as the spec lists.

- [ ] Failing tests: each site with an opening balance (a regular run's 13th month and tax, the year-end leave conversion's de minimis days, final pay's 13th month, the 1601-C's 13th-month split), with hand-derived figures in comments; and each site unchanged when there's no balance.
- [ ] Run and confirm they fail; move each site onto the service; run the whole solution. Every existing test must pass unchanged.
- [ ] Commit `feat(payroll): the 13th month, the 90,000 exemption and the de minimis cap count pay before PeopleCore`.

---

### Task 3: The 2316

**Files:** `Bir2316Service` (the DTO build), the 2316 DTO (a trailing `DateOnly? OpeningBalanceThrough`).

- [ ] Failing tests: a 2316 with an opening balance, item by item as the spec lists, reconciling Items 19, 21, 23 and 25A; the 13th month and other benefits split at ₱90,000 across the balance and the year's runs; the 1604-C following it; final pay's tax settle using it; no change without a balance.
- [ ] Run and confirm they fail; implement; run the whole solution.
- [ ] Commit `feat(payroll): the 2316 includes pay before PeopleCore`.

---

### Task 4: CSV import

**Files:** `OpeningBalanceCsv` in the OpeningBalances folder; the template and import endpoints on the controller.

- [ ] Failing tests: the template's header row; a valid file creates and updates; an invalid file saves nothing and lists every row's problems with the spec's messages; the formula-injection neutraliser applies; the endpoints pinned.
- [ ] Run and confirm they fail; implement; run the whole solution.
- [ ] Commit `feat(payroll): import opening balances from a CSV`.

---

### Task 5: The pages

**Files:** `src/PeopleCore.Web/Pages/Payroll/OpeningBalances.razor` (route `/opening-balances`, `payroll.manage`, nav under Payroll), `Bir2316.razor`, `PayrollRunDetail.razor` (the double-count warning among the run's warnings), `ApiClient.cs`, `NavMenu.razor`.

- [ ] Failing bUnit tests: the list by year with warnings; the form with the spec's field names and validation; the edit warning after saving; the template download and the import's row errors; the 2316 note; the run page's warning; mirror DTOs in order.
- [ ] Run and confirm they fail; implement; run the Web tests and the whole solution.
- [ ] Commit `feat(web): enter or import opening balances, and see them on the 2316`.

---

### Task 6: Verification

- [ ] The whole solution passes with no compiler warnings.
- [ ] `AddPayrollOpeningBalances` holds only what Task 1 lists.
- [ ] Browser check needs a signed-in account; record it as not done if no one can sign in.

---

### Final review fixes

One commit each, test first.

- [x] A balance's basic salary is after unpaid absences and tardiness (docs only).
- [x] Mark Paid refuses a run with the 13th month whose employee's basic earned or exemption used
  earlier in the year changed since compute (new nullable entry columns
  `BasicEarnedEarlierInYear` and `ExemptUsedEarlierInYear`, migration
  `AddYearToDateSnapshotToEntry`); an Approved run in that state can be recomputed.
- [x] Mark Paid names a run only when it was marked Paid after the entry was computed; otherwise a
  changed 13th month already paid is the balance's.
- [x] A 2316 for someone with a balance but no Paid run in the year, from the balance alone; the
  2316 page's years include the balance's (`GetYearsForEmployeeAsync`), and the 1604-C includes her.
- [x] The unpaid regular runs of a year are read Jan 1 to Dec 31, so a balance for 9999 works.
- [x] The 1601-C counts the balance as earlier in the year only for months after its through
  date's month.
- [x] Nits: the figures are rounded before they're checked; the repository's tracking comment, the
  import loader's redundant `AsTracking()` and the page's file-check comment are fixed.
