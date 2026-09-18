using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Attendance.Services;
using PeopleCore.Application.Scheduling.Services;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Attendance;

/// <summary>
/// Clocking in and out from the web app, into a real <c>timestamptz</c> column: the server's clock
/// is stored as the Philippine wall-clock time labelled UTC - the same shape the CSV import stores
/// (see <see cref="AttendanceCsvImportTests"/>) - and reads back unchanged.
/// </summary>
public class AttendanceClockInTests : DatabaseTestBase
{
    public AttendanceClockInTests(PostgresFixture fixture) : base(fixture) { }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly Clock _clock = new();

    private AttendanceService Service => new(
        new AttendanceRepository(Context),
        new HolidayService(new HolidayRepository(Context)),
        new EmployeeRepository(Context),
        new ShiftService(
            new ShiftTemplateRepository(Context),
            new RotatingPatternRepository(Context),
            new ShiftAssignmentRepository(Context)),
        _clock);

    [Fact]
    public async Task ClockingInBefore0800AndOutAt1630Manila_IsStoredAsTheManilaDayAndWallClock()
    {
        var employee = AnEmployee();
        await using (var seeder = NewContext())
        {
            seeder.Employees.Add(employee);
            await seeder.SaveChangesAsync();
        }
        var employeeId = employee.Id;

        _clock.Now = new DateTimeOffset(2026, 3, 11, 23, 55, 30, TimeSpan.Zero);   // 07:55:30 on 12 March in Manila
        await Service.TimeInAsync(new TimeInRequest(employeeId));

        _clock.Now = new DateTimeOffset(2026, 3, 12, 8, 30, 0, TimeSpan.Zero);     // 16:30 in Manila
        await Service.TimeOutAsync(new TimeOutRequest(employeeId));

        await using var check = NewContext();
        var record = await check.AttendanceRecords.SingleAsync(r => r.EmployeeId == employeeId);

        record.AttendanceDate.Should().Be(new DateOnly(2026, 3, 12));
        record.TimeIn.Should().Be(new DateTime(2026, 3, 12, 7, 55, 30, DateTimeKind.Utc));
        record.TimeIn!.Value.Kind.Should().Be(DateTimeKind.Utc);
        record.TimeOut.Should().Be(new DateTime(2026, 3, 12, 16, 30, 0, DateTimeKind.Utc));
        record.LateMinutes.Should().Be(0);
        record.UndertimeMinutes.Should().Be(30);
        record.OvertimeMinutes.Should().Be(0);
        record.IsPresent.Should().BeTrue();
    }
}
