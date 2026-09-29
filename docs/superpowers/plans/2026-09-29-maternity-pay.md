# Maternity Pay in Payroll Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Payroll pays maternity leave the way RA 11210 splits it: the SSS maternity benefit is advanced once, tax-free, on a payroll HR picks; the leave cutoffs pay the salary less the part SSS covers (the taxable salary differential), or nothing beyond the benefit for an exempt employer; and HR tracks the SSS reimbursement.

**Architecture:**
- **The claim:** a `MaternityClaim` per approved maternity request holds the daily allowance, days, benefit and status. `MaternityClaimService` creates it, suggests the allowance from paid payroll history, and records the advance, reimbursement or denial.
- **The engine:** `PayrollComputationService.Compute` takes an optional `MaternityInput(decimal Advance, decimal Offset)`; the advance is a tax-free earning and the offset reduces regular pay before tax.
- **The run:** `PayrollRunService` works out each employee's advance and offset for the run (through `MaternityPayCalculator`), and marks the claim Advanced at Mark Paid.
- **Reports:** the advance is left out of the 2316, 1601-C and 1604-C; the offset lowers basic salary, so nothing else changes.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core (Npgsql, snake_case), Blazor WebAssembly, xUnit, FluentAssertions, Moq, bUnit, Postgres test fixture.

**Spec:** `docs/superpowers/specs/2026-09-29-maternity-pay-design.md`.

## Global Constraints

