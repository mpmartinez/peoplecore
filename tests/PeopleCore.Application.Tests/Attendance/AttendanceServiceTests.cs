using System.Text.Json;
using FluentAssertions;
using Moq;
using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Attendance.Services;
using PeopleCore.Application.Common.Time;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Scheduling.DTOs;
using PeopleCore.Application.Scheduling.Interfaces;
using PeopleCore.Application.Tests.Common;
using PeopleCore.Domain.Entities.Attendance;
using PeopleCore.Domain.Entities.Employees;
using M2NET.Core.Enums;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Attendance;

/// <summary>
/// Clocking in and out is stamped with the server's clock, read as Philippine wall-clock time and
/// stored labelled UTC (<c>08:07Z</c> means 08:07 in Manila) - the same convention the device sync
/// and the CSV import use. The clock below holds a real UTC instant; the expectations are Manila times.
/// </summary>
public class AttendanceServiceTests
{
    private readonly Mock<IAttendanceRepository> _repo = new();
    private readonly Mock<IHolidayService> _holidayService = new();
    private readonly Mock<IEmployeeRepository> _employeeRepo = new();
    private readonly Mock<IShiftService> _shiftService = new();
    private readonly FixedClock _clock = new("2026-03-12T00:07:00Z"); // 08:07 in Manila
    private readonly Employee _emp = MakeEmployee();
    private readonly AttendanceService _sut;

