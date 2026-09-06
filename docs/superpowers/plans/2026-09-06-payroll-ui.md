# Payroll UI — Phase 3 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make payroll operable from the browser — create a run, compute it, review the register, mark it paid, and set an employee's compensation, with no curl and no SQL.

**Architecture:** One new list endpoint with its own summary projection, then three Blazor pages in the app's existing patterns (shadcn-style components under `IAS.Client.Components.UI`, Tailwind classes, `ApiClient` injection, `@attribute [Authorize]`).

**Tech Stack:** .NET 10 · Blazor WebAssembly · xUnit · Moq · FluentAssertions

**Design spec:** [`docs/superpowers/specs/2026-09-06-payroll-ui-design.md`](../specs/2026-09-06-payroll-ui-design.md)

## Global Constraints

- Target framework `net10.0`.
- **Do not modify the Phase 1 statutory files or their tests:** `PayrollComputationService.cs`, `PayrollLineBuilder.cs`, `DolePremiumRates.cs`, `BirWithholdingTax.cs`, `SssContributionSchedule.cs`, and `PayrollComputationServiceTests`, `PayrollLineBuilderTests`, `DolePremiumRatesTests`, `BirWithholdingTaxTests`. `git diff --stat` must never list them.
- Every payroll page carries `@attribute [Authorize(Roles = "Admin,HRManager,PayrollService")]`, matching its controller.
- **No compensation field may appear on any page or DTO reachable by `Manager` or `Employee`.**
- Server-side paged endpoints return `PeopleCore.Application.Common.DTOs.PagedResult<T>`, built with `PagedResult<T>.Create(items, totalCount, page, pageSize)`. The client-side mirror is the `PagedResult<T>` record at the bottom of `ApiClient.cs`.
- Blazor pages follow `src/PeopleCore.Web/Pages/HR/LeaveApprovals.razor`: `@page`, `@attribute [Authorize(...)]`, `@inject ApiClient Api`, `<Card>/<CardContent>/<Table>/<TableHeader>/<TableRow>/<TableHead>/<TableBody>/<TableCell>/<Badge>/<Button>/<Spinner>`, with a `null` model meaning "loading" and an empty collection meaning "nothing to show".
- This repository has **no Blazor component tests** and this phase does not add them. Pages are verified in a browser; the new endpoint gets service-level tests.
- The suite reports **199 passing** at the start of this plan.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/PeopleCore.Application/Payroll/DTOs/PayrollRunDtos.cs` | Gains `PayrollRunSummaryDto` |
| `src/PeopleCore.Application/Payroll/Interfaces/IPayrollRunRepository.cs` | Gains a paged query |
| `src/PeopleCore.Application/Payroll/Services/PayrollRunService.cs` | Gains `GetPagedAsync` |
| `src/PeopleCore.API/Controllers/Payroll/PayrollRunsController.cs` | Gains `GET /api/payroll-runs` |
| `src/PeopleCore.Web/Services/ApiClient.cs` | Payroll methods and client DTOs |
| `src/PeopleCore.Web/Pages/Payroll/PayrollRuns.razor` | Runs list + create dialog |
| `src/PeopleCore.Web/Pages/Payroll/PayrollRunDetail.razor` | The register |
| `src/PeopleCore.Web/Pages/Payroll/EmployeeCompensation.razor` | Compensation form |
| `src/PeopleCore.Web/Layout/NavMenu.razor` | Role-gated Payroll section |

---

### Task 1: The runs list endpoint

**Files:**
- Modify: `src/PeopleCore.Application/Payroll/DTOs/PayrollRunDtos.cs`, `Interfaces/IPayrollRunRepository.cs`, `Interfaces/IPayrollRunService.cs`, `Services/PayrollRunService.cs`, `src/PeopleCore.Infrastructure/Persistence/Repositories/PayrollRunRepository.cs`, `src/PeopleCore.API/Controllers/Payroll/PayrollRunsController.cs`
- Test: `tests/PeopleCore.Application.Tests/Payroll/PayrollRunServiceTests.cs`

**Interfaces:**
- Produces:
  - `PayrollRunSummaryDto` (see below)
  - `IPayrollRunRepository.GetPagedAsync(int page, int pageSize, CancellationToken ct = default)` returning `Task<(IReadOnlyList<PayrollRun> Items, int TotalCount)>`
  - `IPayrollRunService.GetPagedAsync(int page, int pageSize, CancellationToken ct = default)` returning `Task<PagedResult<PayrollRunSummaryDto>>`
  - `GET /api/payroll-runs?page=1&pageSize=20`

- [ ] **Step 1: Add the summary DTO**

Append to `PayrollRunDtos.cs`:

```csharp
/// <summary>
/// A run as it appears in a list. Deliberately omits the Employees collection that
/// <see cref="PayrollRunDto"/> carries: a page of twelve runs of two hundred employees would
/// otherwise ship 2,400 nested records to render twelve table rows.
/// </summary>
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

