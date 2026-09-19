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

    private static AttendanceFileParseResult ParseCsv(string csv)
        => AttendanceFile.Parse(new MemoryStream(Encoding.UTF8.GetBytes(csv)), "attendance.csv");

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

    [Fact]
    public async Task A_ZKTeco_scan_log_is_saved_under_the_employee_enrolled_with_that_number()
    {
        var employeeId = await AnEmployeeNumberedAsync("ZK-0001");
        var corrections = new AttendanceCorrectionService(
            new AttendanceCorrectionRepository(Context), new AttendanceRepository(Context), Service,
            new PayrollRunRepository(Context), TimeProvider.System);
        var import = new AttendanceImportService(
            new EmployeeRepository(Context), Service, new AttendanceRepository(Context), corrections);
        await import.SetBiometricIdAsync(employeeId, "1023");

        var parsed = AttendanceFile.Parse(new MemoryStream(Encoding.UTF8.GetBytes(
            "AC-No.,Name,Time,State\n" +
            "1023,Juan,3/10/2026 8:05 AM,C/In\n" +
            "1023,Juan,3/10/2026 12:00 PM,C/Out\n" +
            "1023,Juan,3/10/2026 5:10 PM,C/Out\n" +
            "999,Nobody,3/10/2026 8:00 AM,C/In\n")), "zk.csv");

        var result = await import.ImportAsync(parsed.Punches, parsed.Errors);

        result.Imported.Should().Be(2);
        result.Errors.Should().ContainSingle().Which.Should().Contain("'999'");

        await using var reader = NewContext();
        var record = await reader.AttendanceRecords.SingleAsync(r => r.EmployeeId == employeeId);
        record.TimeIn.Should().Be(new DateTime(2026, 3, 10, 8, 5, 0, DateTimeKind.Utc));
        record.TimeOut.Should().Be(new DateTime(2026, 3, 10, 17, 10, 0, DateTimeKind.Utc));
        record.LateMinutes.Should().Be(5);
    }
}