    public AttendanceServiceTests()
    {
        // Default: no shift assigned - falls back to the 08:00 start.
        _shiftService.Setup(s => s.ResolveShiftForDayAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync((DailyScheduleDto?)null);
        _employeeRepo.Setup(r => r.GetByIdAsync(_emp.Id, default)).ReturnsAsync(_emp);
        _holidayService.Setup(h => h.IsHolidayAsync(It.IsAny<DateOnly>(), default)).ReturnsAsync((HolidayType?)null);
        _repo.Setup(r => r.AddAsync(It.IsAny<AttendanceRecord>(), default))
             .ReturnsAsync((AttendanceRecord a, CancellationToken _) => a);
        _repo.Setup(r => r.UpdateAsync(It.IsAny<AttendanceRecord>(), default)).Returns(Task.CompletedTask);

        _sut = new AttendanceService(_repo.Object, _holidayService.Object, _employeeRepo.Object, _shiftService.Object, _clock);
    }

    private static Employee MakeEmployee(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        EmployeeNumber = "EMP-001",
        FirstName = "Juan",
        LastName = "dela Cruz",
        DateOfBirth = new DateOnly(1990, 1, 1),
        Gender = Gender.Male,
        WorkEmail = "juan@test.com",
        EmploymentType = EmploymentType.Regular,
        HireDate = new DateOnly(2020, 1, 1),
        IsActive = true
    };

    /// <summary>A Manila wall-clock time, labelled UTC as the database stores it.</summary>
    private static DateTime Wall(int year, int month, int day, int hour, int minute)
        => new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    /// <summary>A record already clocked in at <paramref name="timeIn"/>, found under its own date.</summary>
    private AttendanceRecord ClockedInAt(DateTime timeIn)
    {
        var record = new AttendanceRecord
        {
            EmployeeId = _emp.Id,
            AttendanceDate = DateOnly.FromDateTime(timeIn),
            TimeIn = timeIn,
            IsPresent = true
        };
        _repo.Setup(r => r.GetByEmployeeAndDateAsync(_emp.Id, record.AttendanceDate, default)).ReturnsAsync(record);
        return record;
    }

    [Fact]
    public async Task TimeInAsync_WhenEmployeeNotFound_ThrowsKeyNotFoundException()
    {
        var act = () => _sut.TimeInAsync(new TimeInRequest(Guid.NewGuid()));

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task TimeInAsync_WhenAlreadyClockedIn_ThrowsDomainException()
    {
        ClockedInAt(Wall(2026, 3, 12, 7, 50));

        var act = () => _sut.TimeInAsync(new TimeInRequest(_emp.Id));

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("*already clocked in*");
    }

    [Fact]
    public async Task TimeOutAsync_WhenNotClockedIn_ThrowsDomainException()
    {
        _repo.Setup(r => r.GetByEmployeeAndDateAsync(_emp.Id, It.IsAny<DateOnly>(), default))
             .ReturnsAsync((AttendanceRecord?)null);

        var act = () => _sut.TimeOutAsync(new TimeOutRequest(_emp.Id));

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("*not clocked in*");
    }

    [Fact]
    public async Task TimeInAsync_At0807Manila_RecordsThatDayAndWallClock_SevenMinutesLate()
    {
        var result = await _sut.TimeInAsync(new TimeInRequest(_emp.Id));

        result.AttendanceDate.Should().Be(new DateOnly(2026, 3, 12));
        result.TimeIn.Should().Be(Wall(2026, 3, 12, 8, 7));
        result.TimeIn!.Value.Kind.Should().Be(DateTimeKind.Utc);
        result.LateMinutes.Should().Be(7);
        _repo.Verify(r => r.GetByEmployeeAndDateAsync(_emp.Id, new DateOnly(2026, 3, 12), default), Times.Once);
    }

    [Fact]
    public async Task TimeInAsync_At0755Manila_WhichIsStillYesterdayInUtc_RecordsTheManilaDay_NotLate()
    {
        _clock.Now = DateTimeOffset.Parse("2026-03-11T23:55:00Z");

        var result = await _sut.TimeInAsync(new TimeInRequest(_emp.Id));

        result.AttendanceDate.Should().Be(new DateOnly(2026, 3, 12));
        result.TimeIn.Should().Be(Wall(2026, 3, 12, 7, 55));
        result.LateMinutes.Should().Be(0);
    }

    [Fact]
    public async Task TimeInAsync_At0905Manila_Is65MinutesLate()
    {
        _clock.Now = DateTimeOffset.Parse("2025-01-06T01:05:00Z");

        var result = await _sut.TimeInAsync(new TimeInRequest(_emp.Id));

        result.LateMinutes.Should().Be(65);
    }

    [Theory]
    [InlineData("2026-03-12T09:00:00Z", 0, 0)]    // 17:00 Manila
    [InlineData("2026-03-12T08:30:00Z", 30, 0)]   // 16:30 Manila
    [InlineData("2026-03-12T08:00:00Z", 60, 0)]   // 16:00 Manila
    [InlineData("2026-03-12T11:00:00Z", 0, 120)]  // 19:00 Manila
    public async Task TimeOutAsync_ReadsUndertimeAndOvertimeOffTheManilaWallClock(string instant, int undertime, int overtime)
    {
        ClockedInAt(Wall(2026, 3, 12, 8, 0));
        _clock.Now = DateTimeOffset.Parse(instant);

        var result = await _sut.TimeOutAsync(new TimeOutRequest(_emp.Id));

        result.TimeOut.Should().Be(PhilippineTime.Now(_clock));
        result.UndertimeMinutes.Should().Be(undertime);
        result.OvertimeMinutes.Should().Be(overtime);
    }

    [Fact]
    public async Task ClockingInBefore0800Manila_CanStillClockOutThatAfternoon()
    {
        // 07:55 Manila is 23:55 UTC the previous day. Filed under the UTC date, the time-in would
        // sit on a different day from the 17:00 time-out, and the time-out would find no record.
        var record = ClockedInAt(Wall(2026, 3, 12, 7, 55));
        _clock.Now = DateTimeOffset.Parse("2026-03-12T09:00:00Z");

        var result = await _sut.TimeOutAsync(new TimeOutRequest(_emp.Id));

        result.Id.Should().Be(record.Id);
        result.TimeOut.Should().Be(Wall(2026, 3, 12, 17, 0));
        result.UndertimeMinutes.Should().Be(0);
    }

    [Fact]
    public async Task ATimeSentByTheClient_IsIgnored()
    {
        // An installed PWA that has not refreshed still sends its own clock - and anyone can send
        // any time they like. The body still binds (unknown JSON properties are skipped under
        // ASP.NET Core's web defaults) and the server's clock is what gets recorded.
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var timeIn = JsonSerializer.Deserialize<TimeInRequest>(
            $$"""{"employeeId":"{{_emp.Id}}","timeIn":"2000-01-01T00:00:00Z"}""", web)!;
        var timeOut = JsonSerializer.Deserialize<TimeOutRequest>(
            $$"""{"employeeId":"{{_emp.Id}}","timeOut":"2000-01-01T23:59:00Z"}""", web)!;

        var clockedIn = await _sut.TimeInAsync(timeIn);
        ClockedInAt(clockedIn.TimeIn!.Value);
        _clock.Now = DateTimeOffset.Parse("2026-03-12T09:00:00Z");
        var clockedOut = await _sut.TimeOutAsync(timeOut);

        timeIn.EmployeeId.Should().Be(_emp.Id);
        clockedIn.AttendanceDate.Should().Be(new DateOnly(2026, 3, 12));
        clockedIn.TimeIn.Should().Be(Wall(2026, 3, 12, 8, 7));
        clockedOut.TimeOut.Should().Be(Wall(2026, 3, 12, 17, 0));
    }

    // ─── Measured against the employee's own shift ───────────────────────────────────────────

    private void OnShift(DateOnly date, TimeOnly start, TimeOnly end) =>
        _shiftService.Setup(s => s.ResolveShiftForDayAsync(_emp.Id, date, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new DailyScheduleDto(date, "Shift", start, end, IsRestDay: false,
                         IsNightShift: end <= start));

    private void OnRestDay(DateOnly date) =>
        _shiftService.Setup(s => s.ResolveShiftForDayAsync(_emp.Id, date, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new DailyScheduleDto(date, null, null, null, IsRestDay: true, IsNightShift: false));

    [Theory]
    [InlineData("2026-03-12T08:00:00Z", 0, 0)]    // 16:00 Manila - the shift's end
    [InlineData("2026-03-12T07:30:00Z", 30, 0)]   // 15:30 - half an hour early
    [InlineData("2026-03-12T09:00:00Z", 0, 60)]   // 17:00 - an hour past the shift
    public async Task TimeOutAsync_MeasuresUndertimeAndOvertimeFromTheShiftsEnd_NotFivePm(
        string instant, int undertime, int overtime)
    {
        OnShift(new DateOnly(2026, 3, 12), new TimeOnly(7, 0), new TimeOnly(16, 0));
        ClockedInAt(Wall(2026, 3, 12, 7, 0));
        _clock.Now = DateTimeOffset.Parse(instant);

        var result = await _sut.TimeOutAsync(new TimeOutRequest(_emp.Id));

        result.UndertimeMinutes.Should().Be(undertime);
        result.OvertimeMinutes.Should().Be(overtime);
    }

    [Fact]
    public async Task TimeOutAsync_ANightShiftClocksOutTheNextMorning_OnLastNightsRecord()
    {
        var night = new DateOnly(2026, 3, 11);
        OnShift(night, new TimeOnly(22, 0), new TimeOnly(6, 0));
        var record = ClockedInAt(Wall(2026, 3, 11, 22, 0));
        _repo.Setup(r => r.GetByEmployeeAndDateAsync(_emp.Id, new DateOnly(2026, 3, 12), default))
             .ReturnsAsync((AttendanceRecord?)null);
        _clock.Now = DateTimeOffset.Parse("2026-03-11T21:30:00Z");   // 05:30 on the 12th in Manila

        var result = await _sut.TimeOutAsync(new TimeOutRequest(_emp.Id));

        result.Id.Should().Be(record.Id);
        result.TimeOut.Should().Be(Wall(2026, 3, 12, 5, 30));
        result.UndertimeMinutes.Should().Be(30, "the shift ends at 06:00 the next morning");
        result.OvertimeMinutes.Should().Be(0);
    }

    [Fact]
    public async Task TimeOutAsync_LongAfterLastNightsShiftEnded_IsNotTakenAsItsTimeOut()
    {
        var night = new DateOnly(2026, 3, 11);
        OnShift(night, new TimeOnly(22, 0), new TimeOnly(6, 0));
        ClockedInAt(Wall(2026, 3, 11, 22, 0));
        _repo.Setup(r => r.GetByEmployeeAndDateAsync(_emp.Id, new DateOnly(2026, 3, 12), default))
             .ReturnsAsync((AttendanceRecord?)null);
        _clock.Now = DateTimeOffset.Parse("2026-03-12T12:00:00Z");   // 20:00 on the 12th

        var act = () => _sut.TimeOutAsync(new TimeOutRequest(_emp.Id));

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task ARestDay_HasNoLatenessOrUndertime_AndAllItsTimeIsOvertime()
    {
        var sunday = new DateOnly(2026, 3, 15);
        OnRestDay(sunday);
        _repo.Setup(r => r.GetByEmployeeAndDateAsync(_emp.Id, sunday, default)).ReturnsAsync((AttendanceRecord?)null);
        _clock.Now = DateTimeOffset.Parse("2026-03-15T02:00:00Z");   // 10:00 Manila

        var clockedIn = await _sut.TimeInAsync(new TimeInRequest(_emp.Id));
        ClockedInAt(clockedIn.TimeIn!.Value);
        _clock.Now = DateTimeOffset.Parse("2026-03-15T06:00:00Z");   // 14:00 Manila
        var clockedOut = await _sut.TimeOutAsync(new TimeOutRequest(_emp.Id));

        clockedIn.LateMinutes.Should().Be(0, "a rest day has no shift to be late for");
        clockedOut.UndertimeMinutes.Should().Be(0);
        clockedOut.OvertimeMinutes.Should().Be(240);
    }

    [Fact]
    public async Task SyncPunches_ANightShiftsMorningPunch_ClosesLastNightInsteadOfOpeningToday()
    {
        var night = new DateOnly(2026, 3, 11);
        var morning = new DateOnly(2026, 3, 12);
        OnShift(night, new TimeOnly(22, 0), new TimeOnly(6, 0));
        _employeeRepo.Setup(r => r.GetByNumberAsync("EMP-001", default)).ReturnsAsync(_emp);
        var record = ClockedInAt(Wall(2026, 3, 11, 21, 55));
        _repo.Setup(r => r.GetByEmployeeAndDateAsync(_emp.Id, morning, default)).ReturnsAsync((AttendanceRecord?)null);

        var result = await _sut.SyncPunchesAsync([new AttendancePunchDto("EMP-001", Wall(2026, 3, 12, 6, 10))]);

        result.Imported.Should().Be(1);
        record.TimeOut.Should().Be(Wall(2026, 3, 12, 6, 10));
        record.OvertimeMinutes.Should().Be(10);
        _repo.Verify(r => r.AddAsync(It.IsAny<AttendanceRecord>(), default), Times.Never);
    }

    [Fact]
    public async Task SetDayAsync_MeasuresACorrectedTimeOutFromTheShiftsEnd()
    {
        var date = new DateOnly(2026, 3, 12);
        OnShift(date, new TimeOnly(7, 0), new TimeOnly(16, 0));
        _repo.Setup(r => r.GetByEmployeeAndDateAsync(_emp.Id, date, default)).ReturnsAsync((AttendanceRecord?)null);

        var result = await _sut.SetDayAsync(_emp.Id, date, new TimeOnly(7, 0), new TimeOnly(15, 0));

        result.UndertimeMinutes.Should().Be(60);
        result.OvertimeMinutes.Should().Be(0);
    }

    [Fact]
    public async Task SetDayAsync_ATimeOutEarlierThanTheTimeIn_IsTheNextMorning()
    {
        var night = new DateOnly(2026, 3, 11);
        OnShift(night, new TimeOnly(22, 0), new TimeOnly(6, 0));
        _repo.Setup(r => r.GetByEmployeeAndDateAsync(_emp.Id, night, default)).ReturnsAsync((AttendanceRecord?)null);

        var result = await _sut.SetDayAsync(_emp.Id, night, new TimeOnly(22, 0), new TimeOnly(5, 30));

        result.TimeIn.Should().Be(Wall(2026, 3, 11, 22, 0));
        result.TimeOut.Should().Be(Wall(2026, 3, 12, 5, 30));
        result.UndertimeMinutes.Should().Be(30);
        result.OvertimeMinutes.Should().Be(0);
    }

    [Theory]
    [InlineData(8, 0, 7, 0)]    // 23 hours on
    [InlineData(17, 0, 9, 30)]  // 16.5 hours on
    public async Task SetDayAsync_RefusesATimeOutMoreThanSixteenHoursAfterTheTimeIn(int inH, int inM, int outH, int outM)
    {
        var act = () => _sut.SetDayAsync(_emp.Id, new DateOnly(2026, 3, 11), new TimeOnly(inH, inM), new TimeOnly(outH, outM));

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Contain("later than the time-in");
    }

    // ─── Working days in the summary ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSummaryAsync_CountsTheDaysTheShiftSchedules_NotMondayToFriday()
    {
        // A six-day week: Monday 9 March to Sunday 15 March, with only the Sunday off.
        var from = new DateOnly(2026, 3, 9);
        var to = new DateOnly(2026, 3, 15);
        for (var d = from; d < to; d = d.AddDays(1))
            OnShift(d, new TimeOnly(8, 0), new TimeOnly(17, 0));
        OnRestDay(to);
        _repo.Setup(r => r.GetByEmployeeAndPeriodAsync(_emp.Id, from, to, default)).ReturnsAsync([]);

        var summary = await _sut.GetSummaryAsync(_emp.Id, from, to);

        summary.TotalWorkingDays.Should().Be(6);
    }

    [Fact]
    public async Task GetSummaryAsync_WithNoShiftAssigned_StillCountsMondayToFriday()
    {
        var from = new DateOnly(2026, 3, 9);
        var to = new DateOnly(2026, 3, 15);
        _repo.Setup(r => r.GetByEmployeeAndPeriodAsync(_emp.Id, from, to, default)).ReturnsAsync([]);

        var summary = await _sut.GetSummaryAsync(_emp.Id, from, to);

        summary.TotalWorkingDays.Should().Be(5);
    }

    [Fact]
    public async Task SyncPunches_StillRecordsTheDevicesPunchTime_NotTheServerClock()
    {
        _employeeRepo.Setup(r => r.GetByNumberAsync("EMP-001", default)).ReturnsAsync(_emp);
        var punch = Wall(2026, 3, 9, 8, 15);
        _repo.Setup(r => r.GetByEmployeeAndDateAsync(_emp.Id, new DateOnly(2026, 3, 9), default))
             .ReturnsAsync((AttendanceRecord?)null);
        AttendanceRecord? added = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<AttendanceRecord>(), default))
             .Callback((AttendanceRecord a, CancellationToken _) => added = a)
             .ReturnsAsync((AttendanceRecord a, CancellationToken _) => a);

        var result = await _sut.SyncPunchesAsync([new AttendancePunchDto("EMP-001", punch)]);

        result.Imported.Should().Be(1);
        added!.TimeIn.Should().Be(punch);
        added.AttendanceDate.Should().Be(new DateOnly(2026, 3, 9));
        added.LateMinutes.Should().Be(15);
    }
}