- [ ] **Step 2: Write the failing tests**

Add to `PayrollRunServiceTests.cs`, following the file's existing Moq + FluentAssertions style. Write each body out fully.

```csharp
[Fact]
public async Task GetPagedAsync_ProjectsTotalsWithoutCarryingEntries()
{
    // A run with two entries must summarise to EmployeeCount 2 and the sum of their
    // GrossPay/NetPay, and PayrollRunSummaryDto must have no per-employee collection at all.
}

[Fact]
public async Task GetPagedAsync_PassesPagingThroughAndReportsTheTotalCount()
{
    // page 2, pageSize 5 reaches the repository unchanged, and a repository total of 12
    // surfaces as TotalCount 12 on the PagedResult.
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/PeopleCore.Application.Tests/ --nologo --filter "FullyQualifiedName~GetPagedAsync"`
Expected: FAIL — `GetPagedAsync` not defined

- [ ] **Step 4: Implement the repository query**

`EmployeeCount`, `TotalGrossPay` and `TotalNetPay` are computed properties on `PayrollRun` that sum its `Employees` collection, so the query must `Include` the entries to compute them and the service projects them away. Duplicating the totals in SQL would let them drift from the entity's definition.

```csharp
    public async Task<(IReadOnlyList<PayrollRun> Items, int TotalCount)> GetPagedAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        var query = Context.PayrollRuns.AsQueryable();
        var total = await query.CountAsync(ct);

        // Entries are included so the run's computed totals can be evaluated, then projected
        // away by the service - the totals live on the entity so they cannot drift.
        var items = await query
            .Include(r => r.Employees)
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }
```

- [ ] **Step 5: Implement the service method and the endpoint**

Service, following `LeaveRequestService.GetPagedAsync`'s shape:

```csharp
    public async Task<PagedResult<PayrollRunSummaryDto>> GetPagedAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        var (items, total) = await _runRepo.GetPagedAsync(page, pageSize, ct);
        return PagedResult<PayrollRunSummaryDto>.Create(
            items.Select(ToSummaryDto).ToList(), total, page, pageSize);
    }
```

Controller, on `PayrollRunsController` (which already carries the class-level `[Authorize]`):

```csharp
    [HttpGet]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await _service.GetPagedAsync(page, pageSize, ct));
```

- [ ] **Step 6: Run to verify pass**

Run: `dotnet test PeopleCore.slnx --nologo -v q`
Expected: `Passed: 201`

- [ ] **Step 7: Commit**

```bash
git add src/PeopleCore.Application src/PeopleCore.Infrastructure src/PeopleCore.API tests/PeopleCore.Application.Tests
git commit -m "feat(payroll): add the payroll runs list endpoint"
```

---

### Task 2: ApiClient payroll methods

**Files:**
- Modify: `src/PeopleCore.Web/Services/ApiClient.cs`

