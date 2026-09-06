# Attendance Bridge — Phase 2 Design

**Date:** 2026-09-06
**Status:** Approved for planning
**Phase:** 2 of 4 (follows the payroll domain merge, Phase 1)

## Context

Phase 1 moved PayZen's payroll engine into PeopleCore. The engine consumes a
`PayrollAttendanceInput` record — eight per-employee totals for a pay period — but nothing fills
it. Phase 1 left `DaysWorked`, `OvertimeHours` and `HolidayDays` as explicit inputs on
`CreatePayrollRunRequest`, so payroll computes correctly but is keyed in by hand.

PeopleCore already owns the raw material: `AttendanceRecord` punches, approved `LeaveRequest` and
`OvertimeRequest` rows, a `Holiday` calendar, and shift scheduling that can resolve what an
employee was supposed to work on any given day. This phase connects the two.

**Goal:** derive `PayrollAttendanceInput` from PeopleCore's own attendance data, and snapshot it
onto the payroll run so a recompute can never change what someone was paid.

## Decisions taken

Four decisions shape everything below.

**1. Totals are snapshotted onto the run, not recomputed from raw data.**
The bridge runs once, when a payroll run is created. Its output is stored on
`PayrollRunEmployee`. A recompute reads the stored values. This means editing an old punch cannot
retroactively change a run, and it needs no new entity — the run *is* the snapshot. HR correction
becomes editing a run's entries, which the Phase 3 UI can expose.

**2. Absences are schedule-driven, and the system never guesses.**
An absence is a scheduled non-rest day with no attendance record marked present and no **paid**
leave covering it. An employee with no shift assignment yields **zero** absences and is reported in
the run's diagnostics.

Over-deducting wages is a DOLE compliance problem; under-deducting is a recoverable business one.
So where the schedule is unknown, the bridge declines to deduct rather than inventing a work week.

**3. Only approved overtime is paid.**
`OvertimeRequest` rows with `Status = Approved` are the source. `AttendanceRecord.OvertimeMinutes`
stays a punch-derived operational signal and is never paid — staying late without approval creates
no payroll liability, and the existing approval workflow stays meaningful rather than decorative.

**4. Night differential is measured from actual punches.**
`NightDiffHours` is the real overlap between an employee's `TimeIn`–`TimeOut` interval and the
22:00–06:00 window (Article 86). A record with a missing `TimeOut` contributes zero rather than
being guessed.

## Scope

### In

- A `PayrollAttendanceBridge` application service deriving all eight input fields.
- Seven new snapshot columns on `PayrollRunEmployee`, plus one migration.
- `ComputeAsync` rebuilding `PayrollAttendanceInput` from the snapshot on recompute.
- A bulk shift-assignment query and a shared day-resolution helper (see Architecture).
- Diagnostics reporting employees the bridge could not schedule.

### Out

- Phase 3: Blazor payroll UI, including any HR screen for adjusting a run's entries.
- Phase 4: payslip and BIR 2316 PDFs.
- Custom attendance fields (`AttendanceCustomField.AffectsEarnings` / `AffectsDeductions`). PayZen
  had them; Phase 1 deliberately did not port them, and they stay out here. `OtherDeductions`
  therefore remains zero.
- Correcting `PayrollExportService`, whose `BuildSummary` labels punch-derived
  `AttendanceRecord.OvertimeMinutes` as `TotalApprovedOvertimeMinutes` and hardcodes
  `HasNightShift: false`. That endpoint serves an external payroll consumer that no longer exists
  now that payroll is in-process. Removing or fixing it is its own decision, not this phase's.

## The snapshot problem

This is the reason the phase is larger than "write an aggregator".

`PayrollComputationService` writes back onto `PayrollRunEmployee`:

```csharp
OvertimeHours = ordinaryOtHours + restDayOtHours,
HolidayDays   = regularHolidayDays + specialHolidayDays,
```

Both are **lossy**. Rest-day overtime pays at 1.69x and ordinary at 1.25x; regular holidays pay
200% and special non-working days 130%. Collapsed into one number, the split is gone.

Those two columns, plus `DaysWorked` and `IncludeThirteenthMonth`, are all `PayrollRunEmployee`
persists. Snapshotting only them would mean a recompute silently reclassifies rest-day overtime as
ordinary and special holidays as regular — **underpaying**, with no error.

