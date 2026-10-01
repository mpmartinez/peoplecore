# Rest-Day Double Holiday Hours Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Working a rest-day double regular holiday never pays less than not working it: 200% guaranteed plus the work premium on the hours.

**Architecture:** The bridge counts the guaranteed day for every rest-day double regular holiday; the engine prices hours of a day type with an unworked rate as the premium over that guarantee. No new column or migration.

**Tech Stack:** .NET 10, xUnit, FluentAssertions, Moq.

**Spec:** `docs/superpowers/specs/2026-10-02-rest-day-double-holiday-hours-design.md` (rules, cases, the accepted gap); it binds the task.

## Global Constraints

- Rest-day double regular holiday with a resolved schedule: `UnworkedDays` 1 always. Working-day double holiday: counted only when no record for the date is marked present.
- Engine hours on a day type with `DolePremiumRates.UnworkedBaseRate(dayType) > 0`: `hourlyRate x (BaseRate - UnworkedBaseRate) x Hours`; guaranteed day `dailyRate x (UnworkedBaseRate - alreadyPaid) x UnworkedDays`; every other day type's hours `BaseRate - alreadyPaid`.
- Unchanged: overtime past eight hours, night differential, working-day double holidays' `Days`, single holidays, special days, storage, migrations, DTOs.
- Build/test: `MSBUILDDISABLENODEREUSE=1 dotnet test PeopleCore.slnx -nodeReuse:false -p:UseSharedCompilation=false`.
- Commit messages end with a blank line, then `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.

---

### Task 1: Guarantee plus premium

**Files:** `src/PeopleCore.Application/Payroll/Services/PayrollAttendanceBridge.cs` (the unworked-day block, with `workedTheDay`), `src/PeopleCore.Application/Payroll/Services/PayrollComputationService.cs` (the premium loop's hours term), their tests (`PayrollAttendanceBridgeTests.cs`, `PayrollComputationServiceTests.cs`, `PayrollRunServiceUnworkedHolidayTests.cs`), and the earlier spec `2026-10-01-unworked-double-holiday-pay-design.md` and plan's "The attendance bridge" wording so they say the rest-day rule is now the 2026-10-02 spec's.

- [ ] Failing tests per the spec's Testing section, with hand-derived figures in comments.
- [ ] Run and confirm they fail; implement; run the whole solution.
- [ ] Commit `fix(payroll): working a rest-day double holiday pays the 200% guarantee plus the work premium`.

### Task 2: Verification

- [ ] The whole solution passes with no compiler warnings; no migration was added.
