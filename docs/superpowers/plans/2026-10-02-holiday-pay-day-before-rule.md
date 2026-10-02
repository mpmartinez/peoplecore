# Day-Before Rule for Holiday Pay Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A regular holiday the employee did not work is not paid when the employee was absent without pay on the workday before it.

**Architecture:** The attendance bridge loads a 14-day look-back, walks back from each unworked regular holiday to the qualifying workday, and books an absence (and drops a double holiday's guaranteed day) when it was missed. No engine, storage or migration change.

**Tech Stack:** .NET 10, xUnit, FluentAssertions, Moq.

**Spec:** `docs/superpowers/specs/2026-10-02-holiday-pay-day-before-rule-design.md` holds the exact rules and cases and binds the task.

## Global Constraints

- Walk back from the holiday over rest days, dates the schedule does not cover, and holiday dates (any calendar holiday except special working days); a worked holiday in the walk, or a present record or approved paid leave on the first scheduled working day, satisfies the rule; approved unpaid leave or no record is absent without pay; stop after 14 days and then stay entitled.
- Not entitled: `AbsenceDays` +1 for the holiday and no `UnworkedDays` for a double regular holiday. Entitled or worked: unchanged.
- Records, leave and assignments before the period start are used only by this check; they must not change late, undertime, night, overtime, absence or premium figures for the period.
- Unchanged: the engine, storage, DTOs, migrations, special days, rest-day holidays, every other premium.
- Build/test: `MSBUILDDISABLENODEREUSE=1 dotnet test PeopleCore.slnx -nodeReuse:false -p:UseSharedCompilation=false`.
- Commit messages end with a blank line, then `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.

---

### Task 1: The look-back and the deduction

**Files:** `src/PeopleCore.Application/Payroll/Services/PayrollAttendanceBridge.cs` (load from 14 days before the period; the check in the per-date loop; the absence guard that excludes unworked regular holidays), `tests/PeopleCore.Application.Tests/Payroll/PayrollAttendanceBridgeTests.cs` and a run-level test beside `PayrollRunServiceUnworkedHolidayTests.cs`.

- [ ] Failing tests per the spec's Testing section, with the weekday of every date checked in comments.
- [ ] Run and confirm they fail; implement; run the whole solution.
- [ ] Commit `feat(payroll): a regular holiday is not paid after an unpaid absence the day before`.

### Task 2: Verification

- [ ] The whole solution passes with no compiler warnings; no migration was added.
