using FluentAssertions;
using Moq;
using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Attendance.Services;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Domain.Entities.Attendance;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Attendance;

/// <summary>The rules every correction is held to, however it comes in. Nothing is written when one is broken.</summary>
public class AttendanceCorrectionServiceTests
{
    private static readonly Guid Juan = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 3, 10);

    private readonly Mock<IAttendanceCorrectionRepository> _corrections = new();
    private readonly Mock<IAttendanceRepository> _records = new();
    private readonly Mock<IAttendanceService> _attendance = new();
    private readonly Mock<IPayrollRunRepository> _runs = new();
    private readonly AttendanceCorrectionService _sut;

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    public AttendanceCorrectionServiceTests()
    {
        _sut = new AttendanceCorrectionService(_corrections.Object, _records.Object, _attendance.Object, _runs.Object,
            new FixedClock(new DateTimeOffset(2026, 3, 20, 0, 0, 0, TimeSpan.Zero)));
    }

    private void NothingWritten()
    {
        _attendance.Verify(a => a.SetDayAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<TimeOnly?>(), It.IsAny<TimeOnly?>(), It.IsAny<CancellationToken>()), Times.Never);
        _corrections.Verify(c => c.AddAsync(It.IsAny<AttendanceCorrection>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(8, 0, 7, 0, "later than the time-in")] // out before in
    [InlineData(8, 0, 8, 0, "later than the time-in")] // out equal to in
    public async Task A_time_out_not_after_the_time_in_is_refused(int inH, int inM, int outH, int outM, string message)
    {
        var act = () => _sut.CorrectAsync(new CorrectAttendanceDto(Juan, Day, new(inH, inM), new(outH, outM), "fix"), "hr");

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Contain(message);
        NothingWritten();
    }

    [Fact]
    public async Task A_time_out_without_a_time_in_is_refused()
    {
        var act = () => _sut.CorrectAsync(new CorrectAttendanceDto(Juan, Day, null, new(17, 0), "fix"), "hr");

        await act.Should().ThrowAsync<DomainException>();
        NothingWritten();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_correction_needs_a_reason(string reason)
    {
        var act = () => _sut.CorrectAsync(new CorrectAttendanceDto(Juan, Day, new(8, 0), new(17, 0), reason), "hr");

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Contain("reason");
        NothingWritten();
    }

    [Fact]
    public async Task A_day_that_has_not_happened_yet_cannot_be_corrected()
    {
        var act = () => _sut.CorrectAsync(new CorrectAttendanceDto(Juan, new DateOnly(2026, 3, 21), new(8, 0), null, "fix"), "hr");

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Contain("hasn't happened yet");
        NothingWritten();
    }

    [Fact]
    public async Task A_second_request_for_a_day_already_waiting_is_refused()
    {
        _corrections.Setup(c => c.HasPendingForDayAsync(Juan, Day, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var act = () => _sut.RequestAsync(Juan, new RequestAttendanceCorrectionDto(Day, new(8, 0), null, "forgot"), "juan");

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Contain("already a correction waiting");
        NothingWritten();
    }

    [Fact]
    public async Task Nobody_decides_on_their_own_request()
    {
        var id = Guid.NewGuid();
        _corrections.Setup(c => c.GetWithEmployeeAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(new AttendanceCorrection
        {
            Id = id, EmployeeId = Juan, AttendanceDate = Day, Status = AttendanceCorrectionStatus.Pending,
        });

        var act = () => _sut.ApproveAsync(id, Juan, "juan");

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Contain("your own");
        NothingWritten();
    }

    [Fact]
    public async Task A_request_already_decided_cannot_be_decided_again()
    {
        var id = Guid.NewGuid();
        _corrections.Setup(c => c.GetWithEmployeeAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(new AttendanceCorrection
        {
            Id = id, EmployeeId = Juan, AttendanceDate = Day, Status = AttendanceCorrectionStatus.Rejected,
        });

        var act = () => _sut.ApproveAsync(id, Guid.NewGuid(), "manager");

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Contain("already rejected");
    }
}