- **`MaternityClaim`** (table `maternity_claims`): `LeaveRequestId` (unique), `EmployeeId`, `DailyAllowance` (decimal?, `numeric(18,2)`), `Days` (decimal, `numeric(6,2)`), `Benefit` (decimal, `numeric(18,2)`, default 0), `Status` (`MaternityClaimStatus { Draft, Advanced, Reimbursed, Denied }`, stored as a string, max 40), `AdvanceRunId` (Guid?, SetNull to `payroll_runs`), `AdvancedAt` (DateOnly?), `ReimbursedOn` (DateOnly?), `ReimbursedAmount` (decimal?), `Note` (string?, max 500).
- **Entry columns:** `PayrollRunEmployee.MaternityBenefitAdvance` and `MaternityBenefitOffset` (decimal, `numeric(18,2)`, default 0).
- **Setting:** `PayrollSettings.ExemptFromMaternityDifferential` (bool, default false), on `PayrollSettingsDto` as a trailing member.
- **One migration** (`AddMaternityPay`) adds the table, the two entry columns and the setting.
- **Creating a claim:** only for an Approved request whose type has `IsMaternity`, once per request. `Days` = the request's `TotalDays`. Refusals: "Only an approved maternity leave request can have a claim.", "{name} already has a maternity claim for this leave."
- **Setting the allowance:** only while `Draft`; must be > 0 ("Enter the SSS daily maternity allowance."). `Benefit` = round(`DailyAllowance` × `Days`, 2), stored.
- **Suggested allowance:** the semester of contingency = the two calendar quarters ending with the quarter of the leave's `StartDate`; the window = the 12 months before that semester. For each month in the window, the MSC is `GovernmentReportMath.SssCredit(monthly employee SSS share)` over the employee's Paid regular runs whose `PeriodEnd` falls in the month (the same month assignment as the SSS remittance report, `GetPaidRunsByPeriodEndMonthAsync`). Suggestion = sum of the 6 highest MSCs ÷ 180, rounded to 2 dp; `MonthsFound` = months with an MSC. Fewer than 6 months still gives a suggestion over what was found.
- **The advance:** `PayrollRunEmployeeInput` gains `bool AdvanceMaternityBenefit = false`, stored on the entry as `AdvanceMaternityBenefit`. On compute (create and recompute) it is refused unless the employee has a `Draft` claim with an allowance: "{name} has no maternity claim ready to advance." Refused when a claim is already `Advanced`, `Reimbursed` or `Denied`, or when another regular run of any status carries the advance for that claim: "{name}'s maternity benefit was already advanced on {RunNumber}." The entry's `MaternityBenefitAdvance` = the claim's `Benefit`. It is added to `GrossPay`, never to the withholding base, the contribution base, the 13th-month basis or `ThirteenthMonthAndOtherBenefits`.
- **The offset:** on any regular run, for an employee with an Approved maternity request overlapping the period (the same request lookup the payroll attendance bridge uses): maternity days in the period = calendar days of the request inside `[PeriodStart, PeriodEnd]`. With a claim that has an allowance (any status): `MaternityBenefitOffset` = min(regular pay, `DailyAllowance` × maternity days). With `ExemptFromMaternityDifferential`: `MaternityBenefitOffset` = min(regular pay, round(regular pay × maternity days ÷ period calendar days, 2)), whether or not a claim exists. Otherwise 0. Regular pay is reduced by the offset before every downstream figure (withholding base, 13th-month basis, gross). Contributions stay on the monthly basic.
- **Warnings** on the run DTO (`PayrollRunDto.Warnings`, a list of strings, new trailing member): "Maternity benefit not set up yet for {name}." when maternity days > 0 and there is no claim or no allowance; "Maternity benefit not advanced yet for {name}." when a `Draft` claim with an allowance exists and no run carries its advance.
- **Mark Paid:** for each entry with `AdvanceMaternityBenefit` and `MaternityBenefitAdvance > 0`, the claim becomes `Advanced` with `AdvanceRunId` = the run and `AdvancedAt` = `PayDate`, in the same single save as the run's other Mark Paid changes. Discarding a run that carried an advance leaves the claim `Draft` (the advance is unpaid), and its `AdvanceRunId` stays null.
- **Reimbursement:** on an `Advanced` claim only ("Only an advanced claim can be reimbursed."). `ReimbursedOn` and `ReimbursedAmount` (> 0) are required. A `ReimbursedAmount` ≠ `Benefit` needs a note: "Explain why the reimbursement differs from the benefit." Status → `Reimbursed`.
- **Denial:** on an `Advanced` claim only ("Only an advanced claim can be denied."), note required: "Explain why SSS denied the claim." Status → `Denied`.
- **Outstanding** = sum of `Benefit` over `Advanced` claims.
- **Reports:** `MaternityBenefitAdvance` appears in no 2316 item, no 1601-C column and no 1604-C column. The payslip shows "SSS maternity benefit (advance)" as an earning and "Less: covered by SSS maternity benefit" against basic pay; earnings less deductions still equal net pay.
- **Permissions:** claim endpoints need `Permissions.PayrollManage`; pin them in `PermissionEquivalenceTests`.
- Build and test with `MSBUILDDISABLENODEREUSE=1 dotnet test PeopleCore.slnx -nodeReuse:false -p:UseSharedCompilation=false`. For migrations, set `DataProtection__KeyEncryptionKey=design-time-only-not-a-secret-0123456789` and `ASPNETCORE_ENVIRONMENT=Development`.
- Commit messages end with a blank line, then `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/PeopleCore.Domain/Entities/Payroll/MaternityClaim.cs` (new), `Enums/PayrollEnums.cs`, `PayrollRunEmployee.cs`, `PayrollSettings.cs` (modify) + configurations + migration `AddMaternityPay` | Storage |
| `src/PeopleCore.Application/Payroll/Maternity/MaternityMath.cs` (new) | Pure: semester window, suggested allowance, benefit, offset |
| `src/PeopleCore.Application/Payroll/Maternity/{IMaternityClaimRepository,IMaternityClaimService,MaternityClaimService,MaternityDtos}.cs` (new) + `Infrastructure/.../MaternityClaimRepository.cs` | Claims |
| `src/PeopleCore.Application/Payroll/Services/MaternityInput.cs` (new) + `PayrollComputationService.cs` (modify) | Engine input |
| `src/PeopleCore.Application/Payroll/Services/MaternityPayCalculator.cs` (new) + `PayrollRunService.cs`, `PayrollRunDtos.cs` (modify) | Advance, offset, warnings, Mark Paid |
| `PayslipLineBuilder.cs`, `PayslipDocument.cs` (modify) | Payslip lines |
| `src/PeopleCore.API/Controllers/Payroll/MaternityClaimsController.cs` (new) | Endpoints |
| `src/PeopleCore.Web/Pages/Payroll/MaternityClaims.razor` (new), `PayrollRuns.razor`, `PayrollRunDetail.razor`, `PayrollSettings` page, `ApiClient.cs`, `NavMenu.razor` (modify) | Pages |

