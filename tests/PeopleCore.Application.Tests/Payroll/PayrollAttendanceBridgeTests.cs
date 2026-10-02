using FluentAssertions;
using Moq;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Domain.Entities.Attendance;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Entities.Scheduling;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Interfaces;
using PeopleCore.Domain.Payroll;

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

    // ─── Kinds of day ─────────────────────────────────────────────────────────────────────────

    private void SetupFixedShift(Guid employeeId, DateOnly from) =>
        _assignments.Setup(r => r.GetActiveForPeriodAsync(
                        It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync([FixedAssignment(employeeId, from)]);

    private void SetupHolidays(params Holiday[] holidays) =>
        _holidays.Setup(r => r.GetByYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync(holidays);

    private void SetupPresent(Guid employeeId, params DateOnly[] dates) =>
        _attendance.Setup(r => r.GetAllByPeriodAsync(
                       It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(dates.Select(d => new AttendanceRecord
                   {
                       EmployeeId = employeeId, AttendanceDate = d, IsPresent = true
                   }).ToList());

    private void SetupOvertime(Guid employeeId, DateOnly date, int minutes) =>
        _overtime.Setup(r => r.GetApprovedByPeriodAsync(
                     It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync([new OvertimeRequest
                 {
                     EmployeeId = employeeId, OvertimeDate = date, TotalMinutes = minutes,
                     Status = OvertimeStatus.Approved
                 }]);

    [Fact]
    public async Task BuildAsync_SplitsRestDayWorkIntoItsFirstEightHoursAndOvertime()
    {
        var employeeId = Guid.NewGuid();
        var saturday = new DateOnly(2026, 3, 7);   // a rest day under the Monday-to-Friday shift

        SetupFixedShift(employeeId, new DateOnly(2026, 3, 2));
        SetupOvertime(employeeId, saturday, 600);   // ten hours

        var result = await _sut.BuildAsync([employeeId], saturday, saturday, CancellationToken.None);

        // Eight hours at the rest day's 130%, and only the last two at the 169% overtime rate.
        var input = result.Inputs[employeeId];
        input.PremiumDays.Should().BeEquivalentTo([new PremiumDayInput(WorkDayType.RestDay, Hours: 8m, OvertimeHours: 2m)]);
        input.RestDayOTHours.Should().Be(10m);
    }

    [Fact]
    public async Task BuildAsync_TwoRegularHolidaysOnOneDate_MakeADoubleHoliday()
    {
        var employeeId = Guid.NewGuid();
        var date = new DateOnly(2026, 4, 9);

        SetupFixedShift(employeeId, date);
        SetupPresent(employeeId, date);
        SetupHolidays(
            new Holiday { Name = "Araw ng Kagitingan", HolidayDate = date, HolidayType = HolidayType.RegularHoliday },
            new Holiday { Name = "Maundy Thursday", HolidayDate = date, HolidayType = HolidayType.RegularHoliday });

        var result = await _sut.BuildAsync([employeeId], date, date, CancellationToken.None);

        result.Inputs[employeeId].PremiumDays.Should().BeEquivalentTo(
            [new PremiumDayInput(WorkDayType.DoubleRegularHoliday, Days: 1m)]);
        result.Inputs[employeeId].HolidayRegularDays.Should().Be(1m);
    }

    [Fact]
    public async Task BuildAsync_TwoSpecialDaysOnOneDate_MakeADoubleSpecialDay()
    {
        var employeeId = Guid.NewGuid();
        var date = new DateOnly(2026, 11, 2);

        SetupFixedShift(employeeId, date);
        SetupPresent(employeeId, date);
        SetupHolidays(
            new Holiday { Name = "All Souls' Day", HolidayDate = date, HolidayType = HolidayType.SpecialNonWorking },
            new Holiday { Name = "City founding day", HolidayDate = date, HolidayType = HolidayType.SpecialNonWorking });

        var result = await _sut.BuildAsync([employeeId], date, date, CancellationToken.None);

        result.Inputs[employeeId].PremiumDays.Should().BeEquivalentTo(
            [new PremiumDayInput(WorkDayType.DoubleSpecialNonWorking, Days: 1m)]);
    }

    [Fact]
    public async Task BuildAsync_ARegularHolidayOnARestDay_IsPricedAsOne()
    {
        var employeeId = Guid.NewGuid();
        var saturday = new DateOnly(2026, 3, 7);

        SetupFixedShift(employeeId, new DateOnly(2026, 3, 2));
        SetupOvertime(employeeId, saturday, 480);
        SetupHolidays(new Holiday { Name = "Holiday", HolidayDate = saturday, HolidayType = HolidayType.RegularHoliday });

        var result = await _sut.BuildAsync([employeeId], saturday, saturday, CancellationToken.None);

        result.Inputs[employeeId].PremiumDays.Should().BeEquivalentTo(
            [new PremiumDayInput(WorkDayType.RegularHolidayOnRestDay, Hours: 8m)]);
    }

    [Fact]
    public async Task BuildAsync_OvertimeOnAWorkedHoliday_IsHolidayOvertime_NotOrdinary()
    {
        var employeeId = Guid.NewGuid();
        var date = new DateOnly(2026, 6, 12);   // Independence Day, a Friday

        SetupFixedShift(employeeId, date);
        SetupPresent(employeeId, date);
        SetupOvertime(employeeId, date, 120);
        SetupHolidays(new Holiday { Name = "Independence Day", HolidayDate = date, HolidayType = HolidayType.RegularHoliday });

        var result = await _sut.BuildAsync([employeeId], date, date, CancellationToken.None);

        result.Inputs[employeeId].PremiumDays.Should().BeEquivalentTo(
            [new PremiumDayInput(WorkDayType.RegularHoliday, Days: 1m, OvertimeHours: 2m)]);
    }

    [Fact]
    public async Task BuildAsync_NightHoursOnAHoliday_AreCountedAgainstTheHoliday()
    {
        var employeeId = Guid.NewGuid();
        var date = new DateOnly(2026, 6, 12);

        SetupFixedShift(employeeId, date);
        SetupHolidays(new Holiday { Name = "Independence Day", HolidayDate = date, HolidayType = HolidayType.RegularHoliday });
        _attendance.Setup(r => r.GetAllByPeriodAsync(
                       It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync([new AttendanceRecord
                   {
                       EmployeeId = employeeId, AttendanceDate = date, IsPresent = true,
                       TimeIn = new DateTime(2026, 6, 12, 22, 0, 0),
                       TimeOut = new DateTime(2026, 6, 13, 6, 0, 0)
                   }]);

        var result = await _sut.BuildAsync([employeeId], date, date, CancellationToken.None);

        result.Inputs[employeeId].PremiumDays.Should().BeEquivalentTo(
            [new PremiumDayInput(WorkDayType.RegularHoliday, Days: 1m, NightDiffHours: 8m)]);
    }

    [Fact]
    public async Task BuildAsync_ASpecialWorkingDay_IsAnOrdinaryWorkingDay()
    {
        var employeeId = Guid.NewGuid();
        var worked = new DateOnly(2026, 3, 2);
        var missed = new DateOnly(2026, 3, 3);

        SetupFixedShift(employeeId, worked);
        SetupPresent(employeeId, worked);
        SetupHolidays(
            new Holiday { Name = "Special working day", HolidayDate = worked, HolidayType = HolidayType.SpecialWorking },
            new Holiday { Name = "Special working day", HolidayDate = missed, HolidayType = HolidayType.SpecialWorking });

        var result = await _sut.BuildAsync([employeeId], worked, missed, CancellationToken.None);

        // No premium for working it, and missing it is an absence like any working day.
        result.Inputs[employeeId].PremiumDays.Should().BeEmpty();
        result.Inputs[employeeId].HolidaySpecialDays.Should().Be(0m);
        result.Inputs[employeeId].AbsenceDays.Should().Be(1m);
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

    // ─── Unworked double regular holidays ─────────────────────────────────────────────────────

    private static Holiday[] TwoRegularHolidaysOn(DateOnly date) =>
    [
        new Holiday { Name = "Araw ng Kagitingan", HolidayDate = date, HolidayType = HolidayType.RegularHoliday },
        new Holiday { Name = "Maundy Thursday", HolidayDate = date, HolidayType = HolidayType.RegularHoliday }
    ];

    [Fact]
    public async Task BuildAsync_AnUnworkedDoubleRegularHoliday_IsCountedAsAnUnworkedDay_AndIsNotAnAbsence()
    {
        var employeeId = Guid.NewGuid();
        var date = new DateOnly(2026, 4, 9);   // a Thursday: a scheduled working day

        SetupFixedShift(employeeId, date);
        SetupHolidays(TwoRegularHolidaysOn(date));

        var result = await _sut.BuildAsync([employeeId], date, date, CancellationToken.None);

        var input = result.Inputs[employeeId];
        input.PremiumDays.Should().BeEquivalentTo(
            [new PremiumDayInput(WorkDayType.DoubleRegularHoliday, UnworkedDays: 1m)]);
        // The salary already pays the first 100%; no absence claws it back.
        input.AbsenceDays.Should().Be(0m);
        // The display roll-up counts worked days only.
        input.HolidayRegularDays.Should().Be(0m);
    }

    [Fact]
    public async Task BuildAsync_AnUnworkedDoubleRegularHolidayOnARestDay_IsCountedWhenNoOvertimeIsApproved_AsItIsWithOvertime()
    {
        var employeeId = Guid.NewGuid();
        var saturday = new DateOnly(2026, 3, 7);   // a rest day under the Monday-to-Friday shift

        SetupFixedShift(employeeId, new DateOnly(2026, 3, 2));
        SetupHolidays(TwoRegularHolidaysOn(saturday));

        var result = await _sut.BuildAsync([employeeId], saturday, saturday, CancellationToken.None);

        result.Inputs[employeeId].PremiumDays.Should().BeEquivalentTo(
            [new PremiumDayInput(WorkDayType.DoubleRegularHolidayOnRestDay, UnworkedDays: 1m)]);
    }

    [Fact]
    public async Task BuildAsync_ADoubleRegularHolidayOnARestDay_WorkedThroughApprovedOvertime_CountsTheGuaranteedDayAndKeepsTheHours()
    {
        var employeeId = Guid.NewGuid();
        var saturday = new DateOnly(2026, 3, 7);

        SetupFixedShift(employeeId, new DateOnly(2026, 3, 2));
        SetupOvertime(employeeId, saturday, 480);
        SetupHolidays(TwoRegularHolidaysOn(saturday));

        var result = await _sut.BuildAsync([employeeId], saturday, saturday, CancellationToken.None);

        // The 200% is guaranteed whether or not the day is worked; the eight approved hours are
        // priced on top of it by the engine as the work premium.
        result.Inputs[employeeId].PremiumDays.Should().BeEquivalentTo(
            [new PremiumDayInput(WorkDayType.DoubleRegularHolidayOnRestDay, Hours: 8m, UnworkedDays: 1m)]);
    }

    [Fact]
    public async Task BuildAsync_ADoubleRegularHolidayOnARestDay_WithOvertimePastEightHours_CountsTheGuaranteedDayOnce()
    {
        var employeeId = Guid.NewGuid();
        var saturday = new DateOnly(2026, 3, 7);

        SetupFixedShift(employeeId, new DateOnly(2026, 3, 2));
        SetupOvertime(employeeId, saturday, 600);   // ten hours: eight at the day's rate, two overtime
        SetupHolidays(TwoRegularHolidaysOn(saturday));

        var result = await _sut.BuildAsync([employeeId], saturday, saturday, CancellationToken.None);

        result.Inputs[employeeId].PremiumDays.Should().BeEquivalentTo(
            [new PremiumDayInput(WorkDayType.DoubleRegularHolidayOnRestDay, Hours: 8m, OvertimeHours: 2m, UnworkedDays: 1m)]);
    }

    [Fact]
    public async Task BuildAsync_ADoubleRegularHolidayOnARestDay_WithAPresentRecordAndApprovedOvertime_CountsTheGuaranteedDayOnce()
    {
        var employeeId = Guid.NewGuid();
        var saturday = new DateOnly(2026, 3, 7);

        SetupFixedShift(employeeId, new DateOnly(2026, 3, 2));
        SetupPresent(employeeId, saturday);
        SetupOvertime(employeeId, saturday, 240);
        SetupHolidays(TwoRegularHolidaysOn(saturday));

        var result = await _sut.BuildAsync([employeeId], saturday, saturday, CancellationToken.None);

        result.Inputs[employeeId].PremiumDays.Should().BeEquivalentTo(
            [new PremiumDayInput(WorkDayType.DoubleRegularHolidayOnRestDay, Hours: 4m, UnworkedDays: 1m)]);
    }

    [Fact]
    public async Task BuildAsync_ADoubleRegularHolidayOnARestDay_WithAPresentRecordButNoApprovedOvertime_IsCounted()
    {
        var employeeId = Guid.NewGuid();
        var saturday = new DateOnly(2026, 3, 7);   // a rest day under the Monday-to-Friday shift

        SetupFixedShift(employeeId, new DateOnly(2026, 3, 2));
        SetupPresent(employeeId, saturday);
        SetupHolidays(TwoRegularHolidaysOn(saturday));

        var result = await _sut.BuildAsync([employeeId], saturday, saturday, CancellationToken.None);

        // Rest-day work comes only from approved overtime, so turning up unapproved earns no hours;
        // the guaranteed 200% still counts, so she is never paid less than for staying home.
        result.Inputs[employeeId].PremiumDays.Should().BeEquivalentTo(
            [new PremiumDayInput(WorkDayType.DoubleRegularHolidayOnRestDay, UnworkedDays: 1m)]);
    }

    [Fact]
    public async Task BuildAsync_ADoubleRegularHolidayOnAWorkingDay_WithApprovedOvertimeButNoPresentRecord_IsCountedAndItsOvertimeIsPriced()
    {
        var employeeId = Guid.NewGuid();
        var date = new DateOnly(2026, 4, 9);   // a Thursday: a scheduled working day

        SetupFixedShift(employeeId, date);
        SetupOvertime(employeeId, date, 120);
        SetupHolidays(TwoRegularHolidaysOn(date));

        var result = await _sut.BuildAsync([employeeId], date, date, CancellationToken.None);

        // On a working day only a present record is work, so approved overtime alone does not make
        // it worked: the day is counted, and its overtime is still priced as holiday overtime.
        result.Inputs[employeeId].PremiumDays.Should().BeEquivalentTo(
            [new PremiumDayInput(WorkDayType.DoubleRegularHoliday, OvertimeHours: 2m, UnworkedDays: 1m)]);
    }

    [Fact]
    public async Task BuildAsync_AWorkedDoubleRegularHoliday_IsNotCountedAsUnworked()
    {
        var employeeId = Guid.NewGuid();
        var date = new DateOnly(2026, 4, 9);

        SetupFixedShift(employeeId, date);
        SetupPresent(employeeId, date);
        SetupHolidays(TwoRegularHolidaysOn(date));

        var result = await _sut.BuildAsync([employeeId], date, date, CancellationToken.None);

        result.Inputs[employeeId].PremiumDays.Should().BeEquivalentTo(
            [new PremiumDayInput(WorkDayType.DoubleRegularHoliday, Days: 1m)]);
    }

    [Fact]
    public async Task BuildAsync_AnUnworkedDoubleSpecialDay_PaysNothingExtra_AndIsStillAnAbsence()
    {
        var employeeId = Guid.NewGuid();
        var date = new DateOnly(2026, 11, 2);

        SetupFixedShift(employeeId, date);
        SetupHolidays(
            new Holiday { Name = "All Souls' Day", HolidayDate = date, HolidayType = HolidayType.SpecialNonWorking },
            new Holiday { Name = "City founding day", HolidayDate = date, HolidayType = HolidayType.SpecialNonWorking });

        var result = await _sut.BuildAsync([employeeId], date, date, CancellationToken.None);

        result.Inputs[employeeId].PremiumDays.Should().BeEmpty();
        result.Inputs[employeeId].AbsenceDays.Should().Be(1m);
    }

    [Fact]
    public async Task BuildAsync_AnUnworkedSingleRegularHoliday_AddsNothing()
    {
        var employeeId = Guid.NewGuid();
        var date = new DateOnly(2026, 6, 12);

        SetupFixedShift(employeeId, date);
        SetupHolidays(new Holiday { Name = "Independence Day", HolidayDate = date, HolidayType = HolidayType.RegularHoliday });

        var result = await _sut.BuildAsync([employeeId], date, date, CancellationToken.None);

        result.Inputs[employeeId].PremiumDays.Should().BeEmpty();
        result.Inputs[employeeId].AbsenceDays.Should().Be(0m);
    }

    [Fact]
    public async Task BuildAsync_ADoubleRegularHolidayWithNoScheduleThatDate_AddsNothing()
    {
        var employeeId = Guid.NewGuid();
        var holiday = new DateOnly(2026, 4, 9);

        // The assignment starts the day after, so the holiday has no schedule behind it.
        SetupFixedShift(employeeId, holiday.AddDays(1));
        SetupHolidays(TwoRegularHolidaysOn(holiday));

        var result = await _sut.BuildAsync([employeeId], holiday, holiday.AddDays(1), CancellationToken.None);

        result.Inputs[employeeId].PremiumDays.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildAsync_OnePresentRecordAmongSeveralForTheDate_MakesTheDoubleHolidayWorked()
    {
        var employeeId = Guid.NewGuid();
        var date = new DateOnly(2026, 4, 9);

        SetupFixedShift(employeeId, date);
        SetupHolidays(TwoRegularHolidaysOn(date));
        _attendance.Setup(r => r.GetAllByPeriodAsync(
                       It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync([
                       new AttendanceRecord { EmployeeId = employeeId, AttendanceDate = date, IsPresent = false },
                       new AttendanceRecord { EmployeeId = employeeId, AttendanceDate = date, IsPresent = true }
                   ]);

        var result = await _sut.BuildAsync([employeeId], date, date, CancellationToken.None);

        result.Inputs[employeeId].PremiumDays.Should().BeEquivalentTo(
            [new PremiumDayInput(WorkDayType.DoubleRegularHoliday, Days: 1m)]);
    }

    [Fact]
    public async Task BuildAsync_ApprovedPaidLeaveOnAnUnworkedDoubleRegularHoliday_IsStillCounted()
    {
        var employeeId = Guid.NewGuid();
        var date = new DateOnly(2026, 4, 9);

        SetupFixedShift(employeeId, date);
        SetupHolidays(TwoRegularHolidaysOn(date));
        _leave.Setup(r => r.GetApprovedByPeriodAsync(
                  It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync([new LeaveRequest
              {
                  EmployeeId = employeeId, StartDate = date, EndDate = date,
                  Status = LeaveStatus.Approved,
                  LeaveType = new LeaveType { Name = "Vacation", IsPaid = true }
              }]);

        var result = await _sut.BuildAsync([employeeId], date, date, CancellationToken.None);

        result.Inputs[employeeId].PremiumDays.Should().BeEquivalentTo(
            [new PremiumDayInput(WorkDayType.DoubleRegularHoliday, UnworkedDays: 1m)]);
    }

    [Fact]
    public async Task BuildAsync_ApprovedUnpaidLeaveOnAnUnworkedDoubleRegularHoliday_IsCounted()
    {
        var employeeId = Guid.NewGuid();
        var date = new DateOnly(2026, 4, 9);

        SetupFixedShift(employeeId, date);
        SetupHolidays(TwoRegularHolidaysOn(date));
        _leave.Setup(r => r.GetApprovedByPeriodAsync(
                  It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync([new LeaveRequest
              {
                  EmployeeId = employeeId, StartDate = date, EndDate = date,
                  Status = LeaveStatus.Approved,
                  LeaveType = new LeaveType { Name = "Leave without pay", IsPaid = false }
              }]);

        var result = await _sut.BuildAsync([employeeId], date, date, CancellationToken.None);

        // The holiday's pay does not depend on leave, paid or not; and a regular holiday is never
        // an absence.
        var input = result.Inputs[employeeId];
        input.PremiumDays.Should().BeEquivalentTo(
            [new PremiumDayInput(WorkDayType.DoubleRegularHoliday, UnworkedDays: 1m)]);
        input.AbsenceDays.Should().Be(0m);
    }

    [Fact]
    public async Task BuildAsync_ADoubleRegularHolidayThatAlsoHasASpecialHolidayRow_IsCountedAsADoubleRegularHoliday()
    {
        var employeeId = Guid.NewGuid();
        var date = new DateOnly(2026, 4, 9);

        SetupFixedShift(employeeId, date);
        SetupHolidays([
            .. TwoRegularHolidaysOn(date),
            new Holiday { Name = "City founding day", HolidayDate = date, HolidayType = HolidayType.SpecialNonWorking }
        ]);

        var result = await _sut.BuildAsync([employeeId], date, date, CancellationToken.None);

        // A regular holiday outranks a special day on the same date, so the extra row changes nothing.
        result.Inputs[employeeId].PremiumDays.Should().BeEquivalentTo(
            [new PremiumDayInput(WorkDayType.DoubleRegularHoliday, UnworkedDays: 1m)]);
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

    // ─── The day-before rule (Labor Code Art. 94) ─────────────────────────────────────────────
    //
    // Every date's weekday below was checked against a calendar. The repository mocks in this
    // section answer only for the range they are asked about, as the real repositories do, so a
    // test that needs a date before the period proves the bridge asked for it.

    /// <summary>A Friday. The Monday-to-Friday shift from here covers every date in this section.</summary>
    private static readonly DateOnly ShiftStart = new(2026, 5, 1);

    private static Holiday RegularOn(DateOnly date, string name = "Regular holiday") =>
        new() { Name = name, HolidayDate = date, HolidayType = HolidayType.RegularHoliday };

    private static Holiday SpecialOn(DateOnly date, HolidayType type = HolidayType.SpecialNonWorking) =>
        new() { Name = "Special day", HolidayDate = date, HolidayType = type };

    private static AttendanceRecord PresentOn(Guid employeeId, DateOnly date) =>
        new() { EmployeeId = employeeId, AttendanceDate = date, IsPresent = true };

    private static LeaveRequest LeaveOn(Guid employeeId, DateOnly date, bool paid) => new()
    {
        EmployeeId = employeeId, StartDate = date, EndDate = date, Status = LeaveStatus.Approved,
        LeaveType = new LeaveType { Name = paid ? "Vacation" : "Leave without pay", IsPaid = paid }
    };

    private static EmployeeShiftAssignment FixedAssignment(Guid employeeId, DateOnly effectiveFrom, DateOnly? effectiveTo)
    {
        var assignment = FixedAssignment(employeeId, effectiveFrom);
        assignment.EffectiveTo = effectiveTo;
        return assignment;
    }

    private void SetupRecordsInRange(params AttendanceRecord[] records) =>
        _attendance.Setup(r => r.GetAllByPeriodAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync((DateOnly from, DateOnly to, CancellationToken _) =>
                       records.Where(r => r.AttendanceDate >= from && r.AttendanceDate <= to).ToList());

    private void SetupLeaveInRange(params LeaveRequest[] requests) =>
        _leave.Setup(r => r.GetApprovedByPeriodAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((DateOnly from, DateOnly to, CancellationToken _) =>
                  requests.Where(r => r.StartDate <= to && r.EndDate >= from).ToList());

    private void SetupAssignmentsInRange(params EmployeeShiftAssignment[] assignments) =>
        _assignments.Setup(r => r.GetActiveForPeriodAsync(
                        It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((IReadOnlyList<Guid> _, DateOnly from, DateOnly to, CancellationToken _) =>
                        assignments.Where(a => a.EffectiveFrom <= to && (a.EffectiveTo is null || a.EffectiveTo >= from)).ToList());

    // A Monday-to-Friday week with a regular holiday on the Wednesday.
    private static readonly DateOnly WeekMonday = new(2026, 7, 6);     // Monday
    private static readonly DateOnly WeekTuesday = new(2026, 7, 7);    // Tuesday: the qualifying day
    private static readonly DateOnly WeekHoliday = new(2026, 7, 8);    // Wednesday: the regular holiday
    private static readonly DateOnly WeekThursday = new(2026, 7, 9);   // Thursday
    private static readonly DateOnly WeekFriday = new(2026, 7, 10);    // Friday

    [Fact]
    public async Task BuildAsync_AnUnworkedRegularHoliday_AfterAnUnpaidAbsenceTheDayBefore_IsDeducted()
    {
        var employeeId = Guid.NewGuid();
        SetupAssignmentsInRange(FixedAssignment(employeeId, ShiftStart));
        SetupHolidays(RegularOn(WeekHoliday));
        SetupRecordsInRange(PresentOn(employeeId, WeekMonday), PresentOn(employeeId, WeekThursday), PresentOn(employeeId, WeekFriday));

        var result = await _sut.BuildAsync([employeeId], WeekMonday, WeekFriday, CancellationToken.None);

        // Tuesday is an ordinary absence; the holiday after it is not paid, so it is one too.
        result.Inputs[employeeId].AbsenceDays.Should().Be(2m);
        result.Inputs[employeeId].PremiumDays.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildAsync_AnUnworkedRegularHoliday_WhenPresentTheDayBefore_IsPaid()
    {
        var employeeId = Guid.NewGuid();
        SetupAssignmentsInRange(FixedAssignment(employeeId, ShiftStart));
        SetupHolidays(RegularOn(WeekHoliday));
        SetupRecordsInRange(
            PresentOn(employeeId, WeekMonday), PresentOn(employeeId, WeekTuesday),
            PresentOn(employeeId, WeekThursday), PresentOn(employeeId, WeekFriday));

        var result = await _sut.BuildAsync([employeeId], WeekMonday, WeekFriday, CancellationToken.None);

        result.Inputs[employeeId].AbsenceDays.Should().Be(0m);
        result.Inputs[employeeId].PremiumDays.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, 0)]    // approved paid leave on Tuesday: the holiday is paid
    [InlineData(false, 2)]   // approved unpaid leave on Tuesday: absent without pay, both deducted
    public async Task BuildAsync_AnUnworkedRegularHoliday_AfterApprovedLeaveTheDayBefore_IsPaidOnlyWhenTheLeaveIsPaid(
        bool paid, decimal expectedAbsences)
    {
        var employeeId = Guid.NewGuid();
        SetupAssignmentsInRange(FixedAssignment(employeeId, ShiftStart));
        SetupHolidays(RegularOn(WeekHoliday));
        SetupRecordsInRange(PresentOn(employeeId, WeekMonday), PresentOn(employeeId, WeekThursday), PresentOn(employeeId, WeekFriday));
        SetupLeaveInRange(LeaveOn(employeeId, WeekTuesday, paid));

        var result = await _sut.BuildAsync([employeeId], WeekMonday, WeekFriday, CancellationToken.None);

        result.Inputs[employeeId].AbsenceDays.Should().Be(expectedAbsences);
    }

    [Theory]
    [InlineData(true, 0)]    // present on the Friday: the Monday holiday is paid
    [InlineData(false, 2)]   // absent on the Friday: the Friday and the Monday holiday are deducted
    public async Task BuildAsync_AnUnworkedRegularHolidayOnAMonday_LooksPastTheWeekendToTheFriday(
        bool presentOnFriday, decimal expectedAbsences)
    {
        var employeeId = Guid.NewGuid();
        var friday = new DateOnly(2026, 7, 10);    // Friday
        var monday = new DateOnly(2026, 7, 13);    // Monday: the regular holiday; Sat 11 and Sun 12 are rest days

        SetupAssignmentsInRange(FixedAssignment(employeeId, ShiftStart));
        SetupHolidays(RegularOn(monday));
        SetupRecordsInRange(presentOnFriday ? [PresentOn(employeeId, friday)] : []);

        var result = await _sut.BuildAsync([employeeId], friday, monday, CancellationToken.None);

        // Saturday and Sunday carry no record; were they not skipped as rest days, the holiday
        // would be deducted even with the Friday worked.
        result.Inputs[employeeId].AbsenceDays.Should().Be(expectedAbsences);
    }

    // Holy Week 2026: Holy Thursday and Good Friday are consecutive regular holidays.
    private static readonly DateOnly HolyMonday = new(2026, 3, 30);      // Monday
    private static readonly DateOnly HolyTuesday = new(2026, 3, 31);     // Tuesday
    private static readonly DateOnly HolyWednesday = new(2026, 4, 1);    // Wednesday: the qualifying day for both
    private static readonly DateOnly HolyThursday = new(2026, 4, 2);     // Thursday: a regular holiday
    private static readonly DateOnly GoodFriday = new(2026, 4, 3);       // Friday: a regular holiday

    private void SetupHolyWeek() =>
        SetupHolidays(RegularOn(HolyThursday, "Maundy Thursday"), RegularOn(GoodFriday, "Good Friday"));

    [Fact]
    public async Task BuildAsync_ConsecutiveRegularHolidays_WhenPresentTheWednesday_AreBothPaid()
    {
        var employeeId = Guid.NewGuid();
        SetupAssignmentsInRange(FixedAssignment(employeeId, ShiftStart.AddMonths(-2)));
        SetupHolyWeek();
        SetupRecordsInRange(PresentOn(employeeId, HolyMonday), PresentOn(employeeId, HolyTuesday), PresentOn(employeeId, HolyWednesday));

        var result = await _sut.BuildAsync([employeeId], HolyMonday, GoodFriday, CancellationToken.None);

        // Good Friday walks back past Holy Thursday (a holiday) to the Wednesday.
        result.Inputs[employeeId].AbsenceDays.Should().Be(0m);
    }

    [Fact]
    public async Task BuildAsync_ConsecutiveRegularHolidays_WhenAbsentTheWednesday_AreBothDeducted()
    {
        var employeeId = Guid.NewGuid();
        SetupAssignmentsInRange(FixedAssignment(employeeId, ShiftStart.AddMonths(-2)));
        SetupHolyWeek();
        SetupRecordsInRange(PresentOn(employeeId, HolyMonday), PresentOn(employeeId, HolyTuesday));

        var result = await _sut.BuildAsync([employeeId], HolyMonday, GoodFriday, CancellationToken.None);

        // The Wednesday, Holy Thursday and Good Friday.
        result.Inputs[employeeId].AbsenceDays.Should().Be(3m);
    }

    [Fact]
    public async Task BuildAsync_ConsecutiveRegularHolidays_WhenHolyThursdayIsWorked_GoodFridayIsPaidDespiteTheAbsentWednesday()
    {
        var employeeId = Guid.NewGuid();
        SetupAssignmentsInRange(FixedAssignment(employeeId, ShiftStart.AddMonths(-2)));
        SetupHolyWeek();
        SetupRecordsInRange(PresentOn(employeeId, HolyMonday), PresentOn(employeeId, HolyTuesday), PresentOn(employeeId, HolyThursday));

        var result = await _sut.BuildAsync([employeeId], HolyMonday, GoodFriday, CancellationToken.None);

        // Only the Wednesday is deducted: Holy Thursday was worked (paid as worked, whatever the
        // day before), and it satisfies the rule for Good Friday.
        var input = result.Inputs[employeeId];
        input.AbsenceDays.Should().Be(1m);
        input.PremiumDays.Should().BeEquivalentTo([new PremiumDayInput(WorkDayType.RegularHoliday, Days: 1m)]);
    }

    [Theory]
    [InlineData("present", 0)]
    [InlineData("paid leave", 0)]
    [InlineData("absent", 1)]
    public async Task BuildAsync_ARegularHolidayOnTheFirstDayOfThePeriod_IsJudgedByTheLastWorkdayOfThePreviousPeriod(
        string friday, decimal expectedAbsences)
    {
        var employeeId = Guid.NewGuid();
        var previousFriday = new DateOnly(2026, 7, 10);   // Friday: in the previous period
        var from = new DateOnly(2026, 7, 13);             // Monday: the regular holiday, first day of the period
        var to = new DateOnly(2026, 7, 17);               // Friday

        // The Friday's schedule comes from an assignment that ended with it, which only a
        // look-back load returns; the period itself runs on a new one.
        SetupAssignmentsInRange(
            FixedAssignment(employeeId, ShiftStart, previousFriday),
            FixedAssignment(employeeId, from));
        SetupHolidays(RegularOn(from));
        var inPeriod = Enumerable.Range(1, 4).Select(i => PresentOn(employeeId, from.AddDays(i)));   // Tue 14 to Fri 17
        SetupRecordsInRange([.. inPeriod, .. friday == "present" ? [PresentOn(employeeId, previousFriday)] : Array.Empty<AttendanceRecord>()]);
        if (friday == "paid leave") SetupLeaveInRange(LeaveOn(employeeId, previousFriday, paid: true));

        var result = await _sut.BuildAsync([employeeId], from, to, CancellationToken.None);

        // Absent on the Friday, only the holiday is deducted here: the Friday itself belongs to
        // the previous period and is never counted in this one.
        result.Inputs[employeeId].AbsenceDays.Should().Be(expectedAbsences);
    }

    [Theory]
    [InlineData(2026, 6, 23, 0)]   // Tuesday, 15 days before: beyond the walk, so the employee stays entitled
    [InlineData(2026, 6, 24, 1)]   // Wednesday, 14 days before: the last day the walk reaches, absent
    public async Task BuildAsync_TheWalkStopsAfter14Days_AndThenTheEmployeeStaysEntitled(
        int year, int month, int day, decimal expectedAbsences)
    {
        var employeeId = Guid.NewGuid();
        var lastScheduledDay = new DateOnly(year, month, day);
        var holiday = new DateOnly(2026, 7, 8);   // Wednesday

        // Nothing covers the days between the two assignments, so the walk skips them. Both are
        // returned whatever the range asked for, so it is the walk's own limit that is tested.
        _assignments.Setup(r => r.GetActiveForPeriodAsync(
                        It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync([FixedAssignment(employeeId, ShiftStart, lastScheduledDay), FixedAssignment(employeeId, holiday)]);
        SetupHolidays(RegularOn(holiday));

        var result = await _sut.BuildAsync([employeeId], holiday, holiday, CancellationToken.None);

        result.Inputs[employeeId].AbsenceDays.Should().Be(expectedAbsences);
    }

    [Fact]
    public async Task BuildAsync_AWorkedRegularHoliday_AfterAnAbsentDay_IsPaidAsWorked()
    {
        var employeeId = Guid.NewGuid();
        SetupAssignmentsInRange(FixedAssignment(employeeId, ShiftStart));
        SetupHolidays(RegularOn(WeekHoliday));
        SetupRecordsInRange(PresentOn(employeeId, WeekHoliday));

        var result = await _sut.BuildAsync([employeeId], WeekTuesday, WeekHoliday, CancellationToken.None);

        // Only Tuesday is deducted; the worked holiday earns its premium as before.
        var input = result.Inputs[employeeId];
        input.AbsenceDays.Should().Be(1m);
        input.PremiumDays.Should().BeEquivalentTo([new PremiumDayInput(WorkDayType.RegularHoliday, Days: 1m)]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BuildAsync_AnUnworkedDoubleRegularHoliday_CountsItsGuaranteedDayOnlyWhenEntitled(bool presentTheDayBefore)
    {
        var employeeId = Guid.NewGuid();
        var wednesday = new DateOnly(2026, 4, 8);   // Wednesday: the qualifying day
        var holiday = new DateOnly(2026, 4, 9);     // Thursday: a double regular holiday

        SetupAssignmentsInRange(FixedAssignment(employeeId, ShiftStart.AddMonths(-2)));
        SetupHolidays(TwoRegularHolidaysOn(holiday));
        SetupRecordsInRange(presentTheDayBefore ? [PresentOn(employeeId, wednesday)] : []);

        var result = await _sut.BuildAsync([employeeId], wednesday, holiday, CancellationToken.None);

        var input = result.Inputs[employeeId];
        if (presentTheDayBefore)
        {
            input.AbsenceDays.Should().Be(0m);
            input.PremiumDays.Should().BeEquivalentTo([new PremiumDayInput(WorkDayType.DoubleRegularHoliday, UnworkedDays: 1m)]);
        }
        else
        {
            // Not entitled: the holiday pays nothing for the day - one absence, no guaranteed day.
            input.AbsenceDays.Should().Be(2m);
            input.PremiumDays.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(true, 1)]    // the special day alone: no work, no pay, as before
    [InlineData(false, 2)]   // Tuesday and the special day: nothing more is added for the absent Tuesday
    public async Task BuildAsync_AnUnworkedSpecialNonWorkingDay_IsUnchangedByTheDayBefore(bool presentTuesday, decimal expectedAbsences)
    {
        var employeeId = Guid.NewGuid();
        SetupAssignmentsInRange(FixedAssignment(employeeId, ShiftStart));
        SetupHolidays(SpecialOn(WeekHoliday));
        SetupRecordsInRange(presentTuesday ? [PresentOn(employeeId, WeekTuesday)] : []);

        var result = await _sut.BuildAsync([employeeId], WeekTuesday, WeekHoliday, CancellationToken.None);

        result.Inputs[employeeId].AbsenceDays.Should().Be(expectedAbsences);
        result.Inputs[employeeId].PremiumDays.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildAsync_TheWalkSkipsASpecialNonWorkingDay_ButNotASpecialWorkingDay()
    {
        var skipped = Guid.NewGuid();
        var notSkipped = Guid.NewGuid();
        // Tuesday present, Wednesday (WeekHoliday) a special day not worked, Thursday a regular holiday.
        SetupAssignmentsInRange(FixedAssignment(skipped, ShiftStart), FixedAssignment(notSkipped, ShiftStart));
        SetupRecordsInRange(PresentOn(skipped, WeekTuesday), PresentOn(notSkipped, WeekTuesday));

        SetupHolidays(SpecialOn(WeekHoliday), RegularOn(WeekThursday));
        var nonWorking = await _sut.BuildAsync([skipped], WeekTuesday, WeekThursday, CancellationToken.None);

        SetupHolidays(SpecialOn(WeekHoliday, HolidayType.SpecialWorking), RegularOn(WeekThursday));
        var working = await _sut.BuildAsync([notSkipped], WeekTuesday, WeekThursday, CancellationToken.None);

        // A special non-working day is skipped back to the worked Tuesday: only the special day
        // itself is an absence. A special working day is an ordinary working day: missed, it is the
        // qualifying day, so the regular holiday after it is deducted too.
        nonWorking.Inputs[skipped].AbsenceDays.Should().Be(1m);
        working.Inputs[notSkipped].AbsenceDays.Should().Be(2m);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public async Task BuildAsync_APeriodStartingOnNewYearsDay_WalksBackIntoThePreviousYearsHolidays(
        bool presentMonday, decimal expectedAbsences)
    {
        var employeeId = Guid.NewGuid();
        var monday = new DateOnly(2025, 12, 29);       // Monday: the qualifying day
        var rizalDay = new DateOnly(2025, 12, 30);     // Tuesday: a regular holiday, not worked
        var lastDay = new DateOnly(2025, 12, 31);      // Wednesday: a special non-working day, not worked
        var newYear = new DateOnly(2026, 1, 1);        // Thursday: a regular holiday, first day of the period
        var friday = new DateOnly(2026, 1, 2);         // Friday

        SetupAssignmentsInRange(FixedAssignment(employeeId, new DateOnly(2025, 12, 1)));
        _holidays.Setup(r => r.GetByYearAsync(2025, It.IsAny<CancellationToken>()))
                 .ReturnsAsync([RegularOn(rizalDay, "Rizal Day"), SpecialOn(lastDay)]);
        _holidays.Setup(r => r.GetByYearAsync(2026, It.IsAny<CancellationToken>()))
                 .ReturnsAsync([RegularOn(newYear, "New Year's Day")]);
        SetupRecordsInRange([PresentOn(employeeId, friday), .. presentMonday ? [PresentOn(employeeId, monday)] : Array.Empty<AttendanceRecord>()]);

        var result = await _sut.BuildAsync([employeeId], newYear, friday, CancellationToken.None);

        // Were the previous year's holidays not loaded, Dec 31 would be an unattended working day
        // and New Year's Day would be deducted even with the Monday worked.
        result.Inputs[employeeId].AbsenceDays.Should().Be(expectedAbsences);
    }

    [Fact]
    public async Task BuildAsync_AForfeitedRegularHoliday_CoveredByApprovedPaidLeave_StillBooksItsAbsence()
    {
        var employeeId = Guid.NewGuid();
        SetupAssignmentsInRange(FixedAssignment(employeeId, ShiftStart));
        SetupHolidays(RegularOn(WeekHoliday));
        // Tuesday, before the period, has no record: absent without pay, so the holiday is forfeited.
        SetupRecordsInRange();
        SetupLeaveInRange(LeaveOn(employeeId, WeekHoliday, paid: true));

        var result = await _sut.BuildAsync([employeeId], WeekHoliday, WeekHoliday, CancellationToken.None);

        // The leave does not pay for a forfeited holiday: its one absence is booked all the same.
        result.Inputs[employeeId].AbsenceDays.Should().Be(1m);
        result.Inputs[employeeId].PremiumDays.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildAsync_AForfeitedDoubleRegularHoliday_CoveredByApprovedPaidLeave_BooksOneAbsenceAndNoGuaranteedDay()
    {
        var employeeId = Guid.NewGuid();
        SetupAssignmentsInRange(FixedAssignment(employeeId, ShiftStart));
        SetupHolidays(TwoRegularHolidaysOn(WeekHoliday));
        SetupRecordsInRange();
        SetupLeaveInRange(LeaveOn(employeeId, WeekHoliday, paid: true));

        var result = await _sut.BuildAsync([employeeId], WeekHoliday, WeekHoliday, CancellationToken.None);

        result.Inputs[employeeId].AbsenceDays.Should().Be(1m);
        result.Inputs[employeeId].PremiumDays.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildAsync_AnEntitledRegularHoliday_CoveredByApprovedPaidLeave_BooksNoAbsence()
    {
        var employeeId = Guid.NewGuid();
        SetupAssignmentsInRange(FixedAssignment(employeeId, ShiftStart));
        SetupHolidays(RegularOn(WeekHoliday));
        SetupRecordsInRange(PresentOn(employeeId, WeekTuesday));
        SetupLeaveInRange(LeaveOn(employeeId, WeekHoliday, paid: true));

        var result = await _sut.BuildAsync([employeeId], WeekHoliday, WeekHoliday, CancellationToken.None);

        result.Inputs[employeeId].AbsenceDays.Should().Be(0m);
        result.Inputs[employeeId].PremiumDays.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildAsync_ARegularHolidayOnARestDay_AfterAnAbsentWorkday_IsUntouchedByTheDayBeforeRule()
    {
        var employeeId = Guid.NewGuid();
        var saturday = new DateOnly(2026, 7, 11);   // a rest day; the Friday before has no record

        SetupAssignmentsInRange(FixedAssignment(employeeId, ShiftStart));
        SetupHolidays(RegularOn(saturday));
        SetupRecordsInRange();

        var result = await _sut.BuildAsync([employeeId], saturday, saturday, CancellationToken.None);

        result.Inputs[employeeId].AbsenceDays.Should().Be(0m);
        result.Inputs[employeeId].PremiumDays.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildAsync_ADoubleRegularHolidayOnARestDay_AfterAnAbsentWorkday_KeepsItsGuaranteedDay()
    {
        var employeeId = Guid.NewGuid();
        var saturday = new DateOnly(2026, 7, 11);   // a rest day; the Friday before has no record

        SetupAssignmentsInRange(FixedAssignment(employeeId, ShiftStart));
        SetupHolidays(TwoRegularHolidaysOn(saturday));
        SetupRecordsInRange();

        var result = await _sut.BuildAsync([employeeId], saturday, saturday, CancellationToken.None);

        result.Inputs[employeeId].AbsenceDays.Should().Be(0m);
        result.Inputs[employeeId].PremiumDays.Should().BeEquivalentTo(
            [new PremiumDayInput(WorkDayType.DoubleRegularHolidayOnRestDay, UnworkedDays: 1m)]);
    }

    [Fact]
    public async Task BuildAsync_RecordsBeforeThePeriod_AddNothingToThePeriodsFigures()
    {
        var employeeId = Guid.NewGuid();
        var from = new DateOnly(2026, 7, 13);   // Monday
        var to = new DateOnly(2026, 7, 14);     // Tuesday

        SetupAssignmentsInRange(FixedAssignment(employeeId, ShiftStart));
        // A worked regular holiday on Thursday Jul 9, before the period.
        SetupHolidays(RegularOn(new DateOnly(2026, 7, 9)));
        SetupRecordsInRange(
            // Before the period: Thursday Jul 9 (a worked holiday) and Friday Jul 10, a late,
            // short night shift. Wednesday Jul 8 has no record at all.
            new AttendanceRecord
            {
                EmployeeId = employeeId, AttendanceDate = new DateOnly(2026, 7, 9), IsPresent = true,
                LateMinutes = 30, UndertimeMinutes = 20,
                TimeIn = new DateTime(2026, 7, 9, 22, 0, 0), TimeOut = new DateTime(2026, 7, 10, 6, 0, 0)
            },
            new AttendanceRecord
            {
                EmployeeId = employeeId, AttendanceDate = new DateOnly(2026, 7, 10), IsPresent = true,
                LateMinutes = 40, UndertimeMinutes = 25,
                TimeIn = new DateTime(2026, 7, 10, 22, 0, 0), TimeOut = new DateTime(2026, 7, 11, 6, 0, 0)
            },
            new AttendanceRecord { EmployeeId = employeeId, AttendanceDate = from, IsPresent = true, LateMinutes = 5, UndertimeMinutes = 3 },
            new AttendanceRecord { EmployeeId = employeeId, AttendanceDate = to, IsPresent = true, LateMinutes = 5, UndertimeMinutes = 3 });

        var result = await _sut.BuildAsync([employeeId], from, to, CancellationToken.None);

        var input = result.Inputs[employeeId];
        input.LateMinutes.Should().Be(10m);
        input.UndertimeMinutes.Should().Be(6m);
        input.NightDiffHours.Should().Be(0m);
        input.AbsenceDays.Should().Be(0m);
        input.PremiumDays.Should().BeEmpty();
        input.HolidayRegularDays.Should().Be(0m);
    }
}
