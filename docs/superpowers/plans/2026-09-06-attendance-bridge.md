# Attendance Bridge — Phase 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Derive `PayrollAttendanceInput` from PeopleCore's own attendance data and snapshot it onto the payroll run, so payroll stops being keyed in by hand and a recompute can never change what someone was paid.

**Architecture:** A read-only `PayrollAttendanceBridge` aggregates punches, approved leave, approved overtime, the holiday calendar and the resolved shift schedule into one `PayrollAttendanceInput` per employee. `PayrollRunService` calls it once at run creation and stores the result on `PayrollRunEmployee`, which gains seven columns so the rest-day and special-holiday splits survive a recompute.

**Tech Stack:** .NET 10 · EF Core 10 + Npgsql (snake_case) · xUnit · Moq · FluentAssertions

**Design spec:** [`docs/superpowers/specs/2026-09-06-attendance-bridge-design.md`](../specs/2026-09-06-attendance-bridge-design.md)

## Global Constraints

- Target framework `net10.0` on every project.
- New snapshot columns are `numeric(6,2)`; tables and columns are snake_case via the global naming convention.
- **Do not modify the Phase 1 statutory code or its tests.** `PayrollComputationService.cs`, `PayrollLineBuilder.cs`, `DolePremiumRates.cs`, `BirWithholdingTax.cs`, `SssContributionSchedule.cs` and the test files `PayrollComputationServiceTests`, `PayrollLineBuilderTests`, `DolePremiumRatesTests`, `BirWithholdingTaxTests` are byte-verified against their PayZen originals. This phase changes what the engine is fed, never how it computes.
- **`OvertimeHours` means two different things.** On `PayrollAttendanceInput` it is **ordinary overtime only** (priced 1.25x). On `PayrollRunEmployee` it is the **total** (`ordinary + restDay`, written back by `Compute`). Rebuilding the input from a snapshot is `OvertimeHours - RestDayOTHours`. Mapping it straight across misprices every rest-day hour at 1.25x instead of 1.69x, silently.
- The `Holiday` calendar governs holiday classification, never `AttendanceRecord.IsHoliday`.
- Only approved `OvertimeRequest` rows are payable. `AttendanceRecord.OvertimeMinutes` must never reach any output field.
- Only `LeaveType.IsPaid == true` suppresses an absence.
- An employee with no shift assignment yields zero `AbsenceDays` and is reported, never silently treated as fully present.
- The suite reports **167 passing** at the start of this plan.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/PeopleCore.Application/Scheduling/Services/ShiftScheduleResolver.cs` | Pure day-resolution rule, shared by `ShiftService` and the bridge; returns null when unresolved |
| `src/PeopleCore.Application/Payroll/Services/NightDifferential.cs` | Pure 22:00–06:00 overlap calculator |
| `src/PeopleCore.Application/Payroll/Interfaces/IPayrollAttendanceBridge.cs` | Bridge contract |
| `src/PeopleCore.Application/Payroll/Services/PayrollAttendanceBridge.cs` | The aggregation |
| `src/PeopleCore.Application/Payroll/DTOs/AttendanceBridgeResult.cs` | Result + diagnostics |
| `src/PeopleCore.Domain/Entities/Payroll/PayrollRunEmployee.cs` | Gains seven snapshot columns |
| `src/PeopleCore.Application/Payroll/Services/PayrollRunService.cs` | Calls the bridge, snapshots, rebuilds on recompute |

---

### Task 1: Extract the shift resolution rule

**Files:**
- Create: `src/PeopleCore.Application/Scheduling/Services/ShiftScheduleResolver.cs`
- Modify: `src/PeopleCore.Application/Scheduling/Services/ShiftService.cs`
- Test: `tests/PeopleCore.Application.Tests/Scheduling/ShiftScheduleResolverTests.cs`

**Interfaces:**
- Consumes: `EmployeeShiftAssignment`, `DailyScheduleDto`
- Produces: `ShiftScheduleResolver.Resolve(EmployeeShiftAssignment? assignment, DateOnly date)` returning `DailyScheduleDto?` — **null means unresolved**, which is distinct from a rest day

**Why:** `ShiftService.GetEmployeeScheduleAsync` substitutes `new DailyScheduleDto(d, null, null, null, true, false)` for a day it cannot resolve, making "no assignment" indistinguishable from a genuine rest day. Those mean opposite things for absence derivation. Extracting the rule lets the bridge see the null, and removes a second copy of the rotating-pattern arithmetic.

- [ ] **Step 1: Write the failing tests**

```csharp
using FluentAssertions;
using PeopleCore.Application.Scheduling.Services;
using PeopleCore.Domain.Entities.Scheduling;

namespace PeopleCore.Application.Tests.Scheduling;

