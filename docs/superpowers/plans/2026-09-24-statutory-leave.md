# Philippine Statutory Leave Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** PeopleCore files, checks and approves the Philippine statutory leaves: Service Incentive Leave, Expanded Maternity Leave, maternity days allocated to the father, Paternity, Solo Parent, VAWC and the Magna Carta special leave. Each type carries its entitlement, eligibility and proof, and HR gets a page to manage leave types.

**Architecture:**
- **What a leave type says:** each type states its entitlement kind (Accrued, YearlyAllowance or PerEvent) and its rules, as new columns.
- **Counting days:** a `LeaveDayCounter` counts days, split by calendar year: calendar days, or scheduled working days from the employee's shift assignments less holidays.
- **Checking a request:** a pure `LeaveRules` class checks eligibility and limits in a fixed order, with fixed messages. `LeaveRequestService` gathers the facts (balances net of pending holds, approved event counts, overlaps), calls `LeaveRules`, and charges or refunds balances per year.
- **Documents:** they go through the existing `IStorageService`.
- **Confidential types:** hidden from managers.
- **Setting up the statutory types:** an idempotent "add the statutory set" action creates the missing types.
- **Web:** a Leave Types page, and changes to My Leave, Leave Approvals and the employee form.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core (Npgsql, snake_case), Blazor WebAssembly, xUnit, FluentAssertions, Moq, bUnit, Postgres test fixture (`DatabaseTestBase`).

**Spec:** `docs/superpowers/specs/2026-09-24-statutory-leave-design.md`.

## Global Constraints

- **Entitlement kinds:** `LeaveEntitlementKind { Accrued, YearlyAllowance, PerEvent }`, stored as a string. Existing types become `Accrued`, with every new rule off.
  - **Accrued:** yearly `LeaveBalance` rows built by accrual policies.
  - **YearlyAllowance:** the year's balance is created on first filing in that year, with `TotalDays = MaxDaysPerYear`.
  - **PerEvent:** no balance; `DaysPerEvent` per request. One request is one event.
- **New `LeaveType` columns:**

  | Column | Type | Default |
  |---|---|---|
  | `EntitlementKind` | enum | `Accrued` |
  | `CountsCalendarDays` | bool | false |
  | `DaysPerEvent` | decimal? | null |
  | `MinServiceMonths` | int? | null |
  | `RequiresMarried` | bool | false |
  | `RequiresSoloParentId` | bool | false |
  | `MaxEvents` | int? | null |
  | `IsConfidential` | bool | false |
  | `IsMaternity` | bool | false |

  `MaxDaysPerYear` becomes the allowance for YearlyAllowance types. `RequiresDocument` and `IsActive` are now enforced.
- **Employee:** gains `SoloParentIdNumber` (string?, max 50) and `SoloParentIdValidUntil` (DateOnly?). `HasValidSoloParentId(DateOnly on)` is true when both are set and `SoloParentIdValidUntil >= on`.
- **LeaveRequest:**
  - Gains `MaternityCase` (`MaternityCase { LiveBirth, MiscarriageOrEmergencyTermination }`?, stored as a string) and `DaysAllocatedToFather` (int, default 0).
  - Gains `DaysInStartYear` (decimal), the part of `TotalDays` charged to `StartDate.Year`; the rest goes to `EndDate.Year`.
  - Gains the document fields `DocumentFileName`, `DocumentStorageKey`, `DocumentContentType`, `DocumentSizeBytes`, `DocumentUploadedBy` (Guid?) and `DocumentUploadedAt` (DateTime?), all nullable.
- **RA 11210 constants** (`StatutoryLeave`, in the Domain):
  - `MaternityLiveBirthDays = 105`;
  - `MaternitySoloParentExtraDays = 15`;
  - `MaternityMiscarriageDays = 60`;
  - `MaxDaysAllocatedToFather = 7`.

  A maternity type's `DaysPerEvent` holds the live-birth base.
- **Maternity limit:** LiveBirth is `DaysPerEvent` (+15 when the employee has a valid solo parent ID on the start date), minus `DaysAllocatedToFather`. Miscarriage is 60.
- **Day counting:**
  - Calendar-day types count every date from start to end.
  - Other types count the dates the employee's shift assignment schedules (`ShiftScheduleResolver.Resolve` not null and not a rest day). Where it returns null (no assignment), Monday to Friday counts. Holidays are skipped (`IHolidayService.IsHolidayAsync` not null).
  - This applies to every type, VL and SL included.
  - Counts are split by year. A request may touch at most two calendar years.