---

### Task 1: Storage

**Files:** create `MaternityClaim.cs` and its EF configuration; modify `PayrollEnums.cs` (`MaternityClaimStatus`), `PayrollRunEmployee.cs` (two columns), `PayrollSettings.cs` and `PayrollSettingsDto` (the setting, read and written by the settings service), `AppDbContext`; migration `AddMaternityPay`. Test: `tests/PeopleCore.Infrastructure.Tests/Payroll/MaternityPayStorageTests.cs` (Postgres), the settings service tests.

**Interfaces produced:** the entity and columns exactly as in Global Constraints; `IMaternityClaimRepository` (in Application) with `GetByLeaveRequestAsync(Guid)`, `GetForEmployeeAsync(Guid employeeId)` (all claims, newest first, with `LeaveRequest` loaded), `GetAllAsync()` (with `Employee` and `LeaveRequest`), `AddAsync`, `UpdateAsync`; a `PayrollRunEmployee.AdvanceMaternityBenefit` bool column (default false) recording the input.

- [ ] Failing tests: a claim round-trips every field; the unique index on `leave_request_id` refuses a second claim; the entry columns and the setting round-trip; `AdvanceRunId` becomes null when the run is deleted (SetNull); a setting read back defaults to false.
- [ ] Run and confirm they fail → implement → migration → run the whole solution. Read the migration's `Up`: only the table, the three entry/setting columns and the index.
- [ ] Commit `feat(payroll): store maternity claims, the entry's advance and offset, and the differential exemption`.

---

### Task 2: Maternity math

**Files:** create `MaternityMath.cs`. Test: `MaternityMathTests.cs`.

**Interfaces produced:**
```csharp
public static class MaternityMath
{
    public const int SemesterMonths = 6, WindowMonths = 12, HighestMonths = 6, Divisor = 180;
    /// <summary>The 12-month window before the semester of contingency: (first month inclusive, last month inclusive).</summary>
    public static (DateOnly From, DateOnly To) ContributionWindow(DateOnly contingency);
    /// <summary>Sum of the 6 highest MSCs / 180, rounded to 2 dp; null when no MSC.</summary>
    public static decimal? SuggestedDailyAllowance(IEnumerable<decimal> monthlySalaryCredits);
    public static decimal Benefit(decimal dailyAllowance, decimal days);   // round 2
    /// <summary>Calendar days of [start,end] inside [periodStart,periodEnd].</summary>
    public static int DaysInPeriod(DateOnly start, DateOnly end, DateOnly periodStart, DateOnly periodEnd);
    public static decimal Offset(decimal regularPay, decimal dailyAllowance, int maternityDays);           // min(regular, allowance×days)
    public static decimal ExemptOffset(decimal regularPay, int maternityDays, int periodDays);            // min(regular, round(regular×days/periodDays,2))
}
```

- [ ] Failing tests: contingency Mar 10, 2026 → semester Oct 2025–Mar 2026 → window Oct 2024–Sep 2025 (both ends); contingency Jun 30 → window Jan–Dec 2025; contingency Jul 1, 2026 → window Apr 2025–Mar 2026; 8 MSCs where the 6 highest sum to 120,000 → 666.67; 4 MSCs → their sum ÷ 180; none → null; benefit 666.67 × 105 = 70,000.35; days in period for a request straddling both ends; offset floors at regular pay; exempt offset at 15 of 31 days.
- [ ] Run → fail → implement → pass → commit `feat(payroll): the SSS maternity benefit arithmetic`.