public class ShiftScheduleResolverTests
{
    private static ShiftTemplate Day() => new()
    {
        Name = "Day", StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(17, 0), IsNightShift = false
    };

    [Fact]
    public void Resolve_WhenAssignmentIsNull_ReturnsNull()
    {
        // Null means "no basis to say anything about this day" - NOT a rest day.
        ShiftScheduleResolver.Resolve(null, new DateOnly(2026, 3, 2)).Should().BeNull();
    }

    [Fact]
    public void Resolve_WithFixedShift_ReturnsThatShiftAndIsNotARestDay()
    {
        var template = Day();
        var assignment = new EmployeeShiftAssignment
        {
            ShiftTemplateId = template.Id,
            ShiftTemplate = template,
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };

        var result = ShiftScheduleResolver.Resolve(assignment, new DateOnly(2026, 3, 2));

        result.Should().NotBeNull();
        result!.IsRestDay.Should().BeFalse();
        result.ShiftName.Should().Be("Day");
        result.StartTime.Should().Be(new TimeOnly(8, 0));
    }

    [Fact]
    public void Resolve_WithRotatingPattern_MarksAnEmptySlotAsARestDay()
    {
        var template = Day();
        var pattern = new RotatingPattern { Name = "2-on-1-off", CycleLengthDays = 3 };
        pattern.Slots.Add(new RotatingPatternSlot { DayOffset = 0, ShiftTemplateId = template.Id, ShiftTemplate = template });
        pattern.Slots.Add(new RotatingPatternSlot { DayOffset = 1, ShiftTemplateId = template.Id, ShiftTemplate = template });
        pattern.Slots.Add(new RotatingPatternSlot { DayOffset = 2 });   // rest day

        var assignment = new EmployeeShiftAssignment
        {
            RotatingPatternId = pattern.Id,
            RotatingPattern = pattern,
            PatternStartDate = new DateOnly(2026, 3, 1),
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };

        ShiftScheduleResolver.Resolve(assignment, new DateOnly(2026, 3, 1))!.IsRestDay.Should().BeFalse();
        ShiftScheduleResolver.Resolve(assignment, new DateOnly(2026, 3, 3))!.IsRestDay.Should().BeTrue();
    }

