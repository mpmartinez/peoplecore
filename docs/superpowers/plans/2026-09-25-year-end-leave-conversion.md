# Year-end Leave Conversion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A December regular payroll can pay out each employee's unused year-end-convertible leave (SIL by default) in cash. The pay-out uses final pay's tax split. The converted days are used up when the payroll is marked Paid.

**Architecture:**
- **Leave types:** each leave type gets a `ConvertsAtYearEnd` setting, which is on for SIL.
- **Payroll runs:** a regular run gets an `IncludesLeaveConversion` flag, allowed only when the period ends in December.
- **Computing each employee:** `YearEndLeaveConversion` works out the employee's days and passes the amounts to the engine through a new `LeaveConversionInput`. Final pay uses the same input.
- **Mark Paid:** it reuses final pay's "leave paid out" step, which moves to a shared `LeavePayout` helper.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core (Npgsql, snake_case), Blazor WebAssembly, xUnit, FluentAssertions, Moq, bUnit, Postgres test fixture.

**Spec:** `docs/superpowers/specs/2026-09-25-year-end-leave-conversion-design.md`.

## Global Constraints

- **New columns:**
  - `LeaveType.ConvertsAtYearEnd` (bool, default false; column `converts_at_year_end`).
  - `PayrollRun.IncludesLeaveConversion` (bool, default false; column `includes_leave_conversion`).
  - One migration adds both. It also turns `converts_at_year_end` on for leave types whose `upper(btrim(code)) = 'SIL'`.
- **Carry-over and conversion:** a leave type can't have both `IsCarryOver` and `ConvertsAtYearEnd`. Message: "A leave type can't both carry over and convert at year-end."
- **Statutory set:** it creates SIL with `ConvertsAtYearEnd = true`. Every other statutory type keeps it false.
- **Creating the run:** `CreatePayrollRunRequest` gains `bool IncludeLeaveConversion = false` as a trailing member, stored on the run and honoured on every recompute.
  - Only Regular runs whose `PeriodEnd.Month == 12` may carry it. Otherwise: "Year-end leave conversion goes on a December payroll."
- **Per employee on such a run** (year = `PeriodEnd.Year`):
  - **Types:** each active type with `ConvertsAtYearEnd`.
  - **Days:** max(0, that year's `LeaveBalance.RemainingDays` − the days of the employee's Pending requests of that type charged to that year). Charging follows the filing rule: `DaysInStartYear` goes to the start year, the rest to the end year. No balance row means 0.
  - **Amount:** `FinalPayMath.LeaveConversion(days per type with CountsAsVacationForDeMinimis, dailyRate)` gives `(DeMinimis, OtherBenefits)`. The daily rate is the one the engine works out for the entry.
  - **Once a year:** an employee who already has an entry with `LeaveConversionPay > 0` on another Regular run whose `PeriodEnd.Year` is the same year (any status) is refused: "{name}'s leave for {year} was already converted in {RunNumber}."
- **How the entry records it** (as on a final pay):
  - `LeaveConversionPay = DeMinimis + OtherBenefits`;
  - `LeaveConversionNonTaxable = DeMinimis`;
  - `LeaveConversionOtherBenefits = OtherBenefits`;
  - `FinalPayNonTaxable = DeMinimis`;
  - `FinalPayTaxable = 0`.

  The other benefits share the 90,000 exemption with the 13th month. The exemption already used earlier in the year is the sum of `ThirteenthMonthAndOtherBenefits` over the pay year's earlier Paid runs. The 13th month still due stays one twelfth of the basic less the 13th month already paid; that is a separate figure.
- **Mark Paid of a run with `IncludesLeaveConversion`:**
  - For each entry with `LeaveConversionPay > 0`, it recomputes the day list the same way. If that list no longer prices to `LeaveConversionPay` at the entry's `DailyRate`, it refuses: "{name}'s convertible leave has changed since this payroll was computed; recompute it before paying."
  - Otherwise it adds the days to each balance's `UsedDays`.
  - All checks run before anything changes.
- **Final pay:** its figures and wording are unchanged.
- **Web, create form:** a "Convert unused leave" tick box next to the 13th month, shown only when the chosen period ends in December.
- **Web, run detail:** a "Year-end leave conversion" badge and a "Leave conversion" column when the run has it.
- **Web, Leave Types page:**
  - The setting's label is "Converts to cash at year-end". It is hidden while carry-over is on, and carry-over is hidden while it is on.
  - The rules summary adds "Year-end cash".
  - The SIL note becomes: "Unused SIL is converted to cash on the December payroll (tick Convert unused leave)."