So `PayrollRunEmployee` gains the seven fields it lacks:

| Column | Type |
|---|---|
| `AbsenceDays` | `numeric(6,2)` |
| `LateMinutes` | `numeric(6,2)` |
| `UndertimeMinutes` | `numeric(6,2)` |
| `NightDiffHours` | `numeric(6,2)` |
| `RestDayOTHours` | `numeric(6,2)` |
| `HolidayRegularDays` | `numeric(6,2)` |
| `HolidaySpecialDays` | `numeric(6,2)` |

`OvertimeHours` and `HolidayDays` remain as computed roll-ups for the payslip and register, but
`ComputeAsync` no longer reads them as inputs.

### `OvertimeHours` means two different things — be careful

The name appears on both types with **different meanings**, and conflating them is the most likely
way to introduce a silent pay error:

| Where | Meaning |
|---|---|
| `PayrollAttendanceInput.OvertimeHours` (the input DTO) | **Ordinary** overtime only. `Compute` reads it as `ordinaryOtHours` and prices it at 1.25x |
| `PayrollRunEmployee.OvertimeHours` (the entity column) | **Total** overtime. `Compute` writes back `ordinaryOtHours + restDayOtHours` |

So rebuilding the input from the snapshot is:

```csharp
new PayrollAttendanceInput
{
    OvertimeHours    = entry.OvertimeHours - entry.RestDayOTHours,   // back to ordinary only
    RestDayOTHours   = entry.RestDayOTHours,
    // ...the remaining six fields map across by name
}
```

An implementer who maps `OvertimeHours` straight across will price every rest-day hour at 1.25x
instead of 1.69x on recompute, and no exception will be raised. The round-trip test in Testing
exists to catch exactly this.

## Architecture

```
src/PeopleCore.Application/Payroll/
  DTOs/AttendanceBridgeResult.cs        result + diagnostics
  Interfaces/IPayrollAttendanceBridge.cs
  Services/PayrollAttendanceBridge.cs   the aggregation
src/PeopleCore.Application/Scheduling/
  Services/ShiftScheduleResolver.cs     pure day-resolution rule, shared
```

```csharp
public interface IPayrollAttendanceBridge
{
    Task<AttendanceBridgeResult> BuildAsync(
        IReadOnlyList<Guid> employeeIds, DateOnly from, DateOnly to, CancellationToken ct = default);
}

public record AttendanceBridgeResult(
    IReadOnlyDictionary<Guid, PayrollAttendanceInput> Inputs,
    IReadOnlyList<Guid> EmployeesWithoutSchedule);
```

The bridge reads and never writes. `PayrollRunService` calls it once at run creation and snapshots
the result onto each `PayrollRunEmployee`.

### Two targeted improvements to existing code

**`GetEmployeeScheduleAsync` cannot be used.** It resolves each day and substitutes
`new DailyScheduleDto(d, null, null, null, true, false)` when resolution fails — an unassigned day
is indistinguishable from a genuine rest day. The bridge needs that distinction, since one means
"no absence can be derived" and the other means "not a working day".

**Per-day resolution would be N+1.** `ResolveShiftForDayAsync` hits
`IShiftAssignmentRepository.GetActiveAssignmentAsync` once per employee per day — 200 employees
over a 15-day period is 3,000 queries.

Both are fixed by the same change:

- Add `IShiftAssignmentRepository.GetActiveForPeriodAsync(IReadOnlyList<Guid> employeeIds, DateOnly from, DateOnly to)`, loading every relevant assignment with its `ShiftTemplate` and `RotatingPattern.Slots` in one query.
- Extract the resolution rule out of `ShiftService.ResolveShiftForDayAsync` into a pure
  `ShiftScheduleResolver.Resolve(EmployeeShiftAssignment?, DateOnly)` returning `DailyScheduleDto?`
  — **null meaning unresolved**, preserved rather than masked.

`ShiftService.ResolveShiftForDayAsync` then delegates to the shared resolver, so the rotating-pattern
day-offset arithmetic exists once. This is duplication removal in code the phase already touches, not
unrelated refactoring, and the existing `ShiftServiceTests` protect the behaviour.

## Field derivation

For each employee, over each date in `from..to`:

