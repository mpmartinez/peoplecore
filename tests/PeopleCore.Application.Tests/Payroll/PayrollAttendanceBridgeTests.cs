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
}