**Interfaces:**
- Consumes: the endpoints from Task 1 plus the existing `GET/POST /api/payroll-runs`, `PUT /{id}/compute`, `PUT /{id}/mark-paid`, `GET/PUT /api/employee-compensation/{employeeId}`
- Produces: client-side records `PayrollRunSummaryDto`, `PayrollRunDto`, `PayrollRunEmployeeDto`, `EmployeeCompensationDto`, and `ApiClient` methods `GetPayrollRunsAsync`, `GetPayrollRunAsync`, `CreatePayrollRunAsync`, `ComputePayrollRunAsync`, `MarkPayrollRunPaidAsync`, `GetEmployeeCompensationAsync`, `UpsertEmployeeCompensationAsync`

- [ ] **Step 1: Add the client DTOs**

Mirror the server records at the bottom of `ApiClient.cs`, beside the existing client DTOs. Copy the field names and order from `src/PeopleCore.Application/Payroll/DTOs/PayrollRunDtos.cs` and `EmployeeCompensationDtos.cs` exactly — the client deserialises by name, and a mismatch yields a silent default rather than an error. `PayFrequency` and `PayrollRunStatus` cross the wire as strings by default, so declare them `string` on the client records.

- [ ] **Step 2: Add the methods**

Follow the existing methods' shape in that file: `GetFromJsonAsync` with the shared `JsonOptions` for reads, `PostAsJsonAsync`/`PutAsJsonAsync` for writes.

Two behaviours matter:
- `GetEmployeeCompensationAsync` must return `null` for a 404 rather than throwing — an employee with no compensation row yet is an empty form, not an error. Check `response.IsSuccessStatusCode` instead of `EnsureSuccessStatusCode`.
- `ComputePayrollRunAsync` and `MarkPayrollRunPaidAsync` return `204 No Content` on success and `400` with a problem-detail body when the service throws a `DomainException`. Return a result the page can show — a `(bool Ok, string? Error)` tuple, reading the `detail` field from the problem JSON on failure. Silently swallowing the 400 would leave an operator clicking Compute on a paid run with no feedback.

- [ ] **Step 3: Build**

Run: `dotnet build src/PeopleCore.Web/PeopleCore.Web.csproj --nologo -v q`
Expected: `0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add src/PeopleCore.Web/Services/ApiClient.cs
git commit -m "feat(web): add payroll API client methods"
```

---

### Task 3: The runs list page

**Files:**
- Create: `src/PeopleCore.Web/Pages/Payroll/PayrollRuns.razor`

**Interfaces:**
- Consumes: `ApiClient.GetPayrollRunsAsync`, `CreatePayrollRunAsync`, `GetEmployeesAsync`

- [ ] **Step 1: Build the page**

```razor
@page "/payroll-runs"
@attribute [Authorize(Roles = "Admin,HRManager,PayrollService")]
@inject ApiClient Api
@inject NavigationManager Nav
```

A `<Card>` containing a `<Table>` of runs: run number, period label, pay date, a `<Badge>` for status, employee count, total net pay. Clicking a row navigates to `/payroll-runs/{id}`. Follow `LeaveApprovals.razor` for the loading `<Spinner>`, the empty-state message, and the table markup.

A **Create Run** `<Button>` opens a `<Dialog>` asking for period start, period end, pay date and frequency, plus an employee multi-select.

- [ ] **Step 2: Load ALL active employees for the multi-select**

`GetEmployeesAsync` is paged with a default `pageSize` of 20. **Page through to exhaustion.** Taking page one silently creates a payroll run missing every employee after the twentieth — a payday nobody gets paid on, with no error:

```csharp
    private async Task<List<EmployeeListDto>> LoadAllActiveEmployeesAsync()
    {
        var all = new List<EmployeeListDto>();
        var page = 1;
        while (true)
        {
            var result = await Api.GetEmployeesAsync(page, 100);
            if (result is null || result.Items.Count == 0) break;
            all.AddRange(result.Items.Where(e => e.IsActive));
            if (page >= result.TotalPages) break;
            page++;
        }
        return all;
    }
```

- [ ] **Step 3: Submit the create request**