- Build and test with `MSBUILDDISABLENODEREUSE=1 dotnet test PeopleCore.slnx -nodeReuse:false -p:UseSharedCompilation=false`. For migrations, set `DataProtection__KeyEncryptionKey=design-time-only-not-a-secret-0123456789` and `ASPNETCORE_ENVIRONMENT=Development`.
- Commit messages end with a blank line, then `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/PeopleCore.Domain/Entities/Leave/LeaveType.cs`, `Entities/Payroll/PayrollRun.cs` (modify) + configurations + migration `AddYearEndLeaveConversion` | The two columns and the SIL update |
| `src/PeopleCore.Application/Leave/DTOs/LeaveDtos.cs`, `Services/LeaveTypeService.cs`, `Services/StatutoryLeaveSet.cs` (modify) | The setting, validation and statutory default |
| `src/PeopleCore.Application/Leave/Services/LeaveCharging.cs` (new) | `DaysChargedByYear`, shared by filing and conversion (moved from `LeaveRequestService`) |
| `src/PeopleCore.Application/Payroll/Services/LeaveConversionInput.cs` (new) + `PayrollComputationService.cs` (modify) | The engine input; final-pay extras route through it |
| `src/PeopleCore.Application/Payroll/Services/LeavePayout.cs` (new) | Prices a day list and records it as used (moved out of `FinalPayService`) |
| `src/PeopleCore.Application/Payroll/Services/YearEndLeaveConversion.cs` (new) | Days per employee for a December run |
| `src/PeopleCore.Application/Payroll/Services/PayrollRunService.cs`, `DTOs/PayrollRunDtos.cs` (modify) | Flag, refusals, compute and Mark Paid |
| `src/PeopleCore.Web/Pages/Payroll/PayrollRuns.razor`, `PayrollRunDetail.razor`, `Pages/HR/LeaveTypes.razor`, `Services/ApiClient.cs` (modify) | Pages |

---

### Task 1: The setting and the flag, stored

**Files:**
- Modify: `LeaveType.cs` and `PayrollRun.cs`, their EF configurations, `LeaveDtos.cs`, `LeaveTypeService.cs`, `StatutoryLeaveSet.cs`, `PayrollRunDtos.cs` (run DTOs gain `IncludesLeaveConversion`; the create request gains `IncludeLeaveConversion = false`).
- Create: migration `AddYearEndLeaveConversion`.
- Test:
  - `tests/PeopleCore.Infrastructure.Tests/Payroll/YearEndLeaveConversionStorageTests.cs` (Postgres);
  - `LeaveTypeServiceTests`;
  - `StatutoryLeaveSetTests`.

**Interfaces produced:**
```csharp
// LeaveType
public bool ConvertsAtYearEnd { get; set; }
// PayrollRun
public bool IncludesLeaveConversion { get; set; }
// LeaveTypeDto / CreateLeaveTypeDto: trailing `bool ConvertsAtYearEnd` (create default false)
// CreatePayrollRunRequest: trailing `bool IncludeLeaveConversion = false`
// PayrollRunDto / PayrollRunSummaryDto: trailing `bool IncludesLeaveConversion`
```

**Rules:**
- The migration adds both columns and runs the data update `UPDATE leave_types SET converts_at_year_end = TRUE WHERE upper(btrim(code)) = 'SIL';`. Expose the update as a public const on the migration class for the test, as `MarkVacationLeaveConvertible` does.
- The carry-over check in `LeaveTypeService` validation uses the message in Global Constraints.

- [ ] **Step 1: Write the failing tests.**
  - Postgres:
    - both columns round-trip;
    - the SIL update turns on `SIL`, `sil` and ` SIL `, and leaves `VL` alone.
  - Service:
    - carry-over plus conversion is refused;
    - the setting round-trips on create and update.
  - Statutory set: SIL has it on, and the other six have it off.
