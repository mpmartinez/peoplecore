# Final Pay Run Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Final pay for a separated employee is a payroll run of its own type, computed by the same engine. It pays salary to the last working day, the pro-rated 13th month, cash for unused convertible leave, and statutory separation or retirement pay. It deducts loan balances and accountabilities, and settles the year's tax. It can't be marked Paid until clearance is complete, and it flows into the 2316, the 1601-C and the alphalist.

**Architecture:**
- **Pure math:** a `FinalPayMath` class works out service years, separation pay, retirement pay and the leave-conversion tax split.
- **The run:** `FinalPayService` builds a single-employee "Final pay" run from the separation. It calls the existing `PayrollComputationService.Compute` with a `FinalPayExtras` argument carrying the short-period base pay, the extra earnings, full loan balances, extra deductions and the settled tax.
- **The tax:** it comes from the employee's 2316 for the year, built over their Paid runs plus this draft entry, so the certificate balances by construction.
- **Recompute:** `PayrollRunService.ComputeAsync` hands Final-pay runs back to `FinalPayService`, so a recompute reproduces them.
- **Plan 1 carry-overs:**
  - the overdue flag checks for a Paid final-pay run;
  - clearance edits are locked once final pay is Paid;
  - undoing a clearance is recorded;
  - employees who were inactive before separations existed can have one recorded.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core (Npgsql, snake_case), Blazor WebAssembly, xUnit, FluentAssertions, Moq, bUnit, Postgres test fixture.

**Spec:** `docs/superpowers/specs/2026-09-22-separation-final-pay-coe-design.md` (sections "Final pay as a payroll run" and "Storage"). One deliberate simplification: the year-end tax adjustment is the final-pay entry's `WithholdingTax` itself, which can be negative (a refund), not a separate `TaxAdjustment` column. Every report already totals `WithholdingTax`, so they balance without new wiring.

## Global Constraints