---

### Task 3: Claims

**Files:** create `IMaternityClaimService`, `MaternityClaimService`, `MaternityDtos.cs`, `MaternityClaimsController.cs`; register in DI. Test: `MaternityClaimServiceTests.cs`, `MaternityClaimsControllerTests.cs`, `PermissionEquivalenceTests`, a Postgres test for the suggestion over real paid runs.

**Interfaces produced:**
```csharp
public record MaternityClaimDto(Guid Id, Guid LeaveRequestId, Guid EmployeeId, string EmployeeName,
    DateOnly LeaveStart, DateOnly LeaveEnd, decimal Days, decimal? DailyAllowance, decimal Benefit,
    MaternityClaimStatus Status, Guid? AdvanceRunId, string? AdvanceRunNumber, DateOnly? AdvancedAt,
    DateOnly? ReimbursedOn, decimal? ReimbursedAmount, string? Note);
public record SuggestedAllowanceDto(decimal? DailyAllowance, int MonthsFound, DateOnly WindowFrom, DateOnly WindowTo);
public record SetAllowanceRequest(decimal DailyAllowance);
public record ReimburseRequest(DateOnly ReimbursedOn, decimal ReimbursedAmount, string? Note);
public record DenyRequest(string Note);
public record MaternityClaimsSummaryDto(IReadOnlyList<MaternityClaimDto> Claims, decimal Outstanding);
public interface IMaternityClaimService
{
    Task<MaternityClaimsSummaryDto> ListAsync(CancellationToken ct = default);
    Task<MaternityClaimDto> CreateAsync(Guid leaveRequestId, CancellationToken ct = default);
    Task<SuggestedAllowanceDto> SuggestAsync(Guid claimId, CancellationToken ct = default);
    Task<MaternityClaimDto> SetAllowanceAsync(Guid claimId, SetAllowanceRequest request, CancellationToken ct = default);
    Task<MaternityClaimDto> ReimburseAsync(Guid claimId, ReimburseRequest request, CancellationToken ct = default);
    Task<MaternityClaimDto> DenyAsync(Guid claimId, DenyRequest request, CancellationToken ct = default);
}
// Routes (all [RequirePermission(Permissions.PayrollManage)]):
// GET api/maternity-claims; POST api/maternity-claims/{leaveRequestId:guid} (201);
// GET api/maternity-claims/{id:guid}/suggestion; PUT .../allowance; PUT .../reimburse; PUT .../deny
```

**Rules:** exactly the Global Constraints. The suggestion reads Paid regular runs per window month through `GetPaidRunsByPeriodEndMonthAsync`, takes the employee's `SSSEmployee` summed per month, and `GovernmentReportMath.SssCredit` for the MSC (skip months with no entry or a null MSC).

- [ ] Failing tests for every rule and message; the suggestion's window and highest-6 selection with mocked runs; the Postgres suggestion over 8 real paid runs; the controller's routes and permission.
- [ ] Run → fail → implement → run the whole solution → commit `feat(payroll): maternity claims - suggested allowance, advance readiness, reimbursement and denial`.

---

### Task 4: The engine takes a maternity input

**Files:** create `MaternityInput.cs`; modify `PayrollComputationService.cs`, `PayslipLineBuilder.cs`, `PayslipDocument.cs`, `Bir2316Service.cs` / `GovernmentReportService.cs` only if a total would otherwise pick up the advance. Test: `PayrollComputationServiceMaternityTests.cs`, `PayslipLineBuilderTests`, a 2316/1601-C reconciliation test.

**Interfaces produced:** `public sealed record MaternityInput(decimal Advance, decimal Offset);` and `Compute(..., MaternityInput? maternity = null)`.

