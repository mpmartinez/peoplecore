# Unworked Double Holiday Pay Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An unworked double regular holiday is paid 200% in total, the salary's 100% plus one extra day.

**Architecture:** The premium-day input and its stored row gain an `UnworkedDays` figure. The engine prices it from the day type's unworked rate, and the attendance bridge counts the days. Everything downstream already reads `holidayPay`.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core (Npgsql, snake_case), xUnit, FluentAssertions, Moq, Postgres test fixture.

**Spec:** `docs/superpowers/specs/2026-10-01-unworked-double-holiday-pay-design.md`. It holds the exact rules and cases and binds every task.

## Global Constraints

- Extra pay per unworked double regular holiday: `dailyRate x (2.00 - alreadyPaid)`, where `alreadyPaid` is 1.00, except 0 on a rest day type when `PaysRestDays(factor)` is false (the rule worked days already use in `PayrollComputationService`). Rounded with `holidayPay`.
- Only `DoubleRegularHoliday` and `DoubleRegularHolidayOnRestDay` carry an unworked rate (2.00); every other day type adds nothing for `UnworkedDays`.
- The bridge counts a date as an unworked double holiday when the day type is one of those two, the shift schedule resolves (a working day or a rest day), and the date was not worked. A date is worked when, on a rest day, approved overtime exists for it (a present record alone does not count), and when, on a working day, any record for it is marked present. Paid leave that day does not stop the count.
- Unchanged: an unworked double special non-working day (0%, an absence as now), an unworked single regular holiday, every worked-day rate, `HolidayDays`, gross-to-net structure, the payslip and run page.
- `PayrollRunPremiumDay.UnworkedDays` is `numeric(18,2)` not null default 0; one migration adds only that column. A premium-day row is kept when `UnworkedDays` is its only non-zero figure.
- Build/test: `MSBUILDDISABLENODEREUSE=1 dotnet test PeopleCore.slnx -nodeReuse:false -p:UseSharedCompilation=false`. Migrations need `DataProtection__KeyEncryptionKey=design-time-only-not-a-secret-0123456789` and `ASPNETCORE_ENVIRONMENT=Development`.
- Commit messages end with a blank line, then `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.

---

### Task 1: Price and store unworked days

**Files:** `src/PeopleCore.Domain/Payroll/DolePremiumRates.cs` (a `UnworkedBaseRate(WorkDayType)`: 2.00 for the two double regular types, 0 otherwise), `src/PeopleCore.Application/Payroll/DTOs/PayrollAttendanceInput.cs` (`PremiumDayInput` gains a trailing `decimal UnworkedDays = 0m`; the row-kept filter in `FromTotals` and wherever else a premium day is dropped when empty includes it), `PayrollComputationService.cs` (the premium loop adds `dailyRate x (UnworkedBaseRate(day.DayType) - alreadyPaid) x day.UnworkedDays` to `holidayPay` only when `UnworkedBaseRate` is above 0, and the stored `PayrollRunPremiumDay` rows copy it), `src/PeopleCore.Domain/Entities/Payroll/PayrollRunPremiumDay.cs` (`UnworkedDays`), its EF configuration, `PayrollRunService.cs` (the entry-to-input mapping near line 1095 and `Merge` near line 1055 carry it), migration `AddPremiumDayUnworkedDays`.

- [ ] Failing tests with hand-derived figures in comments: one unworked double regular holiday under the 365 factor adds 100% of the daily rate; on a rest day under 365 (100%) and under 313 (200%); two unworked days; mixed with a worked double holiday on the same day type; gross and the withholding base include it and the 13th month (worked from regular pay) does not; a day type with no unworked rate (`SpecialNonWorking`, `RegularHoliday`) given `UnworkedDays` adds nothing; no unworked days leaves every figure unchanged; `UnworkedBaseRate` for every `WorkDayType`. A Postgres test that the column round-trips and a recompute of an unpaid run reprices the unworked days from the stored rows. A row with only `UnworkedDays` is kept.
- [ ] Run and confirm they fail; implement; run the whole solution. The migration's `Up` adds only the one column, `not null default 0`.
- [ ] Commit `feat(payroll): price and store unworked days on a premium-day row`.

---

### Task 2: The bridge counts them

**Files:** `src/PeopleCore.Application/Payroll/Services/PayrollAttendanceBridge.cs` (in the per-date loop, add `Add(dayType, unworkedDays: 1m)` for a qualifying date; `Add` gains an `unworkedDays` parameter and the `with` copy, the breakdown filter and rounding keep it), `tests/PeopleCore.Application.Tests/.../PayrollAttendanceBridge*Tests.cs` (the existing bridge test file).

- [ ] Failing tests: an unworked double regular holiday on a working day; the same on a rest day with no overtime (counted) and with approved overtime (worked as now, not counted); worked (present) is unchanged and not counted; an unworked double special day (nothing, still an absence); an unworked single regular holiday (nothing); no schedule that date (nothing); one present record among several (worked); approved paid leave that date (still counted); the payroll run created from a bridge result pays the extra day end to end.
- [ ] Run and confirm they fail; implement; run the whole solution.
- [ ] Commit `feat(payroll): an unworked double regular holiday is paid 200%`.

---

### Task 3: Verification

- [ ] The whole solution passes with no compiler warnings.
- [ ] `AddPremiumDayUnworkedDays`'s `Up` adds only the one column.
- [ ] Browser check needs a signed-in account; record it as not done if no one can sign in.
