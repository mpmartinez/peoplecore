using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PeopleCore.API.Controllers.Attendance;
using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Common.Interfaces;
using Xunit;

namespace PeopleCore.Application.Tests.Attendance;

/// <summary>
/// <see cref="AttendanceController"/> sits behind a class-level [Authorize] only, and every employee
/// id that reaches it - query string or request body - is caller-supplied. These tests pin the
/// rules, which match leave: HR and managers (<c>Admin,HRManager,Manager</c>) read anyone's
/// attendance; everybody else reads only their own; and clocking in or out is self-service for
/// everyone, resolved from the employee_id claim.
/// </summary>
public class AttendanceControllerAuthorizationTests
{
    private static readonly Guid Caller = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Stranger = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateOnly To = new(2026, 9, 15);

    private readonly Mock<IAttendanceService> _service = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly AttendanceController _sut;

    public AttendanceControllerAuthorizationTests()
    {
        // Default caller: a rank-and-file employee holding no privileged role.
        SignInAs(Caller);
        _sut = new AttendanceController(_service.Object, _currentUser.Object);
    }

    private void SignInAs(Guid? employeeId, params string[] roles)
    {
        _currentUser.Setup(c => c.EmployeeId).Returns(employeeId);
        _currentUser.Setup(c => c.IsInRole(It.IsAny<string>())).Returns((string r) => roles.Contains(r));
    }

    public static TheoryData<string> AttendanceReaderRoles => new() { "Admin", "HRManager", "Manager" };

    private static TimeInRequest TimeInFor(Guid employeeId) => new(employeeId, new DateTime(2026, 9, 14, 0, 2, 0, DateTimeKind.Utc));
    private static TimeOutRequest TimeOutFor(Guid employeeId) => new(employeeId, new DateTime(2026, 9, 14, 9, 5, 0, DateTimeKind.Utc));

    // ---- GET api/attendance ----------------------------------------------------------------

    [Fact]
    public async Task GetAll_ForOwnRecords_IsAllowedThrough()
    {
        var result = await _sut.GetAll(Caller, From, To, 1, 20, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _service.Verify(s => s.GetAllAsync(Caller, From, To, 1, 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAll_ForAnotherEmployee_ReturnsForbid_WithoutReachingTheService()
    {
        var result = await _sut.GetAll(Stranger, null, null, 1, 20, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAll_WithNoEmployeeFilter_ReturnsForbid_ForAnUnprivilegedCaller()
    {
        // No employeeId means "every employee's time-in and time-out".
        var result = await _sut.GetAll(null, null, null, 1, 20, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAll_WithNoEmployeeFilter_ReturnsForbid_ForACallerWithNoEmployeeIdClaim()
    {
        // A missing claim (null) must not "equal" a missing filter (null).
        SignInAs(null);

        var result = await _sut.GetAll(null, null, null, 1, 20, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _service.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(AttendanceReaderRoles))]
    public async Task GetAll_WithNoEmployeeFilter_IsAllowedThrough_ForAttendanceReaders(string role)
    {
        SignInAs(null, role);

        var result = await _sut.GetAll(null, From, To, 1, 20, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _service.Verify(s => s.GetAllAsync(null, From, To, 1, 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- GET api/attendance/summary --------------------------------------------------------

    [Fact]
    public async Task GetSummary_ForSelf_IsAllowedThrough()
    {
        var result = await _sut.GetSummary(Caller, From, To, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _service.Verify(s => s.GetSummaryAsync(Caller, From, To, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetSummary_ForAnotherEmployee_ReturnsForbid_WithoutReachingTheService()
    {
        var result = await _sut.GetSummary(Stranger, From, To, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetSummary_ForACallerWithNoEmployeeIdClaim_ReturnsForbid()
    {
        SignInAs(null);

        var result = await _sut.GetSummary(Stranger, From, To, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _service.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(AttendanceReaderRoles))]
    public async Task GetSummary_ForAnotherEmployee_IsAllowedThrough_ForAttendanceReaders(string role)
    {
        SignInAs(Caller, role);

        var result = await _sut.GetSummary(Stranger, From, To, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    // ---- POST api/attendance/time-in -------------------------------------------------------

    [Fact]
    public async Task TimeIn_ForSelf_IsAllowedThrough()
    {
        var request = TimeInFor(Caller);

        var result = await _sut.TimeIn(request, CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(201);
        _service.Verify(s => s.TimeInAsync(request, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TimeIn_ForAnotherEmployee_ReturnsForbid_WithoutClockingAnyoneIn()
    {
        var result = await _sut.TimeIn(TimeInFor(Stranger), CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _service.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(AttendanceReaderRoles))]
    public async Task TimeIn_ForAnotherEmployee_ReturnsForbid_EvenForAttendanceReaders(string role)
    {
        // Clocking is self-service only. Corrections go through the import and sync endpoints.
        SignInAs(Caller, role);

        var result = await _sut.TimeIn(TimeInFor(Stranger), CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TimeIn_ForACallerWithNoEmployeeIdClaim_ReturnsForbid()
    {
        SignInAs(null, "Admin");

        var result = await _sut.TimeIn(TimeInFor(Stranger), CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _service.VerifyNoOtherCalls();
    }

    // ---- POST api/attendance/time-out ------------------------------------------------------

    [Fact]
    public async Task TimeOut_ForSelf_IsAllowedThrough()
    {
        var request = TimeOutFor(Caller);

        var result = await _sut.TimeOut(request, CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(201);
        _service.Verify(s => s.TimeOutAsync(request, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TimeOut_ForAnotherEmployee_ReturnsForbid_WithoutClockingAnyoneOut()
    {
        var result = await _sut.TimeOut(TimeOutFor(Stranger), CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _service.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(AttendanceReaderRoles))]
    public async Task TimeOut_ForAnotherEmployee_ReturnsForbid_EvenForAttendanceReaders(string role)
    {
        SignInAs(Caller, role);

        var result = await _sut.TimeOut(TimeOutFor(Stranger), CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TimeOut_ForACallerWithNoEmployeeIdClaim_ReturnsForbid()
    {
        SignInAs(null, "Admin");

        var result = await _sut.TimeOut(TimeOutFor(Stranger), CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public void EveryActionTakingAnEmployeeId_IsCoveredByTheseTests()
    {
        // A new action accepting an employee id (directly or inside a DTO) would otherwise ship
        // with no ownership check and nobody would notice. Add it here once it is guarded and tested.
        string[] covered =
        [
            nameof(AttendanceController.GetAll), nameof(AttendanceController.GetSummary),
            nameof(AttendanceController.TimeIn), nameof(AttendanceController.TimeOut)
        ];

        var takingAnEmployeeId = typeof(AttendanceController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetParameters().Any(p =>
                string.Equals(p.Name, "employeeId", StringComparison.OrdinalIgnoreCase)
                || p.ParameterType.GetProperty("EmployeeId") is not null))
            .Select(m => m.Name);

        takingAnEmployeeId.Should().BeSubsetOf(covered);
    }
}