- **Run types:** `PayrollRunType { Regular, FinalPay }`. Every existing run is Regular.
- **Final-pay run number:** `FP-<year>-<nnn>`, where year is the pay date's year and nnn is one past the highest `FP-<year>-` number already used (not a count: a run whose pay date moves year is renumbered, leaving a gap).
- **One employee per final-pay run.** It must have a separation, and each separation has at most one final-pay run (`Separation.FinalPayRunId`).
- **Default period:** from the day after the employee's last Paid Regular run's `PeriodEnd` (or, if none, the first of the last working day's month - or the hire date, when that is later) to the last working day. HR may set a different start, which must not be after the last working day, nor on or before the `PeriodEnd` of a Paid Regular run that included the employee: "Payroll {RunNumber} already paid up to {PeriodEnd:MMM d, yyyy}; start final pay after that."
  - **No salary left:** when that default start falls after the last working day, the final pay is still created, with no salary: its period is stored as the last working day alone (`PeriodStart = PeriodEnd = LastWorkingDay`), `WorkingDays = 0`, and no attendance. It carries only the 13th month, leave conversion, separation or retirement pay, loans, HR deductions and the tax settle. Recompute reproduces it from the stored inputs. The summary's `NoSalaryDays` says so; an update with a null start keeps it.
- **Contributions:** a final pay tops the separation month (the last working day's month) up to exactly one month's SSS, PhilHealth and Pag-IBIG on the monthly basic, for the employee and employer shares alike: `max(0, the month's full contribution − what that month's Paid runs already deducted)`. "That month's Paid runs" are those whose `PeriodEnd` falls in the month, as the remittance reports pick them (`GetPaidRunsByPeriodEndMonthAsync`), this run excluded. Never a semi-monthly half on its own, never past the month.
  - **Unpaid regular runs:** creating a final pay, or changing its period, is refused while a Regular run that includes the employee and isn't Paid ends on or after the earlier of the final period's start and the first of the last working day's month (it would overlap the final period, pay salary after the separation, or take the month's contributions again): "Payroll {RunNumber} covers {PeriodLabel} and isn't paid yet; pay it before creating final pay."
- **Base pay of the final period:** `DailyRate × WorkingDays`, where WorkingDays are the period's *salary days*, following the daily-rate factor the rate is derived with. On 365 (rest days paid) they are every calendar day from `PeriodStart` to the last working day, inclusive. On 313 and 261 they are the days the employee's shift schedules, falling back to Monday to Friday where no shift is assigned. The attendance bridge's absences still come off once, per scheduled day absent. They are stored on the run's final-pay inputs and reused on recompute. Regular runs are unchanged.
- **Allowances:** each of the employee's allowances is paid for the salary days, pro-rated like base pay: monthly amount × 12 / factor × salary days. Taxable and non-taxable allowances are classified as on a regular run. No salary days, no allowances.
- **13th month:** included. Its year is the **last working day's** year: one twelfth of the basic earned in that year's Paid runs (selected by pay date, as elsewhere) plus this final pay's own RegularPay, less the 13th month already paid in that year. The tax settle and the 2316 stay on the pay date's year.
- **Leave conversion:** remaining days (`LeaveBalance.RemainingDays`, current pay year) of each leave type with `IsConvertibleToCash`, times the daily rate. Up to 10 days in total across types with `CountsAsVacationForDeMinimis` are non-taxable, and the rest is taxable.
- **Separation pay** (type AuthorizedCause), per year of service:
  - one month: Redundancy, LaborSavingDevices;
  - half a month: Retrenchment, ClosureNotDueToLosses, Disease;
  - none: ClosureDueToSeriousLosses.

  The minimum is one month's pay whenever the rate isn't zero. A month's pay is the current monthly basic salary. Non-taxable.
- **Retirement pay** (type Retirement): age 60 to 65 inclusive on the last working day, and at least 5 years of service. It is 22.5 × daily rate × service years. Non-taxable when eligible; when not eligible, nothing is computed.
- **Service years:** from hire date to last working day, in whole years, plus one when the remaining fraction is at least 6 months.
- **HR override:** replaces the computed separation or retirement pay, or adds one where none is computed, and requires a note. It is non-taxable only when the separation type and conditions make the computed amount non-taxable; otherwise taxable.
- **Loans:** the full remaining balance of every active loan, capped so the total fits in net pay after statutory deductions, pro-rated across loans as today. HR-added deductions come after the loans, under the same cap.
- **Tax:** `WithholdingTax` of the final-pay entry = 2316 `Item24_TaxDue` (built over the pay year's Paid runs plus this entry, with the employee's saved 2316 inputs) − the tax already withheld in Paid runs − `Item25B_PrevTaxWithheld` − `Item27_PeraTaxCredit`. It can be negative. The pay year is the pay date's year, even when the last working day was in the year before.
- **Mark Paid of a final-pay run:** refused until the separation's clearance is complete: "Clear {item, item} before paying final pay."
- **Permissions:** `Permissions.PayrollManage` for creating, updating and paying final pay; `Permissions.EmployeesManage` for separations as before.
- **Overdue:** `Status == Separated && FinalPayDueBy < today && (no final-pay run || its status != Paid)`.
- **Clearance locking:** all clearance changes are refused once the final-pay run is Paid: "Final pay has been paid; clearance can't change now."
- **Clearance undo:** recorded on the item (`LastUndoneBy`, `LastUndoneAt`, `LastUndoneNote` holding the note that was cleared).
- **Separations for employees who were already inactive:** HR can record a separation for an inactive employee who has a `SeparationDate` and no separation. It is created directly as Separated with `LastWorkingDay = SeparationDate`, and the given last working day must equal it: "{name} left on {date}; record the separation with that last working day."
- Build and test with `-nodeReuse:false -p:UseSharedCompilation=false`. For migrations, set `DataProtection__KeyEncryptionKey=design-time-only-not-a-secret-0123456789` and `ASPNETCORE_ENVIRONMENT=Development`.
- Commit messages end with a blank line, then `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/PeopleCore.Domain/Enums/PayrollEnums.cs` (modify) | `PayrollRunType` |
| `src/PeopleCore.Domain/Entities/Payroll/PayrollRun.cs`, `PayrollRunEmployee.cs` (modify) | Run type; final-pay earnings columns; `GrossPay` |
| `src/PeopleCore.Domain/Entities/Payroll/FinalPayInputs.cs` (new) | Per-run inputs: separation, working days, overrides, extra deductions |
| `src/PeopleCore.Domain/Entities/Leave/LeaveType.cs` (modify) | Two conversion settings |
| `src/PeopleCore.Domain/Entities/Employees/Separation.cs` (modify) | `FinalPayRunId`; clearance-undo fields |
| `src/PeopleCore.Application/Payroll/FinalPay/FinalPayMath.cs` (new) | Pure: service years, separation pay, retirement pay, leave split |
| `src/PeopleCore.Application/Payroll/Services/PayrollComputationService.cs` (modify) | `FinalPayExtras` argument |
| `src/PeopleCore.Application/Payroll/Services/Bir2316Service.cs` (modify) | Final-pay earnings in the 2316; build with a draft entry |
| `src/PeopleCore.Application/Payroll/GovernmentReports/GovernmentReportService.cs` (modify) | 1601-C non-taxable line takes final-pay non-taxable |
| `src/PeopleCore.Application/Payroll/FinalPay/FinalPayService.cs` (new) + interface + DTOs | Create, update, recompute; summary |
| `src/PeopleCore.Application/Payroll/Services/PayrollRunService.cs` (modify) | Delegate Final-pay recompute; Mark Paid clearance gate; run type in DTOs |
| `src/PeopleCore.Application/Employees/Services/SeparationService.cs` (modify) | Overdue with Paid check; clearance locking; undo recorded; separations for already-inactive employees |
| `src/PeopleCore.API/Controllers/Payroll/FinalPayController.cs` (new) | Final-pay endpoints |
| Leave type API DTO/service (modify) | The two settings |
| Web: `SeparationDetail.razor`, `Separations.razor`, `PayrollRunDetail.razor`, `PayrollRuns.razor`, `ApiClient.cs` (modify) | Final-pay section, status, run display |

---

### Task 1: Storage for final pay

**Files:** the Domain entities above; configurations in `src/PeopleCore.Infrastructure/Persistence/Configurations/{Payroll,Leave,Employees}`; `AppDbContext` (DbSets for `FinalPayInputs` and `FinalPayDeduction`); migration `AddFinalPay`; `PayrollRunRepository.GetWithEntriesAsync` (also load the run's `FinalPayInputs` with deductions); test `tests/PeopleCore.Infrastructure.Tests/Payroll/FinalPayStorageTests.cs`.

**Interfaces (Produces):**
- `enum PayrollRunType { Regular, FinalPay }`. Add `PayrollRun.RunType` (default Regular, stored as string, max length 16) and `PayrollRun.FinalPayInputs` (a nullable one-to-one navigation).
- `PayrollRunEmployee` gains `decimal LeaveConversionPay`, `decimal LeaveConversionNonTaxable`, `decimal SeparationPay`, `decimal RetirementPay` and `decimal FinalPayNonTaxable`. `LeaveConversionNonTaxable` is the de minimis part of the leave conversion; `FinalPayNonTaxable` is the non-taxable part of leave conversion, separation pay and retirement pay combined, so it includes `LeaveConversionNonTaxable`. All are `numeric(18,2)`, default 0.
  - Update `GrossPay` to add `LeaveConversionPay + SeparationPay + RetirementPay`.
  - Add a computed `FinalPayTaxable => LeaveConversionPay + SeparationPay + RetirementPay - FinalPayNonTaxable`, ignored in EF.
- `FinalPayInputs : AuditableEntity` has:
  - `Guid PayrollRunId` (unique) and `Guid SeparationId`;
  - `decimal WorkingDays` (`numeric(6,2)`);
  - `decimal? SeparationPayOverride`, `decimal? RetirementPayOverride` and `string? OverrideNote` (max 500);
  - `List<FinalPayDeduction> Deductions`.
- `FinalPayDeduction : AuditableEntity` has `Guid FinalPayInputsId`, `string Label` (max 200) and `decimal Amount`. It cascades from `FinalPayInputs`, which cascades from the run.
- `LeaveType` gains `bool IsConvertibleToCash` (default false) and `bool CountsAsVacationForDeMinimis` (default true).
- `Separation` gains `Guid? FinalPayRunId` (FK to `payroll_runs`, SetNull on delete) and a `PayrollRun? FinalPayRun` navigation. `SeparationRepository.WithDetails()` includes it.
- `SeparationClearanceItem` gains `string? LastUndoneBy`, `DateTime? LastUndoneAt` and `string? LastUndoneNote`.

- [ ] **Step 1:** Write failing Postgres tests.
  - A FinalPay run with an entry carrying the four new amounts, plus `FinalPayInputs` with two deductions, saves and reloads through `GetWithEntriesAsync` with everything intact.
  - `GrossPay` includes the new earnings.
  - `Separation.FinalPayRunId` round-trips and `GetAsync` loads `FinalPayRun`.
  - A LeaveType's two flags round-trip.
  - Existing runs read back as `RunType == Regular`. Insert one without setting it.
- [ ] **Step 2:** Run them and confirm they fail.
- [ ] **Step 3:** Implement the entities and configurations. Add `builder.Ignore(x => x.FinalPayTaxable)` in `PayrollRunEmployeeConfiguration`, which already ignores `GrossPay` and the other computed properties.
- [ ] **Step 4:** Generate the migration (command below). Read `Up`: it adds the columns (defaults: RunType `'Regular'`, the flags false/true, the amounts 0), creates `final_pay_inputs` and `final_pay_deductions`, and adds the separation FK. Nothing else.

  Run: `DataProtection__KeyEncryptionKey=design-time-only-not-a-secret-0123456789 ASPNETCORE_ENVIRONMENT=Development dotnet ef migrations add AddFinalPay --project src/PeopleCore.Infrastructure --startup-project src/PeopleCore.API`
- [ ] **Step 5:** Run the tests, then the whole solution once. Existing tests must pass unchanged. If a test pins `GrossPay` for an entry with zero new amounts, it still passes.
- [ ] **Step 6:** Commit: `feat(payroll): store final-pay runs, their inputs and earnings, and leave conversion settings`

---

### Task 2: Final-pay arithmetic

**Files:** create `src/PeopleCore.Application/Payroll/FinalPay/FinalPayMath.cs`; test `tests/PeopleCore.Application.Tests/Payroll/FinalPayMathTests.cs`.

**Interfaces (Produces):**
- `static int ServiceYears(DateOnly hireDate, DateOnly lastWorkingDay)`
- `static decimal SeparationPay(AuthorizedCause cause, decimal monthlyBasic, int serviceYears)`
- `static bool IsRetirementEligible(DateOnly dateOfBirth, DateOnly lastWorkingDay, int serviceYears)`
- `static decimal RetirementPay(decimal dailyRate, int serviceYears)`
- `static (decimal NonTaxable, decimal Taxable) LeaveConversion(IEnumerable<(decimal Days, bool CountsAsVacation)> balances, decimal dailyRate)`
- `const decimal DeMinimisVacationDays = 10m`, `const decimal RetirementDaysPerYear = 22.5m`

- [ ] **Step 1: Write the failing tests**

```csharp
using FluentAssertions;
using PeopleCore.Application.Payroll.FinalPay;
using PeopleCore.Domain.Enums;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class FinalPayMathTests
{
    [Theory]
    [InlineData("2021-03-01", "2026-02-28", 5)]   // 4 years 11 months 27 days: the fraction is 6+ months, so 5
    [InlineData("2021-03-01", "2025-08-31", 4)]   // 4 years 5 months 30 days: under 6 months, so 4
    [InlineData("2021-03-01", "2025-09-01", 5)]   // 4 years 6 months exactly: counts
    [InlineData("2026-01-05", "2026-03-31", 0)]   // under 6 months of service
    [InlineData("2025-10-01", "2026-04-01", 1)]   // 6 months exactly
    public void ServiceYears_RoundsASixMonthFractionUpToAWholeYear(string hired, string lastDay, int expected)
    {
        FinalPayMath.ServiceYears(DateOnly.Parse(hired), DateOnly.Parse(lastDay)).Should().Be(expected);
    }

    [Theory]
    [InlineData(AuthorizedCause.Redundancy, 5, 150_000)]              // one month per year
    [InlineData(AuthorizedCause.LaborSavingDevices, 5, 150_000)]
    [InlineData(AuthorizedCause.Retrenchment, 5, 75_000)]             // half a month per year
    [InlineData(AuthorizedCause.ClosureNotDueToLosses, 5, 75_000)]
    [InlineData(AuthorizedCause.Disease, 5, 75_000)]
    [InlineData(AuthorizedCause.Retrenchment, 1, 30_000)]             // half of one month is below the one-month minimum
    [InlineData(AuthorizedCause.Redundancy, 0, 30_000)]               // under six months still gets the minimum
    [InlineData(AuthorizedCause.ClosureDueToSeriousLosses, 5, 0)]     // no separation pay
    public void SeparationPay_FollowsArticles298And299(AuthorizedCause cause, int years, decimal expected)
    {
        FinalPayMath.SeparationPay(cause, monthlyBasic: 30_000m, years).Should().Be(expected);
    }

    [Theory]
    [InlineData("1966-03-01", "2026-03-01", 5, true)]    // turns 60 on the last day
    [InlineData("1966-03-02", "2026-03-01", 5, false)]   // 59 on the last day
    [InlineData("1961-03-01", "2026-03-01", 5, true)]    // exactly 65 on the last day
    [InlineData("1960-03-01", "2026-03-01", 5, false)]   // 66 on the last day, past RA 7641's 65
    [InlineData("1964-01-01", "2026-03-01", 4, false)]   // under 5 years of service
    public void RetirementEligibility_Needs60To65AndFiveYears(string born, string lastDay, int years, bool expected)
    {
        FinalPayMath.IsRetirementEligible(DateOnly.Parse(born), DateOnly.Parse(lastDay), years).Should().Be(expected);
    }

    [Fact]
    public void RetirementPay_Is22AndAHalfDaysPerYear()
    {
        FinalPayMath.RetirementPay(dailyRate: 1_200m, serviceYears: 10).Should().Be(270_000m);   // 1,200 × 22.5 × 10
    }

    [Fact]
    public void LeaveConversion_TreatsTheFirstTenVacationDaysAsDeMinimis()
    {
        var (nonTaxable, taxable) = FinalPayMath.LeaveConversion(
            [(8m, true), (5m, true), (3m, false)], dailyRate: 1_000m);

        nonTaxable.Should().Be(10_000m, "10 of the 13 vacation-type days");
        taxable.Should().Be(6_000m, "the other 3 vacation days, plus 3 days of a type that doesn't count as vacation");
    }

    [Fact]
    public void LeaveConversion_IgnoresNegativeBalances()
    {
        FinalPayMath.LeaveConversion([(-2m, true), (4m, true)], 1_000m).Should().Be((4_000m, 0m));
    }
}
```

Age is counted in completed years; RA 7641 sets retirement at 60 to 65.

- [ ] **Step 2:** Run the tests and confirm they fail.
- [ ] **Step 3: Implement**

```csharp
using PeopleCore.Domain.Enums;

namespace PeopleCore.Application.Payroll.FinalPay;

/// <summary>The statutory final-pay figures, with no data access.</summary>
public static class FinalPayMath
{
    /// <summary>RR 11-2018 (as amended): monetized unused vacation leave up to 10 days a year is de minimis.</summary>
    public const decimal DeMinimisVacationDays = 10m;

    /// <summary>RA 7641: 15 days' pay + 5 days of SIL + 1/12 of the 13th month (2.5 days).</summary>
    public const decimal RetirementDaysPerYear = 22.5m;

    /// <summary>Whole years from hire to the last working day; a fraction of at least six months counts as a year.</summary>
    public static int ServiceYears(DateOnly hireDate, DateOnly lastWorkingDay)
    {
        if (lastWorkingDay < hireDate) return 0;
        int years = lastWorkingDay.Year - hireDate.Year;
        if (hireDate.AddYears(years) > lastWorkingDay) years--;
        var afterWholeYears = hireDate.AddYears(years);
        return afterWholeYears.AddMonths(6) <= lastWorkingDay ? years + 1 : years;
    }

    /// <summary>
    /// Labor Code Art. 298-299: a month's pay per year of service for redundancy and labor-saving
    /// devices, half a month for retrenchment, closure not due to losses and disease, never less
    /// than a month's pay; nothing for closure due to serious losses.
    /// </summary>
    public static decimal SeparationPay(AuthorizedCause cause, decimal monthlyBasic, int serviceYears)
    {
        decimal perYear = cause switch
        {
            AuthorizedCause.Redundancy or AuthorizedCause.LaborSavingDevices => 1m,
            AuthorizedCause.Retrenchment or AuthorizedCause.ClosureNotDueToLosses or AuthorizedCause.Disease => 0.5m,
            _ => 0m
        };
        if (perYear == 0m) return 0m;
        return Math.Round(Math.Max(monthlyBasic, monthlyBasic * perYear * serviceYears), 2);
    }

    /// <summary>RA 7641: 60 to 65 years old and at least five years of service.</summary>
    public static bool IsRetirementEligible(DateOnly dateOfBirth, DateOnly lastWorkingDay, int serviceYears)
    {
        int age = lastWorkingDay.Year - dateOfBirth.Year;
        if (dateOfBirth.AddYears(age) > lastWorkingDay) age--;
        return age is >= 60 and <= 65 && serviceYears >= 5;
    }

    public static decimal RetirementPay(decimal dailyRate, int serviceYears)
        => Math.Round(dailyRate * RetirementDaysPerYear * serviceYears, 2);

    /// <summary>
    /// Unused convertible leave at the daily rate, split into the de minimis part (the first ten
    /// vacation-type days) and the taxable rest.
    /// </summary>
    public static (decimal NonTaxable, decimal Taxable) LeaveConversion(
        IEnumerable<(decimal Days, bool CountsAsVacation)> balances, decimal dailyRate)
    {
        var positive = balances.Where(b => b.Days > 0m).ToList();
        decimal vacationDays = positive.Where(b => b.CountsAsVacation).Sum(b => b.Days);
        decimal otherDays = positive.Where(b => !b.CountsAsVacation).Sum(b => b.Days);
        decimal deMinimisDays = Math.Min(vacationDays, DeMinimisVacationDays);
        return (Math.Round(deMinimisDays * dailyRate, 2),
                Math.Round((vacationDays - deMinimisDays + otherDays) * dailyRate, 2));
    }
}
```

- [ ] **Step 4:** Run the tests and confirm they pass.
- [ ] **Step 5:** Commit: `feat(payroll): the statutory final-pay figures - service years, separation and retirement pay, leave conversion`

---

### Task 3: The engine takes final-pay extras

**Files:**
- Modify: `src/PeopleCore.Application/Payroll/Services/PayrollComputationService.cs`
- Create: `src/PeopleCore.Application/Payroll/FinalPay/FinalPayExtras.cs`
- Test: `tests/PeopleCore.Application.Tests/Payroll/PayrollComputationServiceFinalPayTests.cs`

**Interfaces:**
- Produces the record:

```csharp
public sealed record FinalPayExtras(
    decimal WorkingDays,
    decimal LeaveConversionNonTaxable,
    decimal LeaveConversionTaxable,
    decimal SeparationPay,
    decimal RetirementPay,
    decimal SeparationAndRetirementNonTaxable,
    IReadOnlyList<(string Label, decimal Amount)> Deductions,
    decimal? WithholdingTaxOverride);
```

- `Compute(...)` gains a last optional parameter `FinalPayExtras? finalPay = null`. With it:
  - **base period pay** = `Math.Round(dailyRate × finalPay.WorkingDays, 2)` instead of the salary share;
  - **LeaveConversionPay** = the sum of both parts; **LeaveConversionNonTaxable** is set; **SeparationPay** and **RetirementPay** are set; **FinalPayNonTaxable** = `LeaveConversionNonTaxable + SeparationAndRetirementNonTaxable`;
  - **the taxable part** (`LeaveConversionTaxable` plus the taxable part of separation/retirement) joins the withholding base, the same way taxable allowances do;
  - **loans:** each active loan's `RemainingBalance` is deducted instead of its instalment, with the existing cap and pro-rating;
  - **HR deductions** go into `OtherDeductions`, capped by what's left after the loans. The existing `customDeductions` path already caps; feed their sum into it;
  - **tax:** when `WithholdingTaxOverride` has a value, `WithholdingTax` is that value, which may be negative. It replaces the per-period calculation and the 13th-month excess tax. Without the argument, nothing changes.

- [ ] **Step 1:** Write failing tests. Use a 36,500 monthly salary (daily rate 1,200 under the 365 factor):
  - WorkingDays 8 gives RegularPay 9,600;
  - the leave, separation and retirement fields and FinalPayNonTaxable are set from the extras, and GrossPay includes them;
  - two loans with balances 5,000 and 3,000 (instalments 1,000 each) are deducted in full when net pay covers them;
  - a small final pay caps the loans, pro-rated;
  - HR deductions of 1,500 appear in OtherDeductions after the loans;
  - `WithholdingTaxOverride = -2_000` gives WithholdingTax -2,000, and NetPay rises by 2,000 relative to a zero override;
  - the taxable leave conversion raises the withholding base when there is no override. Compare against the same compute without it.
  - Existing tests must pass untouched.
- [ ] **Step 2:** Run them and confirm they fail.
- [ ] **Step 3:** Implement with the smallest change to `Compute`. Each branch on `finalPay is not null` gets a one-line comment on why.
- [ ] **Step 4:** Run the tests and the whole Application project.
- [ ] **Step 5:** Commit: `feat(payroll): the payroll engine computes a final period, its extra earnings, full loan balances and a settled tax`

---

### Task 4: The 2316 and the 1601-C take final pay

**Files:**
- Modify: `src/PeopleCore.Application/Payroll/Services/Bir2316Service.cs` (and `IBir2316Service`), `src/PeopleCore.Application/Payroll/GovernmentReports/GovernmentReportService.cs`
- Test: `Bir2316ServiceTests.cs`, `GovernmentReportServiceTests.cs`

**Interfaces:**
- In `BuildDto`:
  - **Non-taxable:** `Item35_DeMinimis` = the manual de minimis + the sum of `LeaveConversionNonTaxable`. `Item37_SalariesOtherForms` also adds the sum of `FinalPayNonTaxable − LeaveConversionNonTaxable` (the non-taxable separation or retirement pay).
  - **Taxable:** `Item51B_OtherAmount` = the sum of `FinalPayTaxable`, with `Item51B_OtherLabel` "Final pay (leave conversion, separation/retirement pay)", only when non-zero.
- `Task<Bir2316Dto?> BuildWithDraftEntryAsync(Guid employeeId, int year, PayrollRun draftRun, PayrollRunEmployee draftEntry, CancellationToken ct = default)`: the same as BuildAsync with the saved inputs, but the draft run and entry are added to the Paid runs it sums. It is used by Task 5 to settle tax.
- In the 1601-C:
  - the "Other non-taxable compensation" line and the per-employee "Other non-taxable" column add `FinalPayNonTaxable − LeaveConversionNonTaxable`;
  - "De minimis benefits" becomes the sum of `LeaveConversionNonTaxable` (today it's 0);
  - taxable compensation therefore excludes them.
  - A test pins that the lines still add up.

- [ ] **Step 1:** Write failing tests.
  - A 2316 over a Paid regular run plus a Paid final-pay entry with leave conversion (4,000 non-taxable, 2,000 taxable) and separation pay 150,000 (non-taxable):
    - Item35 includes 4,000;
    - Item37 includes 150,000;
    - Item51B is 2,000 with the label;
    - Item19 = Item38 + Item52.
  - A balancing test: with the final-pay entry's WithholdingTax set to Item24 − Item25A(other runs) − Item25B − Item27 (computed through `BuildWithDraftEntryAsync`), the resulting 2316 has `Item24_TaxDue == Item26_TotalTaxWithheld + Item27_PeraTaxCredit`.
  - A 1601-C for the month the final pay was paid shows the de minimis and other non-taxable lines, and taxable excludes them.
- [ ] **Step 2:** Run them and confirm they fail.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the tests and the whole solution.
- [ ] **Step 5:** Commit: `feat(payroll): final pay's earnings land in the 2316 and 1601-C, and a 2316 can include a draft entry`

---

### Task 5: Creating and recomputing a final-pay run

**Files:**
- Create: `src/PeopleCore.Application/Payroll/FinalPay/{IFinalPayService.cs, FinalPayService.cs, FinalPayDtos.cs}`
- Modify: `PayrollRunService.cs` (`ComputeAsync` delegates FinalPay runs; the run DTOs carry `RunType`; `CountForYearAsync` gets a run-type overload, or a new `CountFinalPayForYearAsync`), `IPayrollRunRepository` and its implementation, `ServiceExtensions.cs`
- Test: `tests/PeopleCore.Application.Tests/Payroll/FinalPayServiceTests.cs`, one Postgres round-trip test in `tests/PeopleCore.Infrastructure.Tests/Payroll/FinalPayServiceDbTests.cs`

**Interfaces:**
- Consumes: Tasks 1-4; `ISeparationRepository`; `IEmployeeCompensationRepository`; `IEmployeeLoanRepository`; `ILeaveBalanceRepository.GetByEmployeeAsync(employeeId, year)` (includes LeaveType); `IShiftService.ResolveShiftForDayAsync`; `IBir2316Service.BuildWithDraftEntryAsync`; `IPayrollRunRepository`; `PayrollComputationService`; `TimeProvider`.
- Produces:
  - `FinalPayRequest(DateOnly PayDate, DateOnly? PeriodStart, decimal? SeparationPayOverride, decimal? RetirementPayOverride, string? OverrideNote, IReadOnlyList<FinalPayDeductionDto> Deductions)`;
  - `FinalPayDeductionDto(string Label, decimal Amount)`;
  - `FinalPaySummaryDto(Guid RunId, string RunNumber, PayrollRunStatus Status, DateOnly PeriodStart, DateOnly PeriodEnd, DateOnly PayDate, decimal WorkingDays, decimal LeaveConversionPay, decimal LeaveConversionNonTaxable, IReadOnlyList<FinalPayLeaveLineDto> LeaveLines, decimal SeparationPay, decimal RetirementPay, decimal? ComputedSeparationOrRetirementPay, string? OverrideNote, int ServiceYears, IReadOnlyList<FinalPayDeductionDto> Deductions, IReadOnlyList<FinalPayLoanLineDto> Loans, decimal WithholdingTax, decimal GrossPay, decimal NetPay, bool ClearanceComplete, IReadOnlyList<string> OutstandingClearance)`;
  - `FinalPayLeaveLineDto(string LeaveType, decimal Days, bool CountsAsVacation)`;
  - `FinalPayLoanLineDto(string LoanType, decimal Balance, decimal Deducted, decimal Uncovered)`.
  - `IFinalPayService`:
    - `CreateAsync(Guid separationId, FinalPayRequest)` → `FinalPaySummaryDto`;
    - `UpdateAsync(Guid separationId, FinalPayRequest)` → `FinalPaySummaryDto` (Draft or ForApproval only; recomputes);
    - `GetAsync(Guid separationId)` → `FinalPaySummaryDto?`;
    - `RecomputeAsync(PayrollRun run)` → `List<PayrollRunEmployee>` (called by `PayrollRunService.ComputeAsync` for FinalPay runs).

**Rules** (each gets a test; messages exact):
- **Create:**
  - The separation must exist.
  - It must have no final-pay run yet: "{name} already has a final-pay run ({runNumber})."
  - The employee must have a compensation record: "{name} has no compensation record."
  - The pay date is required.
  - The period start defaults as in Global Constraints, and must not be after the last working day: "The final pay period can't start after the last working day."
  - An override needs a note: "Explain the separation or retirement pay override."
  - Deduction labels must be non-blank with a positive amount.
- **Working days (salary days):** as in Global Constraints - every calendar day on the 365 factor; on 313 and 261 the days the employee's shift schedules (rest days excluded), falling back to Monday to Friday where unassigned; 0 when there's no salary left. Stored on `FinalPayInputs`.
- **Separation and retirement pay:**
  - AuthorizedCause: `FinalPayMath.SeparationPay` (non-taxable).
  - Retirement and eligible: `RetirementPay` (non-taxable). Not eligible: 0.
  - An override replaces it, and is non-taxable only in those two non-taxable cases.
  - The summary's `ComputedSeparationOrRetirementPay` always shows the computed figure.
- **Leave:** every balance for the last working day's year whose type `IsConvertibleToCash`, with its `RemainingDays` and `CountsAsVacationForDeMinimis`, goes through `FinalPayMath.LeaveConversion`.
- **Tax:** compute once without an override, build the 2316 with that draft entry (`BuildWithDraftEntryAsync`), set the override to `Item24 − (Item25A − thisEntry.WithholdingTax) − Item25B − Item27`, then compute again with the override. Rounded to 2 decimals.
- **The run:** RunType FinalPay, number `FP-{payYear}-{nnn:D3}`, Frequency = the employee's PayFrequency, one employee, 13th month included. The separation's `FinalPayRunId` is set. Saved in one go.
- **Recompute** (`PayrollRunService.ComputeAsync` on a FinalPay run): rebuild from the stored `FinalPayInputs` and the separation, using the stored working days, and redo the tax settle.
- **Summary loans:** the balance of each loan (from the loan repository), what the entry deducted, and the uncovered difference.

- [ ] **Step 1:** Write failing unit tests for each rule, with mocks.
  - Pin one full worked example with literal numbers:
    - employee: 36,500 monthly, hired 2021-03-01, Redundancy with last day 2026-03-13, no attendance;
    - pay year: one Paid regular run earlier, with RegularPay 36,500 and WithholdingTax 2,000;
    - leave: VL 5 days, convertible;
    - loans: one of 3,000;
    - pay date 2026-03-31.

    Assert each of WorkingDays, RegularPay, ThirteenthMonth, LeaveConversionPay, SeparationPay, WithholdingTax and NetPay, with the arithmetic in comments. Work each figure out by hand from the rules and the existing engine, and show the working in the test.
  - Add one Postgres test that creates a final-pay run through the real repositories and reads it back with its inputs and the separation link.
- [ ] **Step 2:** Run them and confirm they fail.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the tests and the whole solution.
- [ ] **Step 5:** Commit: `feat(payroll): create and recompute a final-pay run from a separation`

---

### Task 6: Paying final pay, and the plan-1 carry-overs

**Files:** modify `PayrollRunService.MarkPaidAsync`, `SeparationService`, `SeparationDtos.cs` (plus the Web mirror in Task 8); tests in `PayrollRunServiceTests`, `SeparationServiceTests`, and one Postgres test.

**Rules** (each tested):
- **Mark Paid:** for a FinalPay run, load its separation and refuse unless clearance is complete, with "Clear {item}, {item} before paying final pay." (outstanding item names in order, joined with ", ").
- **SeparationDto** gains `Guid? FinalPayRunId`, `string? FinalPayRunNumber` and `PayrollRunStatus? FinalPayStatus`. `FinalPayOverdue` uses the Paid check.
- **`EnsureClearanceEditable`** refuses once the final-pay run is Paid: "Final pay has been paid; clearance can't change now."
- **Undo** sets `LastUndoneBy`, `LastUndoneAt` and `LastUndoneNote` (the note being cleared). `ClearanceItemDto` gains `LastUndoneBy` and `LastUndoneAt`.
- **Separations for already-inactive employees:** `RecordAsync` for an inactive employee who has a `SeparationDate` and no separation creates it directly as Separated, with `SeparatedBy`/`SeparatedAt` set and `LastWorkingDay` equal to the `SeparationDate`. A mismatch gives: "{name} left on {Mar 3, 2026}; record the separation with that last working day." An inactive employee without a `SeparationDate` keeps the existing "{name} is no longer active." error.
- **Cancel** is refused when a final-pay run exists: "Final pay has already been started for this separation."

- [ ] Steps: failing tests → run and confirm they fail → implement → run the tests and the whole solution → commit `feat(payroll): final pay waits for clearance, and separations track it`.

---

### Task 7: Final-pay endpoints and leave-type settings

**Files:**
- Create: `src/PeopleCore.API/Controllers/Payroll/FinalPayController.cs`, route `api/separations/{separationId:guid}/final-pay`, `[RequirePermission(Permissions.PayrollManage)]`:
  - `GET` → summary or 404;
  - `POST` (FinalPayRequest) → 201;
  - `PUT` (FinalPayRequest) → 200.
- Modify: the leave-type DTOs and service (find them under `src/PeopleCore.Application/Leave`) to read and write `IsConvertibleToCash` and `CountsAsVacationForDeMinimis`.
- Modify: `tools/PeopleCore.DemoSeed`, where the demo's leave types are created (VL/SL). VL is convertible and counts as vacation; SL is neither.
- Update `PermissionEquivalenceTests` for the new routes.
- Tests: controller tests (permission by reflection, results); leave-type DTO round-trip; demo seed tests if they pin the leave-type request body.

- [ ] Steps: failing tests → run and confirm they fail → implement → run the whole solution → commit `feat(api): final-pay endpoints, and leave types say whether they convert to cash`.

---

### Task 8: The pages

**Files:** modify `src/PeopleCore.Web/Services/ApiClient.cs` (the final-pay DTOs and calls; SeparationDto's new fields; `RunType` on the run DTOs), `Pages/HR/SeparationDetail.razor`, `Pages/HR/Separations.razor`, `Pages/Payroll/PayrollRuns.razor`, `Pages/Payroll/PayrollRunDetail.razor`; tests in `tests/PeopleCore.Web.Tests`.

**Behavior** (each tested):
- **Separation detail, Final pay section** (`data-final-pay`), shown when the separation exists and the user has `payroll.manage`:
  - Without a run: a "Create final pay" form (`data-final-pay-form`) with the pay date (default today), an optional period start, the override amount and note, and extra deductions (add or remove label/amount rows). Submitting posts, then shows the summary.
  - With a run:
    - a summary: run number, status, period, working days, the leave lines, separation or retirement pay (with the computed figure when overridden, and the note), deductions, loans with any uncovered balance as a warning (`data-loan-shortfall`), tax (labelled "Tax refund" when negative), gross and net;
    - a link to the run (`/payroll-runs/{id}`);
    - an "Edit" form while Draft or ForApproval (PUT);
    - "Clearance outstanding: {items}" (`data-clearance-outstanding`) while clearance isn't complete.
  - API errors show in `data-final-pay-error`.
- **Separations list:** a Final pay column showing the run number and status, or "Not started". Overdue still uses `FinalPayOverdue`.
- **Record separation form:** the employee picker also lists inactive employees who have no separation, labelled "(left {date})". Choosing one pre-fills the last working day with their separation date.
- **Payroll runs list:** a "Final pay" badge on FinalPay runs.
- **Run detail:** for a FinalPay run, show the leave conversion, separation and retirement pay, and the final-pay non-taxable amount per employee, and label a negative tax as a refund. Mark Paid errors, such as the clearance message, show as they do today.
- **Clearance item:** "Undone by {who} on {date}" when `LastUndoneAt` is set.

- [ ] Steps: failing bUnit tests → run and confirm they fail → implement → run the Web tests and the whole solution → commit `feat(web): create, review and pay final pay from the separation page`.

---

### Task 9: Verification

- [ ] Run `dotnet test PeopleCore.slnx -nodeReuse:false -p:UseSharedCompilation=false`. Expected: every project passes, with no compiler warnings.
- [ ] Read the `AddFinalPay` migration's `Up`: only the columns, tables and foreign key named in Task 1.
- [ ] Browser check: needs a signed-in payroll account; record it as not done if no one can sign in.