- [ ] **Step 2: Run them and confirm they fail.**
- [ ] **Step 3: Implement.** Create the migration with `dotnet ef migrations add AddYearEndLeaveConversion --project src/PeopleCore.Infrastructure --startup-project src/PeopleCore.API`.
- [ ] **Step 4: Run the tests and the whole solution; confirm they pass.** Also read the migration's `Up`: it should hold only the two columns and the update.
- [ ] **Step 5: Commit** with `feat(leave): a leave type can convert to cash at year-end, and a payroll run can carry the conversion`.

---

### Task 2: One leave-conversion path in the engine

**Files:**
- Create: `src/PeopleCore.Application/Payroll/Services/LeaveConversionInput.cs`.
- Modify: `PayrollComputationService.cs`, `FinalPayService.cs` (its call to `Compute`) and `PayrollRunService.cs` (the exemption-used-earlier figure).
- Test:
  - `tests/PeopleCore.Application.Tests/Payroll/PayrollComputationServiceLeaveConversionTests.cs` (new);
  - the existing final-pay tests, unchanged.

**Interfaces produced:**
```csharp
public sealed record LeaveConversionInput(decimal DeMinimis, decimal OtherBenefits);
// PayrollComputationService.Compute(..., FinalPayExtras? finalPay = null,
//     LeaveConversionInput? leaveConversion = null, decimal otherBenefitsExemptUsedEarlierInYear = 0m)
```

**Rules:**
- **`Compute` inputs:**
  - `Compute` fills the entry's leave fields from `leaveConversion`, exactly as the final-pay branch does today.
  - When `finalPay` is set, its leave amounts become the `LeaveConversionInput`, so there is one code path. Passing both `finalPay` and `leaveConversion` throws `ArgumentException`.
- **The 13th-month tax (the `ComputeThirteenthMonthTax` call, about line 362):**
  - It uses `otherBenefitsExemptUsedEarlierInYear` when supplied. It falls back to `thirteenthMonthPaidEarlierInYear` when that is 0, so callers that don't pass it keep today's behaviour.
  - `thirteenthMonthPaidEarlierInYear` still drives the 13th month due.
- **Regular runs:** `PayrollRunService` passes `otherBenefitsExemptUsedEarlierInYear` = the sum of `ThirteenthMonthAndOtherBenefits` over the pay year's earlier Paid runs, next to the existing 13th-month figure. Find how it builds `thirteenthMonthPaidEarlierInYear` (about line 436) and mirror it.
- **Final pay:** its own earlier-exemption figure (`LeaveOtherBenefitsExemptAsync` and friends) is unchanged.

- [ ] **Step 1: Write the failing tests.**
  - A regular entry with `LeaveConversionInput(12000, 6000)`:
    - `LeaveConversionPay` 18,000;
    - `LeaveConversionNonTaxable` 12,000;
    - `LeaveConversionOtherBenefits` 6,000;
    - `FinalPayNonTaxable` 12,000;
    - `FinalPayTaxable` 0;
    - `GrossPay` includes the 18,000;
    - the withholding base excludes it.
  - With a 13th month of 88,000 in the same entry, 4,000 of the 6,000 is taxed. Show the arithmetic in a comment.
  - With `otherBenefitsExemptUsedEarlierInYear` 90,000, all 6,000 is taxed.
  - Passing both inputs throws.
- [ ] **Step 2: Run them and confirm they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run all Application and Infrastructure tests.** The final-pay tests must pass unchanged.
- [ ] **Step 5: Commit** with `refactor(payroll): one leave-conversion path in the engine, for final pay and year-end alike`.

---

### Task 3: Working out and paying out year-end days

**Files:**
- Create:
  - `src/PeopleCore.Application/Leave/Services/LeaveCharging.cs`;
  - `src/PeopleCore.Application/Payroll/Services/LeavePayout.cs`;
  - `src/PeopleCore.Application/Payroll/Services/YearEndLeaveConversion.cs`, with its interface, registered in DI.
- Modify:
  - `LeaveRequestService.cs`, to use `LeaveCharging`;
  - `FinalPayService.cs`, to use `LeavePayout` for `LeavePaidOutAsync` and `RecordLeavePaidOutAsync`, keeping its message.
- Test:
  - `tests/PeopleCore.Application.Tests/Payroll/YearEndLeaveConversionTests.cs`;
  - `LeavePayoutTests.cs`;
  - the existing final-pay and leave tests, unchanged.