- **Filing checks, in this order**, and all re-checked on approval. The date format is `MMM d, yyyy` in the invariant culture.

  | # | Check | Message |
  |---|---|---|
  | 1 | Inactive type | "{Type} is no longer available." |
  | 2 | Gender | "Employee is not eligible for this leave type." (unchanged) |
  | 3 | Service | "{Type} needs {n} months of service; you'll qualify on {date}." (the date is `HireDate.AddMonths(n)`) |
  | 4 | Married | "{Type} is for married employees." |
  | 5 | Solo parent ID | "{Type} needs a valid solo parent ID on your record; ask HR to add it." |
  | 6 | Maternity case missing | "Choose whether this is a live birth or a miscarriage or emergency termination." |
  | 7 | Father allocation | "Up to 7 days can be allocated to the father, for a live birth only." |
  | 8 | Event limit | "You've used {Type} {n} times, the most allowed." |
  | 9 | Span | "A leave request can't span more than two years." |
  | 10 | Zero days | "There are no working days in that range." (calendar-day types can't hit it) |
  | 11 | Overlap | "Employee has an overlapping leave request for these dates." (unchanged) |
  | 12 | Per-event limit | "{Type} is up to {n} days each time; this request is {m}." |
  | 13 | Balance | "You have {n} days of {Type} left for {year}." (the first short year) |
  | 14 | Document, on approval only | "{Type} needs a supporting document." |

  - On a maternity type, the per-event message is instead "Maternity leave for a live birth is up to {n} days; this request is {m}." or "Maternity leave for a miscarriage or emergency termination is up to {n} days; this request is {m}."
  - Numbers are formatted with `0.##`.
- **Pending holds:** a year's available days = `RemainingDays` − the days of that employee's other Pending requests of the same type charged to that year (`DaysInStartYear` to the start year, the rest to the end year).
- **Event count:** approved requests of that type for the employee. Rejected, Cancelled and Pending don't count.
- **Approve:**
  - Recount the days and re-run every check. The request itself is excluded from pending holds; the event count excludes it anyway, since it is Pending.
  - Set `TotalDays` and `DaysInStartYear` from the recount.
  - Add each year's days to that year's `UsedDays` (Accrued and YearlyAllowance). PerEvent touches no balance.
- **Cancel:** an approved request gives back `DaysInStartYear` and the rest to their years.
- **Documents:**
  - PDF, JPEG or PNG (`application/pdf`, `image/jpeg`, `image/png`), at most 10 MB. Otherwise: "Attach a PDF, JPG or PNG of at most 10 MB."
  - Uploaded with `PUT api/leave-requests/{id}/document` (multipart `file`), by the owner only, while Pending ("Only a pending request's document can be replaced."). One file per request; replacing it overwrites the fields.
  - The bucket comes from `DocumentStorageOptions`. The object key is `leave-requests/{requestId}/{Guid}{extension}`.
  - Opened with `GET api/leave-requests/{id}/document`, which returns `{ url }` (presigned, 300 seconds).
  - **Who may open it:**
    - the owner;
    - `approvals.all`;
    - `approvals.team` with `CanManageAsync`, unless the type is confidential.
  - Filing stays JSON.
- **Confidential types:**
  - **Leave Approvals queue:** the unscoped manager list (`GET api/leave-requests` with a ReportingManagerId scope) excludes them.
  - **Deciding:** Approve and Reject need `approvals.all`; anyone else gets 403.
  - **Viewing someone else's leave** (`GET api/leave-requests?employeeId=`, `GET api/leave-requests/{id}`): a caller who is neither the employee nor holds `approvals.all` sees `LeaveTypeId = Guid.Empty`, `LeaveTypeName = "Leave"`, `Reason = null`, `HasDocument = false`.
  - **Balances** (`GET api/leave-balances/{employeeId}`): confidential types' rows are left out unless the caller is the employee or holds `approvals.all`.
- **Deleting a leave type:** refused once it has any request or balance: "{Type} has been used; deactivate it instead."
- **Accrual:** `LeaveAccrualService.RunAccrualsAsync` accrues only Accrued, active types, and skips employees the type's `GenderRestriction` excludes.
- **The statutory set** (`POST api/leave-types/statutory`, `leave.manage`):
  - It creates the missing types, matched by code (trimmed, case-insensitive), and returns `{ added: [codes], skipped: [codes] }`. It is idempotent.
  - All types are paid, with `IsCarryOver = false`.
  - SIL gets a Monthly accrual policy of 5 days a year from 12 months' service, with no maximum tenure.

  | Code | Name | Kind | Days | Calendar | Rules |
  |---|---|---|---|---|---|
  | SIL | Service Incentive Leave | Accrued | MaxDaysPerYear 5 | no | IsConvertibleToCash, CountsAsVacationForDeMinimis |
  | ML | Maternity Leave | PerEvent | DaysPerEvent 105 | yes | IsMaternity, Female, RequiresDocument |
  | AML | Maternity Leave Allocated to Father | PerEvent | DaysPerEvent 7 | yes | Male, RequiresDocument |
  | PL | Paternity Leave | PerEvent | DaysPerEvent 7, MaxEvents 4 | no | Male, RequiresMarried, RequiresDocument |
  | SPL | Solo Parent Leave | YearlyAllowance | MaxDaysPerYear 7 | no | RequiresSoloParentId, MinServiceMonths 6 |
  | VAWC | VAWC Leave | YearlyAllowance | MaxDaysPerYear 10 | no | Female, IsConfidential, RequiresDocument |
  | SLW | Special Leave for Women (Magna Carta) | PerEvent | DaysPerEvent 60 | yes | Female, MinServiceMonths 6, RequiresDocument |

  All other flags are false. `CountsAsVacationForDeMinimis` is false on every type except SIL. `GenderRestriction` holds "Female" or "Male".
- **Permissions:**
  - The leave-type and statutory endpoints need `leave.manage`.
  - The filing options (`GET api/leave-requests/options`) need only a signed-in employee.
  - Uploading and opening documents is as above.
- Build and test with `MSBUILDDISABLENODEREUSE=1 dotnet test PeopleCore.slnx -nodeReuse:false -p:UseSharedCompilation=false`. For migrations, set `DataProtection__KeyEncryptionKey=design-time-only-not-a-secret-0123456789` and `ASPNETCORE_ENVIRONMENT=Development`.
- **EF trap:** an `AuditableEntity` gets its Guid at construction, so a new child added to a tracked parent is saved as an UPDATE. Add new rows explicitly.
- Commit messages end with a blank line, then `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/PeopleCore.Domain/Enums/LeaveEnums.cs` (new) | `LeaveEntitlementKind`, `MaternityCase` |
| `src/PeopleCore.Domain/Leave/StatutoryLeave.cs` (new) | RA 11210 constants |
| `src/PeopleCore.Domain/Entities/Leave/LeaveType.cs`, `LeaveRequest.cs` (modify) | New columns |
| `src/PeopleCore.Domain/Entities/Employees/Employee.cs` (modify) | Solo parent ID, `HasValidSoloParentId` |
| `src/PeopleCore.Infrastructure/Persistence/Configurations/Leave/*`, `.../Employees/EmployeeConfiguration.cs` (modify) + migration `AddStatutoryLeave` | Storage |
| `src/PeopleCore.Application/Leave/Services/LeaveDayCounter.cs` + `Interfaces/ILeaveDayCounter.cs` (new) | Days by year |
| `src/PeopleCore.Application/Leave/Services/LeaveRules.cs` (new) | Pure checks and limits |
| `src/PeopleCore.Application/Leave/Services/LeaveRequestService.cs` (modify) | Create, approve, cancel with the rules; options |
| `src/PeopleCore.Application/Leave/Services/LeaveDocumentService.cs` + interface (new) | Upload and open documents |
| `src/PeopleCore.Application/Leave/Services/StatutoryLeaveSet.cs` (new) | The statutory definitions and the add-missing action |
| `src/PeopleCore.Application/Leave/Services/LeaveTypeService.cs`, `LeaveAccrualService.cs`, `LeaveBalanceService.cs` (modify) | Full settings, delete guard, accrual filter |
| `src/PeopleCore.API/Controllers/Leave/LeaveController.cs` (modify) | Documents, confidential rules, options, statutory |
| `src/PeopleCore.Web/Pages/HR/LeaveTypes.razor` (new), `Pages/ESS/MyLeave.razor`, `Pages/HR/LeaveApprovals.razor`, `Pages/HR/Employees.razor`, `Services/ApiClient.cs`, `Layout/NavMenu.razor` (modify) | Pages |
| `tools/PeopleCore.DemoSeed/Seeding/Seeder.Setup.cs` (modify) | Press the statutory action |

---

### Task 1: Storage

**Files:**
- Create: `src/PeopleCore.Domain/Enums/LeaveEnums.cs`, `src/PeopleCore.Domain/Leave/StatutoryLeave.cs`, and the migration `AddStatutoryLeave`.
- Modify: `LeaveType.cs`, `LeaveRequest.cs` and `Employee.cs` (in `src/PeopleCore.Domain/Entities/...`), plus their EF configurations.
- Test: `tests/PeopleCore.Infrastructure.Tests/Leave/StatutoryLeaveStorageTests.cs` (Postgres), and `tests/PeopleCore.Application.Tests/Leave/SoloParentIdTests.cs`.

**Interfaces produced:**
```csharp
namespace PeopleCore.Domain.Enums;
public enum LeaveEntitlementKind { Accrued, YearlyAllowance, PerEvent }
public enum MaternityCase { LiveBirth, MiscarriageOrEmergencyTermination }

namespace PeopleCore.Domain.Leave;
public static class StatutoryLeave
{
    public const decimal MaternityLiveBirthDays = 105m;
    public const decimal MaternitySoloParentExtraDays = 15m;
    public const decimal MaternityMiscarriageDays = 60m;
    public const int MaxDaysAllocatedToFather = 7;
}

// LeaveType: EntitlementKind, CountsCalendarDays, DaysPerEvent, MinServiceMonths, RequiresMarried,
//            RequiresSoloParentId, MaxEvents, IsConfidential, IsMaternity
// LeaveRequest: MaternityCase?, DaysAllocatedToFather, DaysInStartYear, Document* (6 fields)
// Employee: SoloParentIdNumber, SoloParentIdValidUntil,
public bool HasValidSoloParentId(DateOnly on) =>
    !string.IsNullOrWhiteSpace(SoloParentIdNumber) && SoloParentIdValidUntil is { } until && until >= on;
```

**Rules:**
- Enums are stored as strings, with `HasMaxLength(40)`.
- `DaysPerEvent` is `decimal(5,2)`.
- `DocumentFileName` has max length 255, `DocumentStorageKey` 500, and `DocumentContentType` 100.
- `DaysInStartYear` is `decimal(6,2)`, default 0.
- Bool and enum columns get database defaults matching the C# defaults (`Accrued`, false). Check the EF `HasDefaultValue` sentinel issue:
  - a C# default equal to the column default is fine;
  - never give a bool a database default of true unless the C# default is also true.
- The migration has no data backfill, except `days_in_start_year = total_days` for existing requests whose start and end years match. Where the years differ, all of it goes to the start year, which matches today's behaviour.

- [ ] **Step 1: Write the failing tests.**
  - `SoloParentIdTests`: a valid ID on its expiry date returns true; one after expiry returns false; a missing number or date returns false.
  - `StatutoryLeaveStorageTests`:
    - A leave type with every new setting round-trips.
    - A request with `MaternityCase`, `DaysAllocatedToFather`, `DaysInStartYear` and all six document fields round-trips.
    - An employee's two solo parent fields round-trip.
    - A type inserted without the new columns gets `Accrued` and false.
- [ ] **Step 2: Run them and confirm they fail** (they won't compile).
- [ ] **Step 3: Implement.** Add the entities and configurations, then add the migration: `dotnet ef migrations add AddStatutoryLeave --project src/PeopleCore.Infrastructure --startup-project src/PeopleCore.API`, with the two environment variables. Add the `days_in_start_year` backfill SQL in `Up`.
- [ ] **Step 4: Run the tests and the whole solution; confirm they pass.** Read the migration's `Up`: only the columns above and the backfill.
- [ ] **Step 5: Commit** as `feat(leave): store statutory leave settings, maternity details, documents and solo parent IDs`.

---

### Task 2: Counting leave days

**Files:**
- Create: `src/PeopleCore.Application/Leave/Interfaces/ILeaveDayCounter.cs` and `src/PeopleCore.Application/Leave/Services/LeaveDayCounter.cs`. Register the counter in DI next to the other leave services.
- Test: `tests/PeopleCore.Application.Tests/Leave/LeaveDayCounterTests.cs`.

**Interfaces produced:**
```csharp
public interface ILeaveDayCounter
{
    /// <summary>Leave days from start to end inclusive, keyed by calendar year (only years with days, or the start year with 0).</summary>
    Task<IReadOnlyDictionary<int, decimal>> CountByYearAsync(
        Guid employeeId, LeaveType type, DateOnly start, DateOnly end, CancellationToken ct = default);
}
```

**Rules:**
- **Calendar-day types:** every date counts; holidays are not skipped.
- **Other types:**
  - Load the assignments once, with `IShiftAssignmentRepository.GetActiveForPeriodAsync([employeeId], start, end)`.
  - For each date, pick the assignment in effect the same way `PayrollAttendanceBridge` does (read its lookup and reuse it; don't invent a new one).
  - `ShiftScheduleResolver.Resolve` returns null: count the date only if it is Monday to Friday.
  - It returns a rest day: skip the date.
  - Otherwise, count the date unless `IHolidayService.IsHolidayAsync(date)` is not null.
- The result always contains the start year, even with 0 days.

- [ ] **Step 1: Write the failing tests**, with mocked repositories and holiday service:
  - a calendar-day type from Dec 30 to Jan 2 gives {2026: 2, 2027: 2};
  - with no assignment, a Mon–Sun week gives 5;
  - with a Mon–Sat fixed template, the same week gives 6;
  - a rotating pattern's rest-day slots are skipped;
  - a holiday on a scheduled day is skipped;
  - a range of only rest days gives {year: 0};
  - a working-day request over New Year splits by year.
- [ ] **Step 2: Run them and confirm they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run them and confirm they pass.**
- [ ] **Step 5: Commit** as `feat(leave): count leave days by the employee's shift, or by calendar, per year`.

---

### Task 3: The rules

**Files:**
- Create: `src/PeopleCore.Application/Leave/Services/LeaveRules.cs`.
- Test: `tests/PeopleCore.Application.Tests/Leave/LeaveRulesTests.cs`.

**Interfaces produced:**
```csharp
public sealed record LeaveRuleContext(
    LeaveType Type,
    Employee Employee,
    DateOnly Start,
    DateOnly End,
    IReadOnlyDictionary<int, decimal> DaysByYear,       // from ILeaveDayCounter
    MaternityCase? MaternityCase,
    int DaysAllocatedToFather,
    int ApprovedEvents,                                 // PerEvent only
    IReadOnlyDictionary<int, decimal> AvailableByYear); // Accrued/YearlyAllowance: remaining minus pending holds; empty for PerEvent

public static class LeaveRules
{
    /// <summary>Checks 1-8 of the Global Constraints, in order. Throws DomainException.</summary>
    public static void EnsureEligible(LeaveRuleContext c);
    /// <summary>Checks 9, 10, 12 and 13, in order. (Check 11, overlap, is the service's, run between the two.)</summary>
    public static void EnsureWithinLimits(LeaveRuleContext c);
    /// <summary>The per-event limit, or null for non-PerEvent types. Maternity follows the RA 11210 rule.</summary>
    public static decimal? PerEventLimit(LeaveType type, Employee employee, DateOnly start, MaternityCase? maternityCase, int daysAllocatedToFather);
    /// <summary>True when checks 1-5 pass on the given date (used for the filing options).</summary>
    public static bool IsEligibleOn(LeaveType type, Employee employee, DateOnly on);
}
```

**Rules:**
- Every check and message is exactly as in the Global Constraints table, rows 1 to 13.
- **Service:** `employee.HireDate.AddMonths(n) <= start`.
- **Married:** `employee.CivilStatus == CivilStatus.Married`.
- **Maternity case:** checks 6 and 7 apply only to `IsMaternity` types. On any other type, a non-zero `DaysAllocatedToFather` or a set `MaternityCase` is ignored: the service clears them before saving.
- **Event limit:** applies when `MaxEvents` is set and `ApprovedEvents >= MaxEvents`.
- **Balance:** for each year in `DaysByYear` with days > 0, in ascending order, if `AvailableByYear.GetValueOrDefault(year) < days`, throw the balance message with `n = max(0, available)`.

- [ ] **Step 1: Write the failing tests.** One test per check, pinning the message, plus the order: an inactive type that is also gender-restricted reports inactive. Also:
  - maternity limits of 105, 120 as a solo parent, 98 with 7 days to the father, and 60 for a miscarriage;
  - a father allocation of 8, and a father allocation on a miscarriage, are both refused;
  - Paternity at 4 approved events is refused, and at 3 is allowed;
  - a two-year balance where the second year is short reports that year;
  - `IsEligibleOn` for service, married and solo parent;
  - a three-year span is refused.
- [ ] **Step 2: Run them and confirm they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run them and confirm they pass.**
- [ ] **Step 5: Commit** as `feat(leave): the statutory eligibility and limit rules`.

---

### Task 4: Filing, approving and cancelling

**Files:**
- Modify: `src/PeopleCore.Application/Leave/Services/LeaveRequestService.cs`, `Interfaces/ILeaveRequestRepository.cs`, `Interfaces/ILeaveBalanceRepository.cs` and their Infrastructure implementations, and `DTOs/LeaveDtos.cs`.
- Test: `tests/PeopleCore.Application.Tests/Leave/LeaveRequestServiceTests.cs` (extend it, or create it if it's absent), and `tests/PeopleCore.Infrastructure.Tests/Leave/LeaveRequestRepositoryTests.cs` (Postgres).

**Interfaces consumed:** `ILeaveDayCounter` (Task 2) and `LeaveRules` (Task 3).

**Interfaces produced:**
```csharp
// ILeaveRequestRepository
Task<IReadOnlyList<LeaveRequest>> GetPendingAsync(Guid employeeId, Guid leaveTypeId, Guid? excludeId, CancellationToken ct = default);
Task<int> CountApprovedAsync(Guid employeeId, Guid leaveTypeId, CancellationToken ct = default);
// ILeaveBalanceRepository
Task AddAsync(LeaveBalance balance, CancellationToken ct = default);   // add if absent
// DTOs (new trailing members, defaults keep old callers valid)
public record CreateLeaveRequestDto(Guid EmployeeId, Guid LeaveTypeId, DateOnly StartDate, DateOnly EndDate, string? Reason,
    MaternityCase? MaternityCase = null, int DaysAllocatedToFather = 0);
public record LeaveRequestDto(..., DateTime CreatedAt,
    MaternityCase? MaternityCase, int DaysAllocatedToFather, bool HasDocument, string? DocumentFileName);
```

**Rules:**
- **Create:**
  1. Load the employee and type.
  2. Count the days.
  3. Build the context. For Accrued and YearlyAllowance, available days per year = the balance's `RemainingDays` − the pending holds.
     - For YearlyAllowance, a year with no balance row counts as `MaxDaysPerYear` available.
     - For Accrued, a year with no balance row counts as 0.
  4. Run `EnsureEligible`, then check overlap, then run `EnsureWithinLimits`.
  5. For YearlyAllowance, add any missing balance row (`TotalDays = MaxDaysPerYear`) for each year touched.
  6. Save with `TotalDays` = the sum and `DaysInStartYear` = the start year's days.
- **Approve:** as in the Global Constraints.
  - It additionally refuses when `RequiresDocument` is set and `DocumentStorageKey` is null.
  - Existing guards (Pending only, not the approver's own request) run first.
  - An Accrued year with no balance row can't be charged: throw the balance message (it can only happen if a balance was deleted).
- **Cancel:** gives the days back per year, as in the Global Constraints. The existing Pending/Approved rules stay.
- **Mapping:** `ToDto` maps the new fields. `HasDocument` = `DocumentStorageKey != null`.
- **Existing tests:** `CountWorkingDaysAsync` is removed. Tests that relied on Monday–Friday counting now get it through the no-assignment fallback; mock `IShiftAssignmentRepository` to return nothing.

- [ ] **Step 1: Write the failing tests.**
  - Each check is surfaced through Create, and Approve re-checks: a type deactivated after filing is refused on approval.
  - Pending holds: 5 days available and a 3-day pending request, so a new 3-day request is refused.
  - A YearlyAllowance balance is created on first filing (SPL: 7).
  - A PerEvent request touches no balance, and the fourth approved paternity event blocks the fifth.
  - A two-year Accrued request charges both years on approval and refunds both on cancel.
  - A maternity request with a solo parent ID allows 120 calendar days.
  - A document-requiring type can't be approved without a document, and can be once `DocumentStorageKey` is set.
  - Postgres: `GetPendingAsync` and `CountApprovedAsync`.
- [ ] **Step 2: Run them and confirm they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the Application, Infrastructure and DemoSeed tests and confirm they pass.** The demo seed files leave through the API; if a seeded request now fails a rule, fix the seed data rather than the rule.
- [ ] **Step 5: Commit** as `feat(leave): file, approve and cancel leave under the statutory rules`.

---

### Task 5: Documents and confidential leave

**Files:**
- Create: `src/PeopleCore.Application/Leave/Interfaces/ILeaveDocumentService.cs` and `src/PeopleCore.Application/Leave/Services/LeaveDocumentService.cs`.
- Modify: `src/PeopleCore.API/Controllers/Leave/LeaveController.cs` and `LeaveBalanceService` (or the controller's balances action).
- Test: `tests/PeopleCore.Application.Tests/Leave/LeaveDocumentServiceTests.cs`, `tests/PeopleCore.Application.Tests/Leave/LeaveControllerConfidentialTests.cs`, and `PermissionEquivalenceTests`.

**Interfaces produced:**
```csharp
public interface ILeaveDocumentService
{
    Task<LeaveRequestDto> UploadAsync(Guid requestId, Guid uploaderEmployeeId, Stream content,
        string fileName, string contentType, long length, CancellationToken ct = default);
    Task<string> GetDownloadUrlAsync(Guid requestId, CancellationToken ct = default);
}
// Routes
// PUT  api/leave-requests/{id:guid}/document  (multipart "file")  -> 200 LeaveRequestDto
// GET  api/leave-requests/{id:guid}/document                     -> 200 { url }
```

**Rules:**
- Documents follow the Global Constraints: accepted types, size, owner-only while Pending, the key format, who may open, and 300-second links.
- When the uploader isn't the owner, refuse with "You can only attach documents to your own leave requests."
- Store through `IStorageService.UploadAsync(bucket, key, stream, contentType)`. Get the bucket from `DocumentStorageOptions`, as `EmployeeDocumentService` does.
- Confidential rules follow the Global Constraints: the queue excludes them, deciding them needs `approvals.all`, others see a masked DTO, and balance rows are left out.
- Masking happens in the controller, using `ICurrentUserService.HasPermission(Permissions.ApprovalsAll)` and `EmployeeId`. It needs the type's `IsConfidential` flag, so add `bool IsConfidential` to `LeaveRequestDto`, filled from the type. Masking clears it to false.
- The queue exclusion belongs in the repository query: add a parameter `excludeConfidential` to `GetPagedAsync`, set when the caller lacks `approvals.all`.

- [ ] **Step 1: Write the failing tests.**
  - Upload: accepts PDF, JPEG and PNG; refuses other types and anything over 10 MB; refuses when the request isn't Pending or the uploader isn't the owner; replacing a file overwrites the fields; the key format is right.
  - Opening a document: owner yes; `approvals.all` yes; a manager of a direct report yes for VL and no for VAWC; an unrelated employee no.
  - Confidential: a manager's queue excludes VAWC; a manager approving VAWC gets 403; a manager viewing the employee's requests sees "Leave" with no reason; `approvals.all` sees everything; balances hide VAWC from managers.
  - Pin the two new routes in `PermissionEquivalenceTests`. The upload is self-service; opening is checked in the action.
- [ ] **Step 2: Run them and confirm they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run them and confirm they pass.**
- [ ] **Step 5: Commit** as `feat(leave): supporting documents, and confidential leave kept from managers`.

---

### Task 6: Leave-type settings, the statutory set, and accrual

**Files:**
- Create: `src/PeopleCore.Application/Leave/Services/StatutoryLeaveSet.cs`.
- Modify: `DTOs/LeaveDtos.cs`, `LeaveTypeService.cs` (and `ILeaveTypeService`), `ILeaveTypeRepository` and its implementation (`IsUsedAsync`), `LeaveAccrualService.cs`, `LeaveController.cs`, and `tools/PeopleCore.DemoSeed/Seeding/Seeder.Setup.cs`.
- Test: `LeaveTypeServiceTests`, `tests/PeopleCore.Application.Tests/Leave/StatutoryLeaveSetTests.cs`, the `LeaveAccrualService` tests, the DemoSeed tests, and `PermissionEquivalenceTests`.

**Interfaces produced:**
```csharp
// LeaveTypeDto / CreateLeaveTypeDto gain, as trailing members with defaults on the create DTO:
//   bool IsActive = true, LeaveEntitlementKind EntitlementKind = Accrued, bool CountsCalendarDays = false,
//   decimal? DaysPerEvent = null, int? MinServiceMonths = null, bool RequiresMarried = false,
//   bool RequiresSoloParentId = false, int? MaxEvents = null, bool IsConfidential = false, bool IsMaternity = false
public record StatutoryLeaveResultDto(IReadOnlyList<string> Added, IReadOnlyList<string> Skipped);
public interface ILeaveTypeService { ...; Task<StatutoryLeaveResultDto> AddStatutoryAsync(CancellationToken ct = default); }
// Route: POST api/leave-types/statutory  [RequirePermission(Permissions.LeaveManage)] -> 200 StatutoryLeaveResultDto
```

**Rules:**
- Create and Update read and write every setting. `IsActive` becomes editable.
- **Validation messages:**
  - A PerEvent type without `DaysPerEvent > 0`: "Set the days per event."
  - A YearlyAllowance type without `MaxDaysPerYear > 0`: "Set the days per year."
  - `IsMaternity` on a non-PerEvent type: "A maternity type must be per event."
  - `MaxEvents` on a non-PerEvent type is cleared.
- **Delete:** refused when `IsUsedAsync` (any request or balance): "{Type} has been used; deactivate it instead."
- **Statutory set:** exactly the Global Constraints table, including SIL's Monthly policy (`TenureMonthsMin = 12`, `TenureMonthsMax = null`, `DaysPerYear = 5`). Codes are matched trimmed and case-insensitively. An existing code is skipped untouched. Idempotent.
- **Accrual:** only Accrued, active types. Skip employees whose gender the type's `GenderRestriction` excludes.
- **Demo seed:** after creating VL and SL, `POST api/leave-types/statutory` as the admin.

- [ ] **Step 1: Write the failing tests.**
  - A round-trip of every setting.
  - Each validation message.
  - Delete refused when used, allowed when unused.
  - The statutory set: on an empty site it adds all 7 with the right settings and SIL's policy; a site with "sil " already there skips it; a second run adds nothing.
  - Accrual skips a YearlyAllowance type, an inactive type, and a Female type for a male employee.
  - The DemoSeed test pins the statutory call.
  - `PermissionEquivalenceTests` for the new route.
- [ ] **Step 2: Run them and confirm they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the whole solution and confirm it passes.**
- [ ] **Step 5: Commit** as `feat(leave): full leave-type settings, the Philippine statutory set, and accrual that respects them`.

---

### Task 7: Filing options, and solo parent IDs on the employee record

**Files:**
- Modify: `LeaveRequestService` (and interface), `LeaveController`, `src/PeopleCore.Application/Employees/DTOs/EmployeeDtos.cs`, and `EmployeeService`.
- Test: `LeaveRequestServiceTests`, the `EmployeeService` tests, and `PermissionEquivalenceTests` if needed.

**Interfaces produced:**
```csharp
public record LeaveFilingOptionDto(
    Guid LeaveTypeId, string Name, string Code, LeaveEntitlementKind Kind,
    bool CountsCalendarDays, bool RequiresDocument, bool IsMaternity,
    decimal? DaysLeftThisYear,     // Accrued/YearlyAllowance: remaining (or MaxDaysPerYear if no row yet) minus pending holds
    decimal? DaysPerEvent,         // PerEvent
    int? MaxEvents, int EventsUsed,
    bool HasSoloParentBonus);      // maternity: a valid solo parent ID today
Task<IReadOnlyList<LeaveFilingOptionDto>> GetFilingOptionsAsync(Guid employeeId, CancellationToken ct = default);
// Route: GET api/leave-requests/options -> the caller's own (employee_id claim; 403 without one)
// CreateEmployeeDto / UpdateEmployeeDto / EmployeeDto gain trailing:
//   string? SoloParentIdNumber = null, DateOnly? SoloParentIdValidUntil = null
```

**Rules:**
- The options list every active type for which `LeaveRules.IsEligibleOn(type, employee, today)` holds, where today is the Philippine date via `PhilippineTime`.
  - Accrued types are listed only when a balance row exists for this year.
  - The list is sorted by name.
- The employee service saves and returns the two solo parent fields. `SoloParentIdNumber` is trimmed, and an empty value is stored as null. The directory list doesn't carry them.

- [ ] **Step 1: Write the failing tests.**
  - The options for:
    - a female with no solo parent ID (ML with no bonus; no SPL);
    - a married male (PL, AML);
    - an unmarried male (AML but not PL);
    - someone with 3 months' service (no SPL or SLW);
    - an Accrued type without a balance, which is hidden;
    - days left net of pending holds;
    - `EventsUsed`.
  - Employee create and update round-trip the solo parent fields.
- [ ] **Step 2: Run them and confirm they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run them and confirm they pass.**
- [ ] **Step 5: Commit** as `feat(leave): what an employee can file, and solo parent IDs on the employee record`.

---

### Task 8: The Leave Types page

**Files:**
- Create: `src/PeopleCore.Web/Pages/HR/LeaveTypes.razor`.
- Modify: `src/PeopleCore.Web/Services/ApiClient.cs` (leave-type and accrual-policy DTOs and calls, and the statutory call) and `Layout/NavMenu.razor`.
- Test: `tests/PeopleCore.Web.Tests/Pages/HR/LeaveTypesTests.cs`.

**Behaviour** (each tested):
- **Route and access:** `/leave-types`, gated by `leave.manage` (page and nav entry), in the style of the existing permission-gated pages.
- **List** (`data-leave-types`): name, code, kind as a readable label ("Accrued", "Yearly allowance", "Per event"), limit ("5 days a year", "7 days each time, up to 4 times", "105 days a birth"), "Calendar days" or "Working days", active, and a rules summary (for example "Female · Document · Confidential").
- **Create and edit form** (`data-leave-type-form`): every setting.
  - The kind picker shows only the fields that apply: days per event and max events for PerEvent, days per year for YearlyAllowance, and the accrual policies for Accrued.
  - Gender is a select (Any / Female / Male).
  - API errors show in `data-leave-type-error`.
- **Accrual policies** (`data-accrual-policies`), for an Accrued type being edited: list, add, edit and deactivate, using the existing `api/leave-accrual-policies` endpoints (read the controller for their shapes).
- **"Add Philippine statutory leave"** (`data-add-statutory`): calls the statutory endpoint, then shows "Added: SIL, ML, … Skipped: …" and reloads the list.
- **Maternity note:** maternity types show "Paid through regular payroll for now; the SSS benefit split comes in a later release."
- **Delete:** asks for confirmation. The API's "has been used" refusal is shown.
- Mirror DTOs match the API exactly, field by field and in order, with enums as strings.

- [ ] **Step 1: Write the failing bUnit tests** for each behaviour above.
- [ ] **Step 2: Run them and confirm they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the Web tests and the whole solution; confirm they pass.**
- [ ] **Step 5: Commit** as `feat(web): a page to manage leave types, with the Philippine statutory set`.

---

### Task 9: My Leave, Leave Approvals and the employee form

**Files:**
- Modify: `src/PeopleCore.Web/Pages/ESS/MyLeave.razor`, `Pages/HR/LeaveApprovals.razor`, `Pages/HR/Employees.razor` (the employee create and edit form; find where the employee fields are edited), and `Services/ApiClient.cs`.
- Test: the existing Web tests for those pages, extended.

**Behaviour** (each tested):
- **My Leave**
  - The type dropdown comes from `GET api/leave-requests/options`, no longer from the balances.
  - Next to the chosen type (`data-leave-limit`):
    - Accrued and YearlyAllowance: "{n} days left this year".
    - PerEvent: "Up to {n} days each time", plus " ({used} of {max} used)" when `MaxEvents` is set.
    - Maternity: "Up to {limit} days", worked out live from the case, the solo parent bonus and the father allocation.
  - Maternity shows a case picker (`data-maternity-case`: "Live birth" or "Miscarriage or emergency termination"). For a live birth it also shows the father allocation (`data-father-days`, 0 to 7).
  - A document-requiring type shows a required file input (`data-leave-document`, accepting .pdf, .jpg, .jpeg and .png). Submit stays disabled until a file is chosen.
  - After filing, the file uploads with `PUT api/leave-requests/{id}/document`. If the upload fails, the request stays filed and the page says "Your request was filed, but the document didn't upload: {detail}. Attach it again from the request."
  - A Pending request with a document-requiring type offers "Attach document" or "Replace document".
  - API errors use `detail`, and a submit is disabled while in flight.
- **Leave Approvals**
  - Shows the maternity case and father days when set.
  - Shows a "View document" link (`data-view-document`) that fetches `GET .../document` and opens the URL.
  - When the type requires a document and none is attached, shows "No document attached" as a warning.
  - Refusals from the re-check show the API's `detail`.
- **Employee form:** two fields, "Solo parent ID number" and "Solo parent ID valid until", sent on create and update.
- Mirror DTOs match the API exactly.

- [ ] **Step 1: Write the failing bUnit tests** for each behaviour above.
- [ ] **Step 2: Run them and confirm they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the Web tests and the whole solution; confirm they pass.**
- [ ] **Step 5: Commit** as `feat(web): file statutory leave with its documents, and review them when approving`.

---

### Task 10: Verification

- [ ] Run `MSBUILDDISABLENODEREUSE=1 dotnet test PeopleCore.slnx -nodeReuse:false -p:UseSharedCompilation=false`. Expected: every project passes, with no compiler warnings.
- [ ] Read the `AddStatutoryLeave` migration's `Up`. It should contain only the columns in Task 1 and the `days_in_start_year` backfill.
- [ ] Browser check: it needs a signed-in account. Record it as not done if no one can sign in.