Build `CreatePayrollRunRequest` with one entry per selected employee, **leaving `DaysWorked`, `OvertimeHours` and `HolidayDays` null**. Null means "use what the attendance bridge derived". The dialog must not offer these fields — an operator typing a zero would silently beat the derived figure and unpay earned overtime and holiday premiums.

On success, navigate to the new run's detail page.

- [ ] **Step 4: Verify in the browser**

Start the app and confirm the page renders, the dialog opens, and creating a run navigates to its detail page. Use the run instructions in Task 6, Step 1.

- [ ] **Step 5: Commit**

```bash
git add src/PeopleCore.Web/Pages/Payroll/PayrollRuns.razor
git commit -m "feat(web): add the payroll runs list and create dialog"
```

---

### Task 4: The payroll register

**Files:**
- Create: `src/PeopleCore.Web/Pages/Payroll/PayrollRunDetail.razor`

**Interfaces:**
- Consumes: `ApiClient.GetPayrollRunAsync`, `ComputePayrollRunAsync`, `MarkPayrollRunPaidAsync`

- [ ] **Step 1: Build the register**

```razor
@page "/payroll-runs/{Id:guid}"
@attribute [Authorize(Roles = "Admin,HRManager,PayrollService")]
```

A header with the run number, period label, pay date, status badge, and the three totals. Then a `<Table>` — one row per employee: name, regular pay, overtime, holiday, night differential, gross, SSS, PhilHealth, Pag-IBIG, withholding tax, total deductions, net.

- [ ] **Step 2: Gate the actions on status**

| Status | Compute | Mark Paid |
|---|---|---|
| `Draft`, `Processing`, `ForApproval` | offered | offered |
| `Approved` | **not offered** | offered |
| `Paid` | **not offered** | **not offered** |

`Compute` is withheld at `Approved` because `PayrollRunService.ComputeAsync` throws a `DomainException` for both `Approved` and `Paid`. The UI matches the service rather than offering an action guaranteed to fail. Show the error text returned by the client method if a call fails anyway.

- [ ] **Step 3: Surface the missing-schedule warning**

When `EmployeesMissingAttendance > 0`, render a visible warning above the table naming the count — for example "3 employees had no shift schedule for this period, so no absences were derived for them."

This is the whole reason Phase 2 records the number. An operator about to click **Mark Paid** needs to see it; a log line does not reach them.

- [ ] **Step 4: Verify in the browser**

Confirm the register renders, `Compute` populates the figures, the warning appears when the count is non-zero, and the buttons disappear at the right statuses.

- [ ] **Step 5: Commit**

```bash
git add src/PeopleCore.Web/Pages/Payroll/PayrollRunDetail.razor
git commit -m "feat(web): add the payroll register with compute and mark-paid"
```

---

### Task 5: The compensation page

**Files:**
- Create: `src/PeopleCore.Web/Pages/Payroll/EmployeeCompensation.razor`
- Modify: `src/PeopleCore.Web/Pages/HR/Employees.razor`

**Interfaces:**
- Consumes: `ApiClient.GetEmployeeCompensationAsync`, `UpsertEmployeeCompensationAsync`

- [ ] **Step 1: Build the form**

```razor
@page "/employees/{EmployeeId:guid}/compensation"
@attribute [Authorize(Roles = "Admin,HRManager,PayrollService")]
```

Fields: basic salary (decimal), pay frequency (`Monthly` | `SemiMonthly`), tax code (text, default `"ME"`), dependents (int). Save and Cancel.

**A 404 from `GetEmployeeCompensationAsync` is an empty form, not an error** — an employee simply has no compensation row yet, and `PUT` creates one. Show the form with defaults rather than an error state.

- [ ] **Step 2: Add the entry point**

Add a **Compensation** `<Button Variant="ghost" Size="sm">` to each row's actions column on `Employees.razor`, navigating to `/employees/{id}/compensation`. Wrap it in `<AuthorizeView Roles="Admin,HRManager,PayrollService">` so it does not appear for a `Manager` browsing the employee list.

