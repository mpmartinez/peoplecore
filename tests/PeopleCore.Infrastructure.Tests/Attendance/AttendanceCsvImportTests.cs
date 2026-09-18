using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Attendance.Services;
using PeopleCore.Application.Scheduling.Services;
using PeopleCore.Infrastructure.Attendance;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Attendance;

/// <summary>
/// The CSV import end to end below the controller: parsed rows, through
/// <see cref="AttendanceService.SyncPunchesAsync"/>, into a real <c>timestamptz</c> column. The
/// Application tests mock the repository, so they could never see Npgsql refusing a punch whose
/// <see cref="DateTimeKind"/> is not UTC - which is how every imported row came to be "skipped".
/// </summary>
public class AttendanceCsvImportTests : DatabaseTestBase
{
    public AttendanceCsvImportTests(PostgresFixture fixture) : base(fixture) { }

    private AttendanceService Service => new(
        new AttendanceRepository(Context),
        new HolidayService(new HolidayRepository(Context)),
        new EmployeeRepository(Context),
        new ShiftService(
            new ShiftTemplateRepository(Context),
            new RotatingPatternRepository(Context),
            new ShiftAssignmentRepository(Context)),
        TimeProvider.System);

    private async Task<Guid> AnEmployeeNumberedAsync(string employeeNumber)
    {
        await using var seeder = NewContext();
        var employee = AnEmployee();
        employee.EmployeeNumber = employeeNumber;
        seeder.Employees.Add(employee);
        await seeder.SaveChangesAsync();
        return employee.Id;
    }

    private static AttendanceCsvParseResult ParseCsv(string csv)
        => AttendanceCsv.Parse(new MemoryStream(Encoding.UTF8.GetBytes(csv)));

    [Fact]
    public async Task ImportedPunches_AreSaved_WithLatenessAndUndertimeReadOffTheWallClock()
    {
        var employeeId = await AnEmployeeNumberedAsync("CSV-0001");
        var parsed = ParseCsv(
            "employee_number,date,time_in,time_out\n" +
            "CSV-0001,2026-03-09,07:55,17:05\n" +   // on time
            "CSV-0001,2026-03-10,08:07,17:00\n" +   // 7 minutes late against the default 08:00 shift
            "CSV-0001,2026-03-11,08:00,16:30\n");   // 30 minutes undertime against 17:00

        var result = await Service.SyncPunchesAsync(parsed.Punches);

        result.Errors.Should().BeEmpty();
        result.Imported.Should().Be(6);
        result.Skipped.Should().Be(0);

        await using var reader = NewContext();
        var records = await reader.AttendanceRecords
            .Where(r => r.EmployeeId == employeeId)
            .OrderBy(r => r.AttendanceDate)
            .ToListAsync();

        records.Select(r => (r.AttendanceDate, r.LateMinutes, r.UndertimeMinutes, r.OvertimeMinutes))
            .Should().Equal(
                (new DateOnly(2026, 3, 9), 0, 0, 5),
                (new DateOnly(2026, 3, 10), 7, 0, 0),
                (new DateOnly(2026, 3, 11), 0, 30, 0));

        // Stored as the wall-clock time labelled UTC - not shifted by the server's zone.
        records[1].TimeIn.Should().Be(new DateTime(2026, 3, 10, 8, 7, 0, DateTimeKind.Utc));
        records[2].TimeOut.Should().Be(new DateTime(2026, 3, 11, 16, 30, 0, DateTimeKind.Utc));
    }
}
