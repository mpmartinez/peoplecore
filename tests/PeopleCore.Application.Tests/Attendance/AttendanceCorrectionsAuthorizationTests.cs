using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PeopleCore.API.Controllers.Attendance;
using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Tests.Common;
using PeopleCore.Domain.Enums;
using Xunit;
using static PeopleCore.Application.Tests.Common.SignedInCaller;

namespace PeopleCore.Application.Tests.Attendance;

/// <summary>
/// Who may see and decide attendance corrections, by the same rules as leave and overtime: anyone
/// reads their own, HR reads everyone's, a Manager their direct reports'; a request is always for the
/// caller's own attendance; and only someone who may manage the employee decides on it.
/// </summary>
public class AttendanceCorrectionsAuthorizationTests
{
    private static readonly DateOnly Day = new(2026, 3, 10);
    private static readonly Guid CorrectionId = Guid.NewGuid();

    private readonly Mock<IAttendanceCorrectionService> _service = new();
    private readonly SignedInCaller _caller = new();
    private readonly AttendanceCorrectionsController _sut;

    public AttendanceCorrectionsAuthorizationTests()
    {
        _sut = new AttendanceCorrectionsController(_service.Object, _caller.CurrentUser.Object, _caller.Access);
    }

    private void ACorrectionFor(Guid employeeId) =>
        _service.Setup(s => s.GetAsync(CorrectionId, It.IsAny<CancellationToken>())).ReturnsAsync(new AttendanceCorrectionDto(
            CorrectionId, employeeId, "Someone", "EMP-1", Day, null, null, null, null, "why", "EmployeeRequest", "Pending",
            "someone@company.test", DateTime.UtcNow, null, null, null));

    [Fact]
    public async Task Anyone_reads_their_own_corrections()
    {
        (await _sut.GetAll(Caller, null)).Should().BeOfType<OkObjectResult>();
        (await _sut.GetHistory(Caller, Day)).Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task An_employee_cannot_read_someone_elses_corrections_or_the_whole_list()
    {
        (await _sut.GetAll(Stranger, null)).Should().BeOfType<ForbidResult>();
        (await _sut.GetHistory(Stranger, Day)).Should().BeOfType<ForbidResult>();
        (await _sut.GetAll(null, null)).Should().BeOfType<ForbidResult>();
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_manager_lists_only_their_direct_reports_corrections()
    {
        _caller.As(Caller, "Manager");

        (await _sut.GetAll(null, AttendanceCorrectionStatus.Pending)).Should().BeOfType<OkObjectResult>();

        _service.Verify(s => s.GetPagedAsync(null, Caller, AttendanceCorrectionStatus.Pending, 1, 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_request_is_always_for_the_callers_own_attendance()
    {
        var dto = new RequestAttendanceCorrectionDto(Day, new(8, 0), new(17, 0), "Forgot to scan");

        await _sut.RequestCorrection(dto);

        _service.Verify(s => s.RequestAsync(Caller, dto, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_caller_with_no_employee_record_cannot_file_a_request()
    {
        _caller.As(null);

        (await _sut.RequestCorrection(new RequestAttendanceCorrectionDto(Day, new(8, 0), null, "x"))).Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task A_manager_decides_on_a_direct_reports_request()
    {
        _caller.As(Caller, "Manager");
        ACorrectionFor(DirectReport);

        (await _sut.Approve(CorrectionId)).Should().BeOfType<OkObjectResult>();
        _service.Verify(s => s.ApproveAsync(CorrectionId, Caller, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_manager_cannot_decide_on_someone_outside_their_team()
    {
        _caller.As(Caller, "Manager");
        ACorrectionFor(Stranger);

        (await _sut.Approve(CorrectionId)).Should().BeOfType<ForbidResult>();
        (await _sut.Reject(CorrectionId, new RejectAttendanceCorrectionDto("no"))).Should().BeOfType<ForbidResult>();
        _service.Verify(s => s.ApproveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _service.Verify(s => s.RejectAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [MemberData(nameof(HrRoles), MemberType = typeof(SignedInCaller))]
    public async Task HR_decides_on_anyones_request(string role)
    {
        _caller.As(Caller, role);
        ACorrectionFor(Stranger);

        (await _sut.Approve(CorrectionId)).Should().BeOfType<OkObjectResult>();
    }
}