    [Fact]
    public void Resolve_WithRotatingPattern_HandlesADateBeforeTheAnchor()
    {
        // The day offset must stay non-negative; C# % yields a negative for dates before the anchor.
        var template = Day();
        var pattern = new RotatingPattern { Name = "2-on-1-off", CycleLengthDays = 3 };
        pattern.Slots.Add(new RotatingPatternSlot { DayOffset = 0, ShiftTemplateId = template.Id, ShiftTemplate = template });
        pattern.Slots.Add(new RotatingPatternSlot { DayOffset = 1, ShiftTemplateId = template.Id, ShiftTemplate = template });
        pattern.Slots.Add(new RotatingPatternSlot { DayOffset = 2 });

        var assignment = new EmployeeShiftAssignment
        {
            RotatingPatternId = pattern.Id,
            RotatingPattern = pattern,
            PatternStartDate = new DateOnly(2026, 3, 1),
            EffectiveFrom = new DateOnly(2026, 1, 1)
        };

        // 2026-02-27 is three days before the anchor, so it lands back on offset 0.
        ShiftScheduleResolver.Resolve(assignment, new DateOnly(2026, 2, 26))!.IsRestDay.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/PeopleCore.Application.Tests/ --nologo --filter "FullyQualifiedName~ShiftScheduleResolver"`
Expected: FAIL — `ShiftScheduleResolver` not found

- [ ] **Step 3: Create the resolver**

Move the body of `ShiftService.ResolveShiftForDayAsync` (everything after the repository lookup) into this pure class, unchanged:

```csharp
using PeopleCore.Application.Scheduling.DTOs;
using PeopleCore.Domain.Entities.Scheduling;

namespace PeopleCore.Application.Scheduling.Services;

/// <summary>
/// Resolves what an employee was scheduled to work on one date.
/// <para>
/// Returns null when the assignment cannot answer for the date - which is NOT the same as a rest
/// day. Payroll needs the distinction: a rest day means "not a working day"; null means "no basis
/// to derive an absence", and deriving one anyway would deduct wages the schedule cannot justify.
/// </para>
/// </summary>
public static class ShiftScheduleResolver
{
    public static DailyScheduleDto? Resolve(EmployeeShiftAssignment? assignment, DateOnly date)
    {
        if (assignment is null) return null;

        if (assignment.ShiftTemplateId.HasValue && assignment.ShiftTemplate is not null)
        {
            var s = assignment.ShiftTemplate;
            return new DailyScheduleDto(date, s.Name, s.StartTime, s.EndTime, false, s.IsNightShift);
        }

        if (assignment.RotatingPatternId.HasValue && assignment.RotatingPattern is not null)
        {
            var pattern = assignment.RotatingPattern;
            var anchorDate = assignment.PatternStartDate ?? assignment.EffectiveFrom;
            var rawOffset = (date.DayNumber - anchorDate.DayNumber) % pattern.CycleLengthDays;
            var dayOffset = rawOffset < 0 ? rawOffset + pattern.CycleLengthDays : rawOffset;
            var slot = pattern.Slots.FirstOrDefault(s => s.DayOffset == dayOffset);

            if (slot is null || slot.ShiftTemplateId is null || slot.ShiftTemplate is null)
                return new DailyScheduleDto(date, null, null, null, true, false); // rest day

            var st = slot.ShiftTemplate;
            return new DailyScheduleDto(date, st.Name, st.StartTime, st.EndTime, false, st.IsNightShift);
        }

        return null;
    }
}
```

- [ ] **Step 4: Make `ShiftService` delegate**

Replace `ResolveShiftForDayAsync`'s body so the rule exists once:

```csharp
    public async Task<DailyScheduleDto?> ResolveShiftForDayAsync(
        Guid employeeId, DateOnly date, CancellationToken ct = default)
    {
        var assignment = await _assignments.GetActiveAssignmentAsync(employeeId, date, ct);
        return ShiftScheduleResolver.Resolve(assignment, date);
    }
```

Leave `GetEmployeeScheduleAsync` exactly as it is — its rest-day substitution is correct for the UI that consumes it, and changing it is out of scope.

- [ ] **Step 5: Run the resolver tests and the existing shift tests**

Run: `dotnet test tests/PeopleCore.Application.Tests/ --nologo --filter "FullyQualifiedName~Shift"`
Expected: all pass, including the pre-existing `ShiftServiceTests` — they protect the behaviour you just moved.

- [ ] **Step 6: Commit**

```bash
git add src/PeopleCore.Application/Scheduling tests/PeopleCore.Application.Tests/Scheduling
git commit -m "refactor(scheduling): extract the shift resolution rule so null survives"
```

---

### Task 2: Bulk-load shift assignments for a period

**Files:**
- Modify: `src/PeopleCore.Domain/Interfaces/IShiftAssignmentRepository.cs`, `src/PeopleCore.Infrastructure/Persistence/Repositories/ShiftAssignmentRepository.cs`

**Interfaces:**
- Consumes: `EmployeeShiftAssignment`
- Produces: `Task<IReadOnlyList<EmployeeShiftAssignment>> GetActiveForPeriodAsync(IReadOnlyList<Guid> employeeIds, DateOnly from, DateOnly to, CancellationToken ct = default)`

**Why:** resolving per employee per day through `GetActiveAssignmentAsync` is 200 employees × 15 days = 3,000 queries. One query, resolved in memory.

- [ ] **Step 1: Add the interface method**

```csharp
    /// <summary>
    /// Every assignment overlapping the period for the given employees, with the navigation
    /// properties the resolver needs. One query, so callers can resolve day-by-day in memory
    /// instead of issuing a query per employee per day.
    /// </summary>
    Task<IReadOnlyList<EmployeeShiftAssignment>> GetActiveForPeriodAsync(
        IReadOnlyList<Guid> employeeIds, DateOnly from, DateOnly to, CancellationToken ct = default);
```

- [ ] **Step 2: Implement it**

Mirror the includes on the existing `GetActiveAssignmentAsync` exactly — the resolver dereferences `ShiftTemplate`, `RotatingPattern` and `RotatingPattern.Slots[].ShiftTemplate`, and a missing include silently yields a null shift, which the resolver reads as unresolved:

```csharp
    public async Task<IReadOnlyList<EmployeeShiftAssignment>> GetActiveForPeriodAsync(
        IReadOnlyList<Guid> employeeIds, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        return await _context.ShiftAssignments
            .Include(a => a.ShiftTemplate)
            .Include(a => a.RotatingPattern)
                .ThenInclude(p => p!.Slots)
                    .ThenInclude(s => s.ShiftTemplate)
            .Where(a => employeeIds.Contains(a.EmployeeId)
                && a.EffectiveFrom <= to
                && (a.EffectiveTo == null || a.EffectiveTo >= from))
            .OrderByDescending(a => a.EffectiveFrom)
            .ToListAsync(ct);
    }
```

- [ ] **Step 3: Build and run the suite**

Run: `dotnet test PeopleCore.slnx --nologo -v q`
Expected: `Passed: 171` (167 plus Task 1's four)

- [ ] **Step 4: Commit**

```bash
git add src/PeopleCore.Domain/Interfaces src/PeopleCore.Infrastructure/Persistence/Repositories
git commit -m "feat(scheduling): add a bulk period query for shift assignments"
```

---

### Task 3: Night differential calculator

**Files:**
- Create: `src/PeopleCore.Application/Payroll/Services/NightDifferential.cs`
- Test: `tests/PeopleCore.Application.Tests/Payroll/NightDifferentialTests.cs`

**Interfaces:**
- Consumes: nothing
- Produces: `NightDifferential.Hours(DateTime timeIn, DateTime timeOut)` returning `decimal`

- [ ] **Step 1: Write the failing tests**

```csharp
using FluentAssertions;
using PeopleCore.Application.Payroll.Services;

namespace PeopleCore.Application.Tests.Payroll;

public class NightDifferentialTests
{
    [Fact]
    public void Hours_ForADayShift_IsZero()
    {
        NightDifferential.Hours(new DateTime(2026, 3, 2, 8, 0, 0), new DateTime(2026, 3, 2, 17, 0, 0))
            .Should().Be(0m);
    }

    [Fact]
    public void Hours_ForANightShiftCrossingMidnight_CountsOnlyTheNightWindow()
    {
        // 22:00 to 06:00 is entirely inside the window: eight hours.
        NightDifferential.Hours(new DateTime(2026, 3, 2, 22, 0, 0), new DateTime(2026, 3, 3, 6, 0, 0))
            .Should().Be(8m);
    }

    [Fact]
    public void Hours_CountsOnlyTheOverlappingPortion()
    {
        // 20:00 to 02:00 overlaps the window from 22:00: four hours, not six.
        NightDifferential.Hours(new DateTime(2026, 3, 2, 20, 0, 0), new DateTime(2026, 3, 3, 2, 0, 0))
            .Should().Be(4m);
    }

    [Fact]
    public void Hours_ForAShiftStartingAfterMidnight_UsesThePreviousDaysWindow()
    {
        // 02:00 to 07:00 sits inside the window that opened at 22:00 the previous day: four hours.
        NightDifferential.Hours(new DateTime(2026, 3, 3, 2, 0, 0), new DateTime(2026, 3, 3, 7, 0, 0))
            .Should().Be(4m);
    }

    [Fact]
    public void Hours_SpanningTwoNights_SumsBothWindows()
    {
        // 2 Mar 20:00 to 4 Mar 08:00: 22:00-06:00 twice = sixteen hours.
        NightDifferential.Hours(new DateTime(2026, 3, 2, 20, 0, 0), new DateTime(2026, 3, 4, 8, 0, 0))
            .Should().Be(16m);
    }

    [Fact]
    public void Hours_WhenTimeOutIsNotAfterTimeIn_IsZero()
    {
        NightDifferential.Hours(new DateTime(2026, 3, 2, 22, 0, 0), new DateTime(2026, 3, 2, 22, 0, 0))
            .Should().Be(0m);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/PeopleCore.Application.Tests/ --nologo --filter "FullyQualifiedName~NightDifferential"`
Expected: FAIL — type not found

- [ ] **Step 3: Implement**

```csharp
namespace PeopleCore.Application.Payroll.Services;

/// <summary>
/// Night shift differential window, Labor Code Article 86: hours worked between 10 p.m. and
/// 6 a.m. attract a premium. The window is measured against the punch interval rather than the
/// scheduled shift, so only hours genuinely worked at night are paid.
/// </summary>
public static class NightDifferential
{
    private const int WindowOpensHour = 22;
    private const int WindowClosesHour = 6;

    public static decimal Hours(DateTime timeIn, DateTime timeOut)
    {
        if (timeOut <= timeIn) return 0m;

        decimal total = 0m;

        // Start a day early: a punch beginning at 02:00 falls inside the window that opened at
        // 22:00 the previous day.
        for (var day = timeIn.Date.AddDays(-1); day <= timeOut.Date; day = day.AddDays(1))
        {
            var windowStart = day.AddHours(WindowOpensHour);
            var windowEnd = day.AddDays(1).AddHours(WindowClosesHour);

            var overlapStart = timeIn > windowStart ? timeIn : windowStart;
            var overlapEnd = timeOut < windowEnd ? timeOut : windowEnd;

            if (overlapEnd > overlapStart)
                total += (decimal)(overlapEnd - overlapStart).TotalHours;
        }

        return Math.Round(total, 2);
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/PeopleCore.Application.Tests/ --nologo --filter "FullyQualifiedName~NightDifferential"`
Expected: `Passed: 6`

- [ ] **Step 5: Commit**

```bash
git add src/PeopleCore.Application/Payroll/Services/NightDifferential.cs tests/PeopleCore.Application.Tests/Payroll/NightDifferentialTests.cs
git commit -m "feat(payroll): add the night differential window calculator"
```

---

### Task 4: Snapshot columns on PayrollRunEmployee

**Files:**
- Modify: `src/PeopleCore.Domain/Entities/Payroll/PayrollRunEmployee.cs`, `src/PeopleCore.Infrastructure/Persistence/Configurations/Payroll/PayrollRunEmployeeConfiguration.cs`
- Create: one migration

**Interfaces:**
- Produces: seven new persisted properties on `PayrollRunEmployee`

- [ ] **Step 1: Add the properties**

Place them beside the existing `DaysWorked` / `OvertimeHours` / `HolidayDays` input block:

```csharp
    /// <summary>
    /// The attendance totals this entry was computed from, snapshotted so a recompute cannot
    /// change what an employee was paid when a punch is edited later. OvertimeHours and
    /// HolidayDays above are the computed roll-ups; these are the inputs, and RestDayOTHours
    /// is the part of OvertimeHours that attracts the rest-day rate.
    /// </summary>
    public decimal AbsenceDays { get; set; }
    public decimal LateMinutes { get; set; }
    public decimal UndertimeMinutes { get; set; }
    public decimal NightDiffHours { get; set; }
    public decimal RestDayOTHours { get; set; }
    public decimal HolidayRegularDays { get; set; }
    public decimal HolidaySpecialDays { get; set; }
```

- [ ] **Step 2: Configure the precisions**

Add to `PayrollRunEmployeeConfiguration`, matching the existing `numeric(6,2)` day/hour columns:

```csharp
        builder.Property(x => x.AbsenceDays).HasColumnType("numeric(6,2)");
        builder.Property(x => x.LateMinutes).HasColumnType("numeric(6,2)");
        builder.Property(x => x.UndertimeMinutes).HasColumnType("numeric(6,2)");
        builder.Property(x => x.NightDiffHours).HasColumnType("numeric(6,2)");
        builder.Property(x => x.RestDayOTHours).HasColumnType("numeric(6,2)");
        builder.Property(x => x.HolidayRegularDays).HasColumnType("numeric(6,2)");
        builder.Property(x => x.HolidaySpecialDays).HasColumnType("numeric(6,2)");
```

- [ ] **Step 3: Generate and apply the migration**

```bash
dotnet ef migrations add AddAttendanceSnapshotColumns --project src/PeopleCore.Infrastructure --startup-project src/PeopleCore.API
dotnet ef database update --project src/PeopleCore.Infrastructure --startup-project src/PeopleCore.API
```

- [ ] **Step 4: Verify the schema**

Run: `docker exec m2net-postgres psql -U postgres -d peoplecore -c "\d payroll_run_employees"`
Expected: seven new `numeric(6,2)` columns; no column added for any computed property.

- [ ] **Step 5: Run the suite**

Run: `dotnet test PeopleCore.slnx --nologo -v q`
Expected: `Passed: 177` (171 plus Task 3's six)

- [ ] **Step 6: Commit**

```bash
git add src/PeopleCore.Domain src/PeopleCore.Infrastructure
git commit -m "feat(payroll): snapshot attendance inputs on the payroll entry"
```

---

### Task 5: The attendance bridge

**Files:**
- Create: `src/PeopleCore.Application/Payroll/DTOs/AttendanceBridgeResult.cs`, `src/PeopleCore.Application/Payroll/Interfaces/IPayrollAttendanceBridge.cs`, `src/PeopleCore.Application/Payroll/Services/PayrollAttendanceBridge.cs`
- Test: `tests/PeopleCore.Application.Tests/Payroll/PayrollAttendanceBridgeTests.cs`

**Interfaces:**
- Consumes: `IAttendanceRepository.GetAllByPeriodAsync`, `ILeaveRequestRepository.GetApprovedByPeriodAsync`, `IOvertimeRepository.GetApprovedByPeriodAsync`, `IHolidayRepository.GetByYearAsync`, `IShiftAssignmentRepository.GetActiveForPeriodAsync` (Task 2), `ShiftScheduleResolver` (Task 1), `NightDifferential` (Task 3)
- Produces:

```csharp
public record AttendanceBridgeResult(
    IReadOnlyDictionary<Guid, PayrollAttendanceInput> Inputs,
    IReadOnlyList<Guid> EmployeesWithoutSchedule);

public interface IPayrollAttendanceBridge
{
    Task<AttendanceBridgeResult> BuildAsync(
        IReadOnlyList<Guid> employeeIds, DateOnly from, DateOnly to, CancellationToken ct = default);
}
```

- [ ] **Step 1: Write the failing tests**

Follow the Moq + FluentAssertions style of `tests/PeopleCore.Application.Tests/Payroll/PayrollRunServiceTests.cs`. Every repository is mocked; no database. Cover exactly these cases, each of which encodes a decision from the design spec:

The first is written out in full as the template for the rest — it establishes the mock wiring every
other case reuses:

```csharp
using FluentAssertions;
using Moq;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Domain.Entities.Attendance;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Entities.Scheduling;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Interfaces;

namespace PeopleCore.Application.Tests.Payroll;

public class PayrollAttendanceBridgeTests
{
    private readonly Mock<IAttendanceRepository> _attendance = new();
    private readonly Mock<ILeaveRequestRepository> _leave = new();
    private readonly Mock<IOvertimeRepository> _overtime = new();
    private readonly Mock<IHolidayRepository> _holidays = new();
    private readonly Mock<IShiftAssignmentRepository> _assignments = new();
    private readonly PayrollAttendanceBridge _sut;

    public PayrollAttendanceBridgeTests()
    {
        // Empty by default; each test sets up only what it is about.
        _attendance.Setup(r => r.GetAllByPeriodAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync([]);
        _leave.Setup(r => r.GetApprovedByPeriodAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync([]);
        _overtime.Setup(r => r.GetApprovedByPeriodAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync([]);
        _holidays.Setup(r => r.GetByYearAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync([]);
        _assignments.Setup(r => r.GetActiveForPeriodAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync([]);

        _sut = new PayrollAttendanceBridge(
            _attendance.Object, _leave.Object, _overtime.Object, _holidays.Object, _assignments.Object);
    }

    private static ShiftTemplate DayShift() => new()
    {
        Name = "Day", StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(17, 0)
    };

    /// <summary>A 2-on-1-off pattern anchored at <paramref name="anchor"/>; offset 2 is a rest day.</summary>
    private static EmployeeShiftAssignment RotatingAssignment(Guid employeeId, DateOnly anchor)
    {
        var template = DayShift();
        var pattern = new RotatingPattern { Name = "2-on-1-off", CycleLengthDays = 3 };
        pattern.Slots.Add(new RotatingPatternSlot { DayOffset = 0, ShiftTemplateId = template.Id, ShiftTemplate = template });
        pattern.Slots.Add(new RotatingPatternSlot { DayOffset = 1, ShiftTemplateId = template.Id, ShiftTemplate = template });
        pattern.Slots.Add(new RotatingPatternSlot { DayOffset = 2 });   // rest day

        return new EmployeeShiftAssignment
        {
            EmployeeId = employeeId,
            RotatingPatternId = pattern.Id,
            RotatingPattern = pattern,
            PatternStartDate = anchor,
            EffectiveFrom = anchor
        };
    }

    [Fact]
    public async Task BuildAsync_PutsRestDayOvertimeInRestDayHours_NotOrdinary()
    {
        var employeeId = Guid.NewGuid();
        var anchor = new DateOnly(2026, 3, 1);
        var restDay = new DateOnly(2026, 3, 3);          // offset 2 in the pattern

        _assignments.Setup(r => r.GetActiveForPeriodAsync(
                        It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync([RotatingAssignment(employeeId, anchor)]);

        _overtime.Setup(r => r.GetApprovedByPeriodAsync(
                     It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync([new OvertimeRequest
                 {
                     EmployeeId = employeeId,
                     OvertimeDate = restDay,
                     TotalMinutes = 240,
                     Status = OvertimeStatus.Approved
                 }]);

        var result = await _sut.BuildAsync([employeeId], anchor, new DateOnly(2026, 3, 5), CancellationToken.None);

        var input = result.Inputs[employeeId];
        // Rest-day overtime prices at 1.69x; ordinary at 1.25x. Crossing these underpays.
        input.RestDayOTHours.Should().Be(4m);
        input.OvertimeHours.Should().Be(0m);
    }

[Fact]
public async Task BuildAsync_PaidLeaveSuppressesAnAbsence_UnpaidLeaveDoesNot()
{
    // Two scheduled working days with no attendance: one covered by approved leave whose
    // LeaveType.IsPaid is true, one by leave whose IsPaid is false.
    // AbsenceDays must be 1, not 0 and not 2.
}

[Fact]
public async Task BuildAsync_WhenEmployeeHasNoShiftAssignment_DerivesNoAbsencesAndReportsThem()
{
    // No EmployeeShiftAssignment at all, no attendance records across a 5-day period.
    // AbsenceDays must be 0 - deducting would be wages we cannot justify - and the
    // employee id must appear in EmployeesWithoutSchedule.
}

[Fact]
public async Task BuildAsync_DoesNotCountARestDayWithNoAttendanceAsAnAbsence()
{
    // A rest day is not a working day; absence requires a scheduled working day.
}

[Fact]
public async Task BuildAsync_ClassifiesHolidaysFromTheCalendar_NotTheAttendanceRecordFlag()
{
    // A date present in the Holiday calendar as SpecialNonWorking, whose AttendanceRecord has
    // IsHoliday = false, must still count as HolidaySpecialDays = 1. And an AttendanceRecord
    // flagged IsHoliday for a date absent from the calendar must count as neither.
}

[Fact]
public async Task BuildAsync_NeverPaysPunchDerivedOvertime()
{
    // An AttendanceRecord with OvertimeMinutes = 120 and NO approved OvertimeRequest
    // must yield OvertimeHours = 0. Unapproved overtime is not a payroll liability.
}

[Fact]
public async Task BuildAsync_SumsLateAndUndertimeAcrossTheperiod()
{
    // Two records, 15 and 20 late minutes, 10 and 5 undertime.
    // LateMinutes = 35, UndertimeMinutes = 15.
}

[Fact]
public async Task BuildAsync_IgnoresARecordWithNoTimeOutForNightDifferential()
{
    // TimeIn 22:00 with TimeOut null contributes zero NightDiffHours rather than a guess.
}
```

Write each body out fully against the mocked repositories. Do not leave a test with only a comment.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/PeopleCore.Application.Tests/ --nologo --filter "FullyQualifiedName~PayrollAttendanceBridge"`
Expected: FAIL — `PayrollAttendanceBridge` not found

- [ ] **Step 3: Implement the bridge**

Shape:

1. Load once, for the whole employee set: attendance records (`GetAllByPeriodAsync`), approved leave, approved overtime, assignments (`GetActiveForPeriodAsync`), and holidays via `GetByYearAsync` for **every year the period touches** — a period can straddle a year boundary.
2. Index each by employee (and holidays by date).
3. For each employee, walk `from..to`. For each date pick the assignment whose `EffectiveFrom <= date` and `EffectiveTo` is null or `>= date`, preferring the latest `EffectiveFrom`, then call `ShiftScheduleResolver.Resolve(assignment, date)`.
4. Accumulate per the design spec's derivation table.
5. An employee for whom **no** assignment covers **any** date in the period goes into `EmployeesWithoutSchedule`.

Rules that must be explicit in the code, each with a short comment:
- A date counts as present if **any** record for it has `IsPresent`; late, undertime and night hours sum across all records for the date.
- Overtime on a date whose schedule resolves to null counts as ordinary `OvertimeHours` — it was approved, so it is payable, but without a schedule there is no basis for the rest-day premium.
- `AttendanceRecord.OvertimeMinutes` is read nowhere.

Guard the one genuine programming error at the top of `BuildAsync`:

```csharp
        if (to < from)
            throw new ArgumentException($"Period end {to:yyyy-MM-dd} precedes start {from:yyyy-MM-dd}.", nameof(to));
```

Missing data is never an error — an employee with no records simply accumulates zeros.

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/PeopleCore.Application.Tests/ --nologo --filter "FullyQualifiedName~PayrollAttendanceBridge"`
Expected: `Passed: 8`

- [ ] **Step 5: Commit**

```bash
git add src/PeopleCore.Application/Payroll tests/PeopleCore.Application.Tests/Payroll/PayrollAttendanceBridgeTests.cs
git commit -m "feat(payroll): derive attendance totals from punches, leave and overtime"
```

---

### Task 6: Wire the bridge into payroll runs

**Files:**
- Modify: `src/PeopleCore.Application/Payroll/Services/PayrollRunService.cs`, `src/PeopleCore.API/Extensions/ServiceExtensions.cs`
- Test: extend `tests/PeopleCore.Application.Tests/Payroll/PayrollRunServiceTests.cs`

**Interfaces:**
- Consumes: `IPayrollAttendanceBridge` (Task 5), the snapshot columns (Task 4)

- [ ] **Step 1: Register the bridge**

In `ServiceExtensions.AddInfrastructure`, in the existing `// Payroll` block:

```csharp
        services.AddScoped<IPayrollAttendanceBridge, PayrollAttendanceBridge>();
```

- [ ] **Step 2: Make the manual inputs nullable so "not supplied" is distinguishable from zero**

`PayrollRunEmployeeInput.OvertimeHours` and `.HolidayDays` are currently non-nullable with a `0m` default. If the caller's value simply wins, a request that omits them silently zeroes the derived overtime and holiday figures — the exact opposite of what the bridge is for. Change both to nullable so null means "use the derived value":

```csharp
public record PayrollRunEmployeeInput(
    Guid EmployeeId,
    decimal? DaysWorked = null,
    decimal? OvertimeHours = null,
    decimal? HolidayDays = null,
    bool IncludeThirteenthMonth = false);
```

Update the existing Phase 1 service tests' arrange sections for the new shape. Those are our tests, not ported ones — changing how they construct the request is fine, but do not change what any of them assert.

- [ ] **Step 3: Call the bridge when creating a run**

In `ComputeEntriesAsync`, call `_attendanceBridge.BuildAsync(employeeIds, run.PeriodStart, run.PeriodEnd, ct)` and pass each employee's `PayrollAttendanceInput` into `Compute`'s `attendance:` parameter. A non-null field on `PayrollRunEmployeeInput` overrides the derived value, so a run can still be corrected by hand; null falls through to what the bridge derived. Log the count of `EmployeesWithoutSchedule` at warning level.

- [ ] **Step 3: Snapshot the inputs onto each entry**

After `Compute` returns an entry, copy the seven derived values onto it:

```csharp
            entry.AbsenceDays        = attendance.AbsenceDays;
            entry.LateMinutes        = attendance.LateMinutes;
            entry.UndertimeMinutes   = attendance.UndertimeMinutes;
            entry.NightDiffHours     = attendance.NightDiffHours;
            entry.RestDayOTHours     = attendance.RestDayOTHours;
            entry.HolidayRegularDays = attendance.HolidayRegularDays;
            entry.HolidaySpecialDays = attendance.HolidaySpecialDays;
```

- [ ] **Step 4: Rebuild the input from the snapshot on recompute**

`ComputeAsync` must NOT call the bridge — that would defeat the snapshot. It rebuilds from the stored columns. **Mind the two meanings of `OvertimeHours`:**

```csharp
    private static PayrollAttendanceInput FromSnapshot(PayrollRunEmployee entry) => new()
    {
        // entry.OvertimeHours is the TOTAL that Compute wrote back; the input wants the
        // ordinary part only, or every rest-day hour reprices from 1.69x down to 1.25x.
        OvertimeHours      = entry.OvertimeHours - entry.RestDayOTHours,
        RestDayOTHours     = entry.RestDayOTHours,
        AbsenceDays        = entry.AbsenceDays,
        LateMinutes        = entry.LateMinutes,
        UndertimeMinutes   = entry.UndertimeMinutes,
        NightDiffHours     = entry.NightDiffHours,
        HolidayRegularDays = entry.HolidayRegularDays,
        HolidaySpecialDays = entry.HolidaySpecialDays
    };
```

- [ ] **Step 5: Write the round-trip regression test**

This is the phase's acceptance criterion. Add to `PayrollRunServiceTests`:

```csharp
[Fact]
public async Task ComputeAsync_AfterARecompute_ReproducesEveryMonetaryFigure()
{
    // A run whose entries include BOTH rest-day and ordinary overtime, and BOTH regular and
    // special holidays - the four values the collapsed columns cannot represent.
    // Create, compute, capture every figure, recompute, and assert equality on:
    // RegularPay, OvertimePay, HolidayPay, NightDiffPay, GrossPay,
    // SSSEmployee, PhilHealthEmployee, PagIbigEmployee, WithholdingTax, NetPay.
    // If this fails on OvertimePay or HolidayPay, FromSnapshot is mapping OvertimeHours
    // straight across instead of subtracting RestDayOTHours.
}
```

Write the body out fully.

- [ ] **Step 6: Run the full suite**

Run: `dotnet test PeopleCore.slnx --nologo -v q`
Expected: all green, ≥186.

- [ ] **Step 7: Verify the Phase 1 statutory tests are untouched**

Run: `git diff --stat main..HEAD -- tests/PeopleCore.Application.Tests/Payroll/PayrollComputationServiceTests.cs tests/PeopleCore.Application.Tests/Payroll/PayrollLineBuilderTests.cs tests/PeopleCore.Application.Tests/Payroll/DolePremiumRatesTests.cs tests/PeopleCore.Application.Tests/Payroll/BirWithholdingTaxTests.cs`
Expected: no output.

- [ ] **Step 8: Commit**

```bash
git add src/PeopleCore.Application/Payroll src/PeopleCore.API tests/PeopleCore.Application.Tests/Payroll
git commit -m "feat(payroll): compute runs from derived attendance and snapshot the inputs"
```

---

## Phase exit criteria

1. A payroll run created from real attendance data produces non-zero `OvertimePay`, `HolidayPay` and `NightDiffPay` where the data warrants them.
2. Recomputing that run reproduces every monetary figure exactly, proven by the Task 6 round-trip test.
3. Rest-day overtime is still paid at the rest-day rate after a recompute.
4. An employee with no shift assignment is never deducted an absence and appears in `EmployeesWithoutSchedule`.
5. `dotnet test PeopleCore.slnx` is green and `dotnet build` is clean.
6. The four Phase 1 statutory test files are byte-unchanged.