| Field | Rule |
|---|---|
| `LateMinutes` | Sum of `AttendanceRecord.LateMinutes` |
| `UndertimeMinutes` | Sum of `AttendanceRecord.UndertimeMinutes` |
| `AbsenceDays` | Count of dates where the resolved schedule is non-null and `!IsRestDay`, **no** attendance record for that date is marked `IsPresent`, and no approved **paid** leave covers the date. Zero when the employee has no assignment at all |
| `OvertimeHours` | Approved `OvertimeRequest.TotalMinutes` ÷ 60 for dates whose resolved schedule is non-null and `!IsRestDay`. This is the input DTO's **ordinary-only** sense |
| `RestDayOTHours` | The same, for dates whose resolved schedule has `IsRestDay` |
| `HolidayRegularDays` | Count of dates having an attendance record with `IsPresent` where the `Holiday` calendar has `HolidayType.RegularHoliday` |
| `HolidaySpecialDays` | The same, where the calendar has `HolidayType.SpecialNonWorking` |
| `NightDiffHours` | Sum over records with **both** `TimeIn` and `TimeOut` of the overlap between `[TimeIn, TimeOut]` and the night windows, in hours |

**Night window precisely.** The window is the set of intervals `[22:00 on day D, 06:00 on day D+1]`
for every day `D` the punch interval touches. A shift running 22:00 Monday to 06:00 Tuesday overlaps
one such interval for its full eight hours; a 08:00–17:00 shift overlaps none. Compute the overlap
against each candidate interval and sum, so a punch spanning more than one night is handled without
special-casing.

**More than one attendance record for a date.** Nothing in the schema enforces one record per
employee per day. Treat a date as present if **any** record for it is marked `IsPresent`, and sum
`LateMinutes`, `UndertimeMinutes` and night-window overlap across all of them.

**The `Holiday` calendar governs, not `AttendanceRecord.IsHoliday`.** The record carries a
denormalised `IsHoliday`/`HolidayType` pair that can drift from the calendar. One source of truth.
Holidays are loaded with `IHolidayRepository.GetByYearAsync` for each year the period touches — a
period can straddle a year boundary.

**Overtime on a date with no resolved schedule** counts as ordinary `OvertimeHours`, not rest-day.
It was approved, so it is payable; without a schedule there is no basis to claim the premium rate.

**Paid versus unpaid leave.** Only `LeaveType.IsPaid == true` suppresses an absence. Approved
unpaid leave leaves the day absent, which is correct — unpaid leave is unpaid.

## Error handling

The bridge does not throw for missing data; absent data means a zero contribution. It throws only
for a genuine programming error, such as `to < from`.

Employees with no shift assignment are returned in `EmployeesWithoutSchedule` rather than being
silently treated as fully present. `PayrollRunService` logs the count when it creates a run, so the
condition is visible in operations before anyone is paid.

## Testing

The bridge takes repositories and `IShiftAssignmentRepository` by interface, so every case tests
with mocks and no database.

Required cases:

- Rest-day overtime lands in `RestDayOTHours`, ordinary overtime in `OvertimeHours`, and the two do
  not cross-contaminate.
- Approved **paid** leave suppresses an absence; approved **unpaid** leave does not.
- An employee with no shift assignment yields zero `AbsenceDays` and appears in
  `EmployeesWithoutSchedule`.
- A night shift crossing midnight contributes only its 22:00–06:00 portion.
- A record with `TimeIn` but no `TimeOut` contributes zero `NightDiffHours`.
- A rest day with no attendance produces no absence.
- A holiday present in the calendar but absent from `AttendanceRecord.IsHoliday` is still counted,
  and the reverse is not.
- `AttendanceRecord.OvertimeMinutes` never reaches any output field.

Plus one round-trip test at the service level: create a run from bridged inputs, compute, recompute,
and assert every monetary figure is identical. This is the regression the snapshot columns exist to
prevent, and it is the phase's acceptance criterion.

## Acceptance criteria

1. A payroll run created from real attendance data produces non-zero `OvertimePay`, `HolidayPay` and
   `NightDiffPay` where the underlying data warrants them.
2. Recomputing that run reproduces every monetary figure exactly, proven by test.
3. Rest-day overtime is paid at the rest-day rate after a recompute, not reclassified as ordinary.
4. An employee with no shift assignment is never deducted an absence and is reported in diagnostics.
5. `dotnet build` is clean and the full suite is green.
6. The Phase 1 statutory tests remain untouched and passing — this phase changes what the engine is
   fed, never how it computes.