**Rules:** `regularPay -= Offset` before anything reads regular pay (withholding base, 13th-month basis, gross); `MaternityBenefitAdvance = Advance` joins `GrossPay` only; both stored on the entry. The payslip adds the two lines named in Global Constraints and its sums still hold. Contributions unchanged.

- [ ] Failing tests: an entry with offset 6,000 on regular 30,000 taxes 24,000 and keeps SSS/PhilHealth/Pag-IBIG on 30,000; an advance of 70,000.35 raises gross by exactly that and nothing else; both together; the payslip sums; the 2316 Item 19/52 and the 1601-C leave the advance out and take the reduced basic (hand-derived figures in comments).
- [ ] Run → fail → implement → all Application/Infrastructure tests → commit `feat(payroll): the engine pays the SSS maternity advance tax-free and nets the covered days off basic`.

---

### Task 5: The run advances, offsets, warns and settles

**Files:** create `MaternityPayCalculator.cs` (+ interface, DI); modify `PayrollRunService.cs`, `PayrollRunDtos.cs` (`AdvanceMaternityBenefit` on the input; `Warnings` and the two entry figures on the DTOs), `IPayrollRunRepository` (a query for runs carrying an advance for a claim). Test: `PayrollRunServiceTests`, `MaternityPayDbTests.cs` (Postgres, end to end through Mark Paid).

**Interfaces produced:**
```csharp
public sealed record MaternityPay(decimal Advance, decimal Offset, IReadOnlyList<string> Warnings, Guid? ClaimId);
public interface IMaternityPayCalculator
{
    Task<MaternityPay> ForAsync(PayrollRun run, Guid employeeId, bool advanceRequested, decimal regularPayBeforeOffset, bool exempt, CancellationToken ct = default);
}
```

**Rules:** exactly the Global Constraints for the advance, the offset, the warnings, Mark Paid and discard. Compute calls the calculator per employee (regular runs only), passes `MaternityInput`, collects warnings onto the run DTO; recompute keeps `AdvanceMaternityBenefit` from the stored entry.

- [ ] Failing tests: advance refused without a ready claim, refused twice (other run of any status, and claim already Advanced), paid once; offset split across two cutoffs of a 105-day leave; the floor; the exempt setting; both warnings; recompute reproduces; Mark Paid sets the claim Advanced with run and pay date in one save; discard leaves it Draft; a final-pay run gets no maternity handling.
- [ ] Run → fail → implement → whole solution → commit `feat(payroll): a payroll advances the maternity benefit and pays the salary differential`.

---

### Task 6: The pages

**Files:** create `Pages/Payroll/MaternityClaims.razor`; modify `PayrollRuns.razor` (per-employee "Advance maternity benefit", offered only for employees with a ready claim, from a new `GET api/maternity-claims/ready` → list of employee ids, `PayrollManage`), `PayrollRunDetail.razor` (columns and warnings, `data-maternity-warnings`), the payroll settings page (the switch), `ApiClient.cs`, `NavMenu.razor`. Tests in `tests/PeopleCore.Web.Tests`.

**Behaviour** (each tested): the claims list with outstanding total (`data-outstanding`); create from an approved maternity request (a picker of approved maternity requests without a claim, from `GET api/maternity-claims/eligible`); the allowance form showing the suggestion and months found (`data-suggestion`); reimburse and deny forms with the note rule; readable statuses; the create-payroll tick per employee; the run page's columns and warnings; the settings switch; errors from `detail`; in-flight guards; mirror DTOs matching the API in order.

- [ ] Failing bUnit tests → fail → implement → Web tests and the whole solution → commit `feat(web): maternity claims, the benefit advance on a payroll, and the differential exemption`.

---

### Task 7: Verification

- [ ] Whole solution passes with no compiler warnings.
- [ ] Read `AddMaternityPay`'s `Up`: only what Task 1 lists.
- [ ] Browser check needs a signed-in account; record as not done if no one can sign in.
