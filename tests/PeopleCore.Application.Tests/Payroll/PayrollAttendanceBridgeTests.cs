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

    /// <summary>A plain fixed day shift: every date in the period is a scheduled working day.</summary>
    private static EmployeeShiftAssignment FixedAssignment(Guid employeeId, DateOnly effectiveFrom)
    {
        var template = DayShift();
        return new EmployeeShiftAssignment
        {
            EmployeeId = employeeId,
            ShiftTemplateId = template.Id,
            ShiftTemplate = template,
            EffectiveFrom = effectiveFrom
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
        var employeeId = Guid.NewGuid();
        var from = new DateOnly(2026, 3, 2);
        var to = new DateOnly(2026, 3, 3);

        // A fixed day shift, so both dates are scheduled working days - and no attendance at all.
        _assignments.Setup(r => r.GetActiveForPeriodAsync(
                        It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync([FixedAssignment(employeeId, from)]);

        _leave.Setup(r => r.GetApprovedByPeriodAsync(
                  It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync([
                  new LeaveRequest
                  {
                      EmployeeId = employeeId,
                      StartDate = from, EndDate = from,
                      Status = LeaveStatus.Approved,
                      LeaveType = new LeaveType { Name = "Vacation", IsPaid = true }
                  },
                  new LeaveRequest
                  {
                      EmployeeId = employeeId,
                      StartDate = to, EndDate = to,
                      Status = LeaveStatus.Approved,
                      LeaveType = new LeaveType { Name = "Leave without pay", IsPaid = false }
                  }
              ]);

        var result = await _sut.BuildAsync([employeeId], from, to, CancellationToken.None);

        // Paid leave suppresses its day; unpaid leave leaves the day absent - unpaid leave is unpaid.
        result.Inputs[employeeId].AbsenceDays.Should().Be(1m);
    }

    [Fact]
    public async Task BuildAsync_PaidLeaveStraddlingThePeriodBoundary_SuppressesAbsencesOnTheInPeriodDays()
    {
        var employeeId = Guid.NewGuid();
        var from = new DateOnly(2026, 3, 2);
        var to = new DateOnly(2026, 3, 3);

        // A fixed day shift, so both in-period dates are scheduled working days, with no
        // attendance recorded at all - so both would be absences unless the leave suppresses them.
        _assignments.Setup(r => r.GetActiveForPeriodAsync(
                        It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync([FixedAssignment(employeeId, from)]);

        // This row starts before `from` and ends after `to` - a leave straddling the period on
        // both sides. This is exactly the shape LeaveRequestRepository.GetApprovedByPeriodAsync's
        // overlap predicate returns and its old containment predicate never did. The bridge is
        // expected to clamp the unclamped StartDate/EndDate to the requested period rather than
        // require the repository (or this mock) to do it.
        _leave.Setup(r => r.GetApprovedByPeriodAsync(
                  It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync([
                  new LeaveRequest
                  {
                      EmployeeId = employeeId,
                      StartDate = new DateOnly(2026, 2, 28),
                      EndDate = new DateOnly(2026, 3, 5),
                      Status = LeaveStatus.Approved,
                      LeaveType = new LeaveType { Name = "Vacation", IsPaid = true }
                  }
              ]);

        var result = await _sut.BuildAsync([employeeId], from, to, CancellationToken.None);

        // Both in-period days are covered by the straddling leave, so neither is an absence.
        result.Inputs[employeeId].AbsenceDays.Should().Be(0m);
    }

    [Fact]
    public async Task BuildAsync_WhenEmployeeHasNoShiftAssignment_DerivesNoAbsencesAndReportsThem()
    {
        var employeeId = Guid.NewGuid();
        var from = new DateOnly(2026, 3, 2);
        var to = new DateOnly(2026, 3, 6);   // five days, no assignment and no attendance

        var result = await _sut.BuildAsync([employeeId], from, to, CancellationToken.None);

        // Deducting wages the schedule cannot justify is the compliance problem; failing to
        // deduct is the recoverable business one.
        result.Inputs[employeeId].AbsenceDays.Should().Be(0m);
        result.EmployeesWithoutSchedule.Should().Contain(employeeId);
    }

    [Fact]
    public async Task BuildAsync_DoesNotCountARestDayWithNoAttendanceAsAnAbsence()
    {
        var employeeId = Guid.NewGuid();
        var anchor = new DateOnly(2026, 3, 1);   // offsets 0 and 1 work; offset 2 (Mar 3) rests

        _assignments.Setup(r => r.GetActiveForPeriodAsync(
                        It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync([RotatingAssignment(employeeId, anchor)]);

        var result = await _sut.BuildAsync([employeeId], anchor, new DateOnly(2026, 3, 3), CancellationToken.None);

        // Three dates with no attendance, but only two of them are scheduled working days.
        result.Inputs[employeeId].AbsenceDays.Should().Be(2m);
        result.EmployeesWithoutSchedule.Should().NotContain(employeeId);
    }

    [Fact]
    public async Task BuildAsync_ClassifiesHolidaysFromTheCalendar_NotTheAttendanceRecordFlag()
    {
        var employeeId = Guid.NewGuid();
        var inCalendar = new DateOnly(2026, 3, 4);    // calendar says special; the record says no
        var flaggedOnly = new DateOnly(2026, 3, 5);   // record says regular; the calendar says no

        _assignments.Setup(r => r.GetActiveForPeriodAsync(
                        It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync([FixedAssignment(employeeId, inCalendar)]);

        _attendance.Setup(r => r.GetAllByPeriodAsync(
                       It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync([
                       new AttendanceRecord
                       {
                           EmployeeId = employeeId, AttendanceDate = inCalendar,
                           IsPresent = true, IsHoliday = false
                       },
                       new AttendanceRecord
                       {
                           EmployeeId = employeeId, AttendanceDate = flaggedOnly,
                           IsPresent = true, IsHoliday = true, HolidayType = HolidayType.RegularHoliday
                       }
                   ]);

        _holidays.Setup(r => r.GetByYearAsync(2026, It.IsAny<CancellationToken>()))
                 .ReturnsAsync([new Holiday
                 {
                     Name = "Special non-working day",
                     HolidayDate = inCalendar,
                     HolidayType = HolidayType.SpecialNonWorking
                 }]);

        var result = await _sut.BuildAsync([employeeId], inCalendar, flaggedOnly, CancellationToken.None);

        var input = result.Inputs[employeeId];
        // The calendar is the single source of truth; the record's denormalised flag can drift.
        input.HolidaySpecialDays.Should().Be(1m);
        input.HolidayRegularDays.Should().Be(0m);
    }

    [Fact]
    public async Task BuildAsync_NeverPaysPunchDerivedOvertime()
    {
        var employeeId = Guid.NewGuid();
        var date = new DateOnly(2026, 3, 2);

        _assignments.Setup(r => r.GetActiveForPeriodAsync(
                        It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync([FixedAssignment(employeeId, date)]);

        _attendance.Setup(r => r.GetAllByPeriodAsync(
                       It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync([new AttendanceRecord
                   {
                       EmployeeId = employeeId, AttendanceDate = date,
                       IsPresent = true,
                       OvertimeMinutes = 120   // stayed late, never approved
                   }]);

        var result = await _sut.BuildAsync([employeeId], date, date, CancellationToken.None);

        var input = result.Inputs[employeeId];
        // Only approved OvertimeRequest rows are a payroll liability.
        input.OvertimeHours.Should().Be(0m);
        input.RestDayOTHours.Should().Be(0m);
    }

    [Fact]
    public async Task BuildAsync_SumsLateAndUndertimeAcrossTheperiod()
    {
        var employeeId = Guid.NewGuid();
        var first = new DateOnly(2026, 3, 2);
        var second = new DateOnly(2026, 3, 3);

        _assignments.Setup(r => r.GetActiveForPeriodAsync(
                        It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync([FixedAssignment(employeeId, first)]);

        _attendance.Setup(r => r.GetAllByPeriodAsync(
                       It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync([
                       new AttendanceRecord
                       {
                           EmployeeId = employeeId, AttendanceDate = first,
                           IsPresent = true, LateMinutes = 15, UndertimeMinutes = 10
                       },
                       new AttendanceRecord
                       {
                           EmployeeId = employeeId, AttendanceDate = second,
                           IsPresent = true, LateMinutes = 20, UndertimeMinutes = 5
                       }
                   ]);

        var result = await _sut.BuildAsync([employeeId], first, second, CancellationToken.None);

        var input = result.Inputs[employeeId];
        input.LateMinutes.Should().Be(35m);
        input.UndertimeMinutes.Should().Be(15m);
    }

    [Fact]
    public async Task BuildAsync_IgnoresARecordWithNoTimeOutForNightDifferential()
    {
        var employeeId = Guid.NewGuid();
        var complete = new DateOnly(2026, 3, 2);
        var openEnded = new DateOnly(2026, 3, 4);

        _assignments.Setup(r => r.GetActiveForPeriodAsync(
                        It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync([FixedAssignment(employeeId, complete)]);

        _attendance.Setup(r => r.GetAllByPeriodAsync(
                       It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync([
                       new AttendanceRecord
                       {
                           EmployeeId = employeeId, AttendanceDate = complete, IsPresent = true,
                           TimeIn = new DateTime(2026, 3, 2, 22, 0, 0),
                           TimeOut = new DateTime(2026, 3, 3, 6, 0, 0)      // the whole 22:00-06:00 window
                       },
                       new AttendanceRecord
                       {
                           EmployeeId = employeeId, AttendanceDate = openEnded, IsPresent = true,
                           TimeIn = new DateTime(2026, 3, 4, 22, 0, 0),
                           TimeOut = null                                    // never punched out
                       }
                   ]);

        var result = await _sut.BuildAsync([employeeId], complete, new DateOnly(2026, 3, 5), CancellationToken.None);

        // The open-ended record contributes nothing rather than a guess at when it ended.
        result.Inputs[employeeId].NightDiffHours.Should().Be(8m);
    }

    [Fact]
    public async Task BuildAsync_OvertimeOnANullScheduleDate_LandsInOrdinaryHours_NotRestDayHours()
    {
        var employeeId = Guid.NewGuid();
        var date = new DateOnly(2026, 3, 2);

        // No assignment at all for this employee, so PickAssignment returns null and
        // ShiftScheduleResolver.Resolve returns null - null, not IsRestDay: true, because there is
        // no schedule to say the date is a rest day. Approved overtime is still payable, but at
        // the ordinary basis rather than the rest-day premium.
        _overtime.Setup(r => r.GetApprovedByPeriodAsync(
                     It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync([new OvertimeRequest
                 {
                     EmployeeId = employeeId,
                     OvertimeDate = date,
                     TotalMinutes = 120,
                     Status = OvertimeStatus.Approved
                 }]);

        var result = await _sut.BuildAsync([employeeId], date, date, CancellationToken.None);

        var input = result.Inputs[employeeId];
        input.OvertimeHours.Should().Be(2m);
        input.RestDayOTHours.Should().Be(0m);
    }

    [Fact]
    public async Task BuildAsync_WhenTwoAssignmentsCoverAnEmployee_TheLaterEffectiveFromGovernsItsDates()
    {
        var employeeId = Guid.NewGuid();
        var from = new DateOnly(2026, 3, 2);
        var to = new DateOnly(2026, 3, 4);

        // Assignment 1: a plain fixed day shift, open-ended from the start of the period - every
        // date would be a scheduled working day if this assignment alone governed.
        var earlier = FixedAssignment(employeeId, from);

        // Assignment 2: a rotating pattern anchored - and effective - on the last date of the
        // period, whose offset 0 is a rest day. Its EffectiveFrom is later than assignment 1's, so
        // it should supersede assignment 1 for the one date it covers (Mar 4), leaving assignment 1
        // to govern the earlier two dates.
        var lastDate = to;
        var pattern = new RotatingPattern { Name = "rest-on-effective-date", CycleLengthDays = 3 };
        pattern.Slots.Add(new RotatingPatternSlot { DayOffset = 0 }); // rest day
        var later = new EmployeeShiftAssignment
        {
            EmployeeId = employeeId,
            RotatingPatternId = pattern.Id,
            RotatingPattern = pattern,
            PatternStartDate = lastDate,
            EffectiveFrom = lastDate
        };

        _assignments.Setup(r => r.GetActiveForPeriodAsync(
                        It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync([earlier, later]);

        // No attendance at all, so every scheduled working day with no paid leave is an absence.
        var result = await _sut.BuildAsync([employeeId], from, to, CancellationToken.None);

        // Mar 2 and Mar 3: only the earlier assignment covers them -> two absences. Mar 4: the
        // later assignment's later EffectiveFrom means it - not the earlier, still-open-ended
        // assignment - governs, and it says Mar 4 is a rest day, so no absence there. If the
        // earlier assignment wrongly won the tie for Mar 4, this would be 3.
        result.Inputs[employeeId].AbsenceDays.Should().Be(2m);
    }

    [Fact]
    public async Task BuildAsync_WhenTwoAssignmentsShareAnEffectiveFrom_TheLaterCreatedAtGoverns()
    {
        var employeeId = Guid.NewGuid();
        var from = new DateOnly(2026, 3, 2);
        var to = new DateOnly(2026, 3, 6);

        // Without a tie-break on CreatedAt, the repository's row order (or SQL order) decides which
        // assignment's schedule governs an employee's daily hours - two assignments effective the
        // same day can differ by days of pay. This test pins that tie-break: it returns both
        // assignments in the wrong order (earlier CreatedAt first) and verifies the later one wins.

        // Assignment 1: a plain fixed day shift, effective Mar 2 with an earlier CreatedAt.
        // All five days in the period (Mar 2-6) are scheduled working days, so no attendance
        // would mean 5 absences.
        var earlier = FixedAssignment(employeeId, from);
        earlier.CreatedAt = new DateTime(2026, 3, 1, 9, 0, 0);

        // Assignment 2: a 2-on-1-off rotating pattern, effective the same date (Mar 2) with a
        // later CreatedAt, anchored so Mar 4 (Friday) is a rest day (offset 2).
        // Only four days in the period are scheduled working days, so no attendance would mean
        // 4 absences: Mar 2, 3, 5, 6 are working days; Mar 4 is rest.
        var later = RotatingAssignment(employeeId, from);
        later.CreatedAt = new DateTime(2026, 3, 1, 10, 0, 0);

        // Return them in the order that would give the WRONG answer if the tie-break were absent:
        // the earlier-CreatedAt assignment first. Without the tie-break, it would be selected,
        // yielding 5 absences. With the tie-break, the later-CreatedAt assignment wins, yielding 4.
        _assignments.Setup(r => r.GetActiveForPeriodAsync(
                        It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync([earlier, later]);

        var result = await _sut.BuildAsync([employeeId], from, to, CancellationToken.None);

        // Only the later-CreatedAt (rotating) assignment can produce this outcome: four absences
        // (Mar 2, 3, 5, 6 are working days; Mar 4 is a rest day with no attendance, which is not
        // an absence). If the earlier-CreatedAt (fixed) assignment were selected, all five would
        // be absences. This assertion fails if the CreatedAt tie-break clause is deleted.
        result.Inputs[employeeId].AbsenceDays.Should().Be(4m);
    }

    [Fact]
    public async Task BuildAsync_FixedTemplateAssignment_TreatsSaturdayAndSundayAsNonWorking()
    {
        var employeeId = Guid.NewGuid();
        var saturday = new DateOnly(2026, 3, 7);
        var sunday = new DateOnly(2026, 3, 8);

        // A plain fixed shift template has no day-of-week concept, so on its own it would make
        // Saturday and Sunday scheduled working days too. The bridge must fall back to the
        // Mon-Fri convention for a fixed-template assignment.
        _assignments.Setup(r => r.GetActiveForPeriodAsync(
                        It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync([FixedAssignment(employeeId, saturday)]);

        var result = await _sut.BuildAsync([employeeId], saturday, sunday, CancellationToken.None);

        // No attendance at all on a Saturday and Sunday, but neither should be deducted.
        result.Inputs[employeeId].AbsenceDays.Should().Be(0m);
    }

    [Fact]
    public async Task BuildAsync_RotatingPatternSchedulingSaturdayAsWork_StillTreatsItAsAWorkingDay()
    {
        var employeeId = Guid.NewGuid();
        var saturday = new DateOnly(2026, 3, 7);

        // A rotating pattern that anchors and works its one-day cycle on a Saturday - its own
        // rest-day slots are authoritative and must not be second-guessed by the Mon-Fri
        // fallback that applies to fixed-template assignments.
        var template = DayShift();
        var pattern = new RotatingPattern { Name = "always-on", CycleLengthDays = 1 };
        pattern.Slots.Add(new RotatingPatternSlot { DayOffset = 0, ShiftTemplateId = template.Id, ShiftTemplate = template });
        var assignment = new EmployeeShiftAssignment
        {
            EmployeeId = employeeId,
            RotatingPatternId = pattern.Id,
            RotatingPattern = pattern,
            PatternStartDate = saturday,
            EffectiveFrom = saturday
        };

        _assignments.Setup(r => r.GetActiveForPeriodAsync(
                        It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync([assignment]);

        var result = await _sut.BuildAsync([employeeId], saturday, saturday, CancellationToken.None);

        // The pattern says Saturday is a working day, and no attendance was recorded, so it is an
        // absence - the rotating pattern's own rest-day slots govern, not the calendar day.
        result.Inputs[employeeId].AbsenceDays.Should().Be(1m);
    }

    [Fact]
    public async Task BuildAsync_UnworkedRegularHolidayCreatesNoAbsence_UnworkedSpecialNonWorkingDoes()
    {
        var employeeId = Guid.NewGuid();
        var regularHoliday = new DateOnly(2026, 1, 1);       // New Year's Day - regular holiday
        var specialNonWorking = new DateOnly(2026, 1, 2);    // adjacent day - special non-working

        // Fixed shift, scheduled on both dates; the employee does not show up on either.
        _assignments.Setup(r => r.GetActiveForPeriodAsync(
                        It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync([FixedAssignment(employeeId, regularHoliday)]);

        _holidays.Setup(r => r.GetByYearAsync(2026, It.IsAny<CancellationToken>()))
                 .ReturnsAsync([
                     new Holiday { Name = "New Year's Day", HolidayDate = regularHoliday, HolidayType = HolidayType.RegularHoliday },
                     new Holiday { Name = "Special Day", HolidayDate = specialNonWorking, HolidayType = HolidayType.SpecialNonWorking }
                 ]);

        var result = await _sut.BuildAsync([employeeId], regularHoliday, specialNonWorking, CancellationToken.None);

        // Regular holiday: 100% of the daily wage is owed whether worked or not, and
        // basePeriodPay already pays it - deducting an absence would claw it back. Special
        // non-working: "no work, no pay" governs, so the absence still applies.
        result.Inputs[employeeId].AbsenceDays.Should().Be(1m);
    }

    [Fact]
    public async Task BuildAsync_DuplicateHolidayDates_PreferRegularOverSpecial_RegardlessOfOrder()
    {
        var employeeId = Guid.NewGuid();
        var date = new DateOnly(2026, 4, 9); // e.g. a regular holiday a local government also
                                              // declares a special non-working day

        _assignments.Setup(r => r.GetActiveForPeriodAsync(
                        It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync([FixedAssignment(employeeId, date)]);

        _attendance.Setup(r => r.GetAllByPeriodAsync(
                       It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync([new AttendanceRecord { EmployeeId = employeeId, AttendanceDate = date, IsPresent = true }]);

        // Order 1: RegularHoliday row first.
        _holidays.Setup(r => r.GetByYearAsync(2026, It.IsAny<CancellationToken>()))
                 .ReturnsAsync([
                     new Holiday { Name = "Regular", HolidayDate = date, HolidayType = HolidayType.RegularHoliday },
                     new Holiday { Name = "Special", HolidayDate = date, HolidayType = HolidayType.SpecialNonWorking }
                 ]);

        var resultRegularFirst = await _sut.BuildAsync([employeeId], date, date, CancellationToken.None);

        resultRegularFirst.Inputs[employeeId].HolidayRegularDays.Should().Be(1m);
        resultRegularFirst.Inputs[employeeId].HolidaySpecialDays.Should().Be(0m);

        // Order 2: SpecialNonWorking row first - the tie-break must still prefer RegularHoliday.
        _holidays.Setup(r => r.GetByYearAsync(2026, It.IsAny<CancellationToken>()))
                 .ReturnsAsync([
                     new Holiday { Name = "Special", HolidayDate = date, HolidayType = HolidayType.SpecialNonWorking },
                     new Holiday { Name = "Regular", HolidayDate = date, HolidayType = HolidayType.RegularHoliday }
                 ]);

        var resultSpecialFirst = await _sut.BuildAsync([employeeId], date, date, CancellationToken.None);

        resultSpecialFirst.Inputs[employeeId].HolidayRegularDays.Should().Be(1m);
        resultSpecialFirst.Inputs[employeeId].HolidaySpecialDays.Should().Be(0m);
    }

    [Fact]
    public async Task BuildAsync_SixDayFixedTemplateAssignment_AccruesAnAbsenceForAnUnattendedSaturday()
    {
        var employeeId = Guid.NewGuid();
        var saturday = new DateOnly(2026, 3, 7);

        // A six-day (Mon-Sat) fixed template. Under the old hardcoded Mon-Fri fallback this
        // Saturday would have been silently forgiven; the resolver now knows this employee's
        // work week actually includes it.
        var template = DayShift();
        template.WorkDays = WorkDays.MondayToSaturday;
        var assignment = new EmployeeShiftAssignment
        {
            EmployeeId = employeeId,
            ShiftTemplateId = template.Id,
            ShiftTemplate = template,
            EffectiveFrom = saturday
        };

        _assignments.Setup(r => r.GetActiveForPeriodAsync(
                        It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync([assignment]);

        var result = await _sut.BuildAsync([employeeId], saturday, saturday, CancellationToken.None);

        // No attendance recorded on this Saturday, and it is a scheduled working day for this
        // six-day employee, so it accrues an absence.
        result.Inputs[employeeId].AbsenceDays.Should().Be(1m);
    }

    [Fact]
    public async Task BuildAsync_ThrowsArgumentException_WhenPeriodEndPrecedesStart()
    {
        var employeeId = Guid.NewGuid();
        var from = new DateOnly(2026, 3, 5);
        var to = new DateOnly(2026, 3, 1);

        var act = () => _sut.BuildAsync([employeeId], from, to, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