Do not add any compensation VALUE to the employees table — only the navigation button. `EmployeeListDto` carries no compensation field and must not gain one.

- [ ] **Step 3: Verify in the browser**

Set a salary for an employee, reload, and confirm it persisted. Then confirm the page loads cleanly for an employee who has never had compensation set.

- [ ] **Step 4: Commit**

```bash
git add src/PeopleCore.Web/Pages/Payroll/EmployeeCompensation.razor src/PeopleCore.Web/Pages/HR/Employees.razor
git commit -m "feat(web): add the employee compensation editor"
```

---

### Task 6: Navigation and end-to-end verification

**Files:**
- Modify: `src/PeopleCore.Web/Layout/NavMenu.razor`

- [ ] **Step 1: Add a role-gated Payroll section**

`NavMenu.razor` groups links in collapsible sections inside `<AuthorizeView Roles="...">` blocks, each toggled by an integer index via `ToggleSection(n)` / `IsOpen(n)`. Read the file first and follow that structure exactly, using the next free index.

The Payroll section needs its own `<AuthorizeView Roles="Admin,HRManager,PayrollService">` — the existing blocks are `Admin,HRManager`, which would hide payroll from a `PayrollService` account. It contains one link, **Payroll Runs** → `/payroll-runs`. Compensation is reached from an employee row, not from the nav.

- [ ] **Step 2: Run the app**

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/PeopleCore.API --urls http://localhost:5188
```

and separately:

```bash
dotnet run --project src/PeopleCore.Web
```

PostgreSQL is in Docker container `m2net-postgres`, database `peoplecore`, user `postgres`. The API needs `Jwt:Key`, already in user-secrets. Sign in with `admin@peoplecore.local` / `Admin@123456`.

**Capture the specific process ids you start and stop only those.** Do not use `taskkill /F /IM dotnet.exe`, which kills unrelated processes.

- [ ] **Step 3: Walk the whole flow and screenshot each step**

The database has employees but no compensation and no payroll runs, so seed as you go:

1. Employees list → **Compensation** on an employee → set a salary → save → reload and confirm it persisted.
2. Payroll Runs → **Create Run** → pick a period, select employees → create.
3. On the register: **Compute** → confirm figures populate and totals are non-zero.
4. Confirm the missing-schedule warning appears (no shift assignments exist, so every employee should be counted).
5. **Mark Paid** → confirm both buttons disappear.
6. Reload the run and confirm the figures are unchanged — the snapshot holding.

- [ ] **Step 4: Confirm the authorization boundary**

Confirm the Payroll nav section and the row-level Compensation button are absent for a non-payroll role. If no such account exists, create one through Identity rather than skipping the check, and say in your report how you verified it.

- [ ] **Step 5: Final verification**

Run: `dotnet build PeopleCore.slnx --nologo -v q` → `0 Error(s)`
Run: `dotnet test PeopleCore.slnx --nologo -v q` → all green
Run: `git diff --stat main..HEAD` → must not list any Phase 1 statutory file or its tests

- [ ] **Step 6: Commit**

```bash
git add src/PeopleCore.Web/Layout/NavMenu.razor
git commit -m "feat(web): add the payroll navigation section"
```

---

## Phase exit criteria

1. A payroll run can be created, computed, reviewed and marked paid entirely in the browser.
2. An employee's compensation can be set in the browser, and a run computed afterwards reflects it.
3. The runs list returns `PagedResult<PayrollRunSummaryDto>` and carries no per-employee entries.
4. `Compute` is not offered on an `Approved` or `Paid` run; `Mark Paid` is not offered on a `Paid` one.
5. A run with a non-zero `EmployeesMissingAttendance` shows a visible warning naming the count.
6. The Payroll nav section and the Compensation button are absent for `Manager` and `Employee`.
7. `dotnet build` is clean and the full suite is green.
8. The Phase 1 statutory files and their tests are byte-unchanged.