**Interfaces produced:**
```csharp
public static class LeaveCharging
{
    public static IEnumerable<(int Year, decimal Days)> DaysChargedByYear(LeaveRequest r);
}

public sealed record LeavePaidOut(LeaveBalance Balance, decimal Days);   // moved here from FinalPay if defined there

public static class LeavePayout
{
    /// <summary>The (DeMinimis, OtherBenefits) of a day list at a daily rate.</summary>
    public static (decimal DeMinimis, decimal OtherBenefits) Price(IEnumerable<LeavePaidOut> days, decimal dailyRate);
}

public interface IYearEndLeaveConversion
{
    /// <summary>Each year-end type's convertible days for the employee in the year.</summary>
    Task<IReadOnlyList<LeavePaidOut>> DaysAsync(Guid employeeId, int year, CancellationToken ct = default);
    /// <summary>Adds the days to each balance's UsedDays.</summary>
    Task RecordAsync(IReadOnlyList<LeavePaidOut> paidOut, CancellationToken ct = default);
}
```

**Rules:**
- **Days:** as in Global Constraints. Use the active types with `ConvertsAtYearEnd`, the year's balances (`ILeaveBalanceRepository.GetByEmployeeAsync(employeeId, year)`, which includes `LeaveType`), and the Pending holds (`ILeaveRequestRepository.GetPendingAsync` plus `LeaveCharging.DaysChargedByYear`).
- **Balances:** only balances with days > 0 are returned.
- **Final pay:** its day list stays as it is (convertible types, `RemainingDays`, no pending holds). Only the pricing and recording move to `LeavePayout`, with no behaviour change.

- [ ] **Step 1: Write the failing tests.**
  - SIL: 5 remaining with 2 pending gives 3.
  - VL (converts): remaining 1, pending 3 gives 0 (floored, not returned).
  - An inactive year-end type is skipped.
  - A type without the setting is skipped.
  - No balance row gives nothing.
  - A pending request spanning New Year charges only its year's part.
  - `Price`: 8 VL + 5 SIL days at 1,000 gives (10,000, 3,000).
  - `RecordAsync` adds to `UsedDays`.
- [ ] **Step 2: Run them and confirm they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run all Application and Infrastructure tests.** They must pass, with final pay unchanged.
- [ ] **Step 5: Commit** with `feat(payroll): work out and pay out an employee's year-end convertible leave`.

---

### Task 4: The December payroll converts

**Files:**
- Modify: `PayrollRunService.cs` (create, compute and Mark Paid), and `PayrollRunRepository` if a query for the once-a-year check is needed.
- Test: `PayrollRunServiceTests`, and `tests/PeopleCore.Infrastructure.Tests/Payroll/YearEndLeaveConversionDbTests.cs` (Postgres).

**Interfaces consumed:**
- `IYearEndLeaveConversion` (Task 3);
- `LeavePayout.Price`;
- `Compute(..., leaveConversion:, otherBenefitsExemptUsedEarlierInYear:)` (Task 2).

**Rules:**
- **Create:** store the flag. Refuse a non-December period, or a FinalPay run, with the December message.
- **Compute (create and recompute), for each employee on a flagged run:**
  1. Days = `DaysAsync(employeeId, PeriodEnd.Year)`.
  2. Apply the once-a-year check (another Regular run in the same `PeriodEnd.Year` whose entry for the employee has `LeaveConversionPay > 0`) and refuse with its message.
  3. Price the days at the daily rate the engine will use. The engine works the rate out, so price inside the engine call, or compute the rate once the same way.
  4. Pass the priced amounts as `LeaveConversionInput`.
- **Mark Paid (flagged runs):** for every entry with `LeaveConversionPay > 0`:
  - redo `DaysAsync`;
  - if `Price(...)` at `entry.DailyRate` no longer adds up to `LeaveConversionPay`, refuse with the message from the Global Constraints;
  - after all checks pass, save everything together with the run's other Mark Paid changes, as the final-pay path does.
- A final-pay run never converts on this path.

- [ ] **Step 1: Write the failing tests.**
  - A December run with the flag gives an entry of 3 SIL days at a 1,200 daily rate = 3,600, all de minimis.
  - A November run with the flag is refused.
  - A second December run with the flag, for an employee already converted, is refused with its message.
  - A recompute reproduces the entry.
  - Mark Paid adds 3 to SIL's `UsedDays`.
  - If a request is approved after compute, Mark Paid refuses and changes nothing (loans untouched, status unchanged).
  - A flagged run with the 13th month: its 2316 and 1601-C reconcile. Write a Postgres test that pays the run and builds both.
