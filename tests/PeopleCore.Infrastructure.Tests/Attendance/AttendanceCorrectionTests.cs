using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Attendance.Services;
using PeopleCore.Application.Scheduling.Services;
using PeopleCore.Domain.Entities.Attendance;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Attendance;

/// <summary>
/// Correcting a recorded day against a real database: the times and the minutes computed from them
/// change together, the old times are kept in the day's history, and a day inside a paid payroll run
/// for that employee is refused.
/// </summary>
public class AttendanceCorrectionTests : DatabaseTestBase
{
    private static readonly DateOnly Day = new(2026, 3, 10);

    public AttendanceCorrectionTests(PostgresFixture fixture) : base(fixture) { }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private AttendanceCorrectionService Sut
    {
        get
        {
            var attendance = new AttendanceService(
                new AttendanceRepository(Context),
                new HolidayService(new HolidayRepository(Context)),
                new EmployeeRepository(Context),
                new ShiftService(
                    new ShiftTemplateRepository(Context),
                    new RotatingPatternRepository(Context),
                    new ShiftAssignmentRepository(Context)),
                TimeProvider.System);
            return new AttendanceCorrectionService(
                new AttendanceCorrectionRepository(Context), new AttendanceRepository(Context), attendance,
                new PayrollRunRepository(Context), new FixedClock(new DateTimeOffset(2026, 3, 20, 0, 0, 0, TimeSpan.Zero)));
        }
    }

    private async Task<Guid> AnEmployeeRecordedAsync(TimeOnly timeIn, TimeOnly timeOut)
    {
        await using var seeder = NewContext();
        var employee = AnEmployee();
        seeder.Employees.Add(employee);
        seeder.AttendanceRecords.Add(new AttendanceRecord
        {
            EmployeeId = employee.Id,
            AttendanceDate = Day,
            TimeIn = Day.ToDateTime(timeIn, DateTimeKind.Utc),
            TimeOut = Day.ToDateTime(timeOut, DateTimeKind.Utc),
            IsPresent = true,
        });
        await seeder.SaveChangesAsync();
        return employee.Id;
    }

    [Fact]
    public async Task An_HR_correction_changes_the_day_recomputes_its_minutes_and_keeps_the_old_times()
    {
        var employeeId = await AnEmployeeRecordedAsync(new(9, 30), new(16, 0));

        await Sut.CorrectAsync(new CorrectAttendanceDto(employeeId, Day, new(8, 5), new(17, 30), "Clock was down"), "hr@company.test");

        await using var reader = NewContext();
        var record = await reader.AttendanceRecords.SingleAsync(r => r.EmployeeId == employeeId);
        (record.TimeIn, record.TimeOut).Should().Be((Day.ToDateTime(new(8, 5), DateTimeKind.Utc), Day.ToDateTime(new(17, 30), DateTimeKind.Utc)));
        (record.LateMinutes, record.UndertimeMinutes, record.OvertimeMinutes).Should().Be((5, 0, 30));

        var history = await Sut.GetHistoryAsync(employeeId, Day);
        history.Should().ContainSingle();
        history[0].PreviousTimeIn.Should().Be(Day.ToDateTime(new(9, 30), DateTimeKind.Utc));
        history[0].Reason.Should().Be("Clock was down");
        history[0].Status.Should().Be("Applied");
    }

    [Fact]
    public async Task A_day_inside_a_paid_run_for_that_employee_is_refused_and_left_as_it_was()
    {
        var employeeId = await AnEmployeeRecordedAsync(new(9, 30), new(16, 0));
        await using (var seeder = NewContext())
        {
            var run = ARun("PR-2026-005", new(2026, 3, 1), new(2026, 3, 15), new(2026, 3, 20));
            seeder.PayrollRuns.Add(run);
            seeder.PayrollRunEmployees.Add(AnEntry(run.Id, employeeId));
            await seeder.SaveChangesAsync();
        }

        var act = () => Sut.CorrectAsync(new CorrectAttendanceDto(employeeId, Day, new(8, 0), new(17, 0), "Late fix"), "hr@company.test");

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Contain("PR-2026-005");
        await using var reader = NewContext();
        (await reader.AttendanceRecords.SingleAsync(r => r.EmployeeId == employeeId)).TimeIn
            .Should().Be(Day.ToDateTime(new(9, 30), DateTimeKind.Utc));
        (await reader.AttendanceCorrections.CountAsync(c => c.EmployeeId == employeeId)).Should().Be(0);
    }

    [Theory]
    [InlineData(PayrollRunStatus.Draft)]
    [InlineData(PayrollRunStatus.Approved)]
    public async Task A_run_not_yet_paid_does_not_block_a_correction(PayrollRunStatus status)
    {
        var employeeId = await AnEmployeeRecordedAsync(new(9, 30), new(16, 0));
        await using (var seeder = NewContext())
        {
            var run = ARun("PR-2026-006", new(2026, 3, 1), new(2026, 3, 15), new(2026, 3, 20), status);
            seeder.PayrollRuns.Add(run);
            seeder.PayrollRunEmployees.Add(AnEntry(run.Id, employeeId));
            await seeder.SaveChangesAsync();
        }

        await Sut.CorrectAsync(new CorrectAttendanceDto(employeeId, Day, new(8, 0), new(17, 0), "Fix"), "hr@company.test");

        (await Sut.GetHistoryAsync(employeeId, Day)).Should().ContainSingle();
    }

    [Fact]
    public async Task An_employees_request_changes_nothing_until_it_is_approved()
    {
        var employeeId = await AnEmployeeRecordedAsync(new(9, 30), new(16, 0));
        var approverId = Guid.NewGuid();

        var request = await Sut.RequestAsync(employeeId,
            new RequestAttendanceCorrectionDto(Day, new(8, 0), new(17, 0), "Forgot to scan in"), "juan@company.test");

        await using (var reader = NewContext())
            (await reader.AttendanceRecords.SingleAsync(r => r.EmployeeId == employeeId)).TimeIn
                .Should().Be(Day.ToDateTime(new(9, 30), DateTimeKind.Utc));

        var approved = await Sut.ApproveAsync(request.Id, approverId, "manager@company.test");

        approved.Status.Should().Be("Applied");
        approved.ReviewedBy.Should().Be("manager@company.test");
        await using var after = NewContext();
        (await after.AttendanceRecords.SingleAsync(r => r.EmployeeId == employeeId)).TimeIn
            .Should().Be(Day.ToDateTime(new(8, 0), DateTimeKind.Utc));
    }
}