- [ ] **Step 2: Run them and confirm they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the whole solution and confirm it passes.**
- [ ] **Step 5: Commit** with `feat(payroll): a December payroll converts unused year-end leave to cash`.

---

### Task 5: The pages

**Files:** modify `src/PeopleCore.Web/Services/ApiClient.cs` (mirrors), `Pages/Payroll/PayrollRuns.razor`, `Pages/Payroll/PayrollRunDetail.razor` and `Pages/HR/LeaveTypes.razor`. Tests go with their existing Web tests.

**Behaviour** (each tested):
- **Create payroll:** a "Convert unused leave" tick box (`data-leave-conversion`) next to the 13th month control, shown only when the period end is in December. It sends `IncludeLeaveConversion`. API refusals show the way the page shows others.
- **Run detail:** when `IncludesLeaveConversion` is set, show a "Year-end leave conversion" badge (`data-leave-conversion-badge`) and a "Leave conversion" column per employee.
- **Runs list:** a small "Leave conversion" badge on flagged runs.
- **Leave Types:**
  - The "Converts to cash at year-end" setting (`data-converts-at-year-end`) is hidden while carry-over is on, and carry-over is hidden while it is on.
  - The rules summary adds "Year-end cash".
  - The SIL note reads exactly as in Global Constraints.
- Mirror DTOs match the API field by field and in order.

- [ ] **Step 1: Write the failing bUnit tests.**
- [ ] **Step 2: Run them and confirm they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the Web tests and the whole solution; confirm they pass.**
- [ ] **Step 5: Commit** with `feat(web): tick year-end leave conversion on a December payroll, and set which leave converts`.

---

### Task 6: Verification

- [ ] Run the whole solution: every project passes with no compiler warnings.
- [ ] Read `AddYearEndLeaveConversion`'s `Up`: it should hold only the two columns and the SIL update.
- [ ] Browser check: this needs a signed-in account. If no one can sign in, record it as not done.

---

### Task 5b: The 13th month from the web (added 2026-09-25)

The web could not pay the 13th month on any regular payroll: the create form never sent `IncludeThirteenthMonth`, nothing could change it after create, and the run page had no 13th month column. The legal deadline is Dec 24 (PD 851).

**Rules:**
- **API:** `IPayrollRunService.SetThirteenthMonthAsync(Guid runId, bool include)` and `PUT api/payroll-runs/{id:guid}/thirteenth-month` with body `{ "include": bool }`, under `PayrollManage`, returning the updated `PayrollRunDto`. Pin it in `PermissionEquivalenceTests`.
  - It sets every entry's `IncludeThirteenthMonth`, recomputes the run, and sends an Approved or For approval run back to Draft.
  - Refusals:
    - a Paid run: "A paid payroll run can't be changed.";
    - a final pay: "A final pay's 13th month can't be changed here.";
    - no change: "This payroll already includes the 13th month." / "This payroll already leaves out the 13th month."
  - Allowed in any month, so advances work (the engine nets out 13th month paid earlier in the year).
- **Run DTOs:** `PayrollRunDto` and `PayrollRunSummaryDto` gain a trailing `bool IncludesThirteenthMonth`, true when any entry includes it.
- **Create form:** an "Include 13th month" tick box (`data-thirteenth-month`), shown for every period, in a "Year-end pay" group with Convert unused leave. It sends `includeThirteenthMonth` on every employee input.
- **Run detail:**
  - a "13th month" badge (`data-thirteenth-month-badge`) when the run includes it;
  - a "13th month" column when any entry has `ThirteenthMonth > 0`;
  - on a regular run that isn't Paid, a toggle ("Include 13th month" / "Leave out the 13th month"). It asks first; on an Approved or For approval run, the confirmation says the run goes back to Draft for approval again. It reloads afterwards and shows the API's refusals.
- **Runs list:** a "13th month" badge on runs that include it.

- [ ] Steps: failing tests (service: toggle on/off recomputes and resets status, refusals; controller route and permission; bUnit: tick box sends the flag on every employee, badge, column, toggle and confirm, refusals) → run and confirm they fail → implement → run the whole solution → commit `feat(payroll): pay the 13th month from the web - tick it on a payroll, or switch it on afterwards`.
