using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PeopleCore.API.Controllers.Attendance;
using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Tests.Common;
using Xunit;
using static PeopleCore.Application.Tests.Common.SignedInCaller;

namespace PeopleCore.Application.Tests.Attendance;

/// <summary>
/// <see cref="AttendanceController"/> sits behind a class-level [Authorize] only, and every employee
/// id that reaches it - query string or request body - is caller-supplied. These tests pin the
/// rules, which match leave: HR staff (<c>Admin,HRManager</c>) read anyone's attendance; a Manager
/// reads their direct reports'; everybody else reads only their own; and clocking in or out is
/// self-service for everyone, resolved from the employee_id claim.
/// </summary>
public class AttendanceControllerAuthorizationTests
{
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateOnly To = new(2026, 9, 15);

    private readonly Mock<IAttendanceService> _service = new();
    private readonly SignedInCaller _caller = new();
    private readonly AttendanceController _sut;

    public AttendanceControllerAuthorizationTests()
    {
        _sut = new AttendanceController(_service.Object, _caller.CurrentUser.Object, _caller.Access);
    }

    private void SignInAs(Guid? employeeId, params string[] roles) => _caller.As(employeeId, roles);

    private static TimeInRequest TimeInFor(Guid employeeId) => new(employeeId);
    private static TimeOutRequest TimeOutFor(Guid employeeId) => new(employeeId);

    // ---- GET api/attendance ----------------------------------------------------------------

    [Fact]
    public async Task GetAll_ForOwnRecords_IsAllowedThrough()
    {
        var result = await _sut.GetAll(Caller, From, To, 1, 20, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _service.Verify(s => s.GetAllAsync(Caller, null, From, To, 1, 20, It.IsAny<CancellationToken>()), Times.Once);
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
    [MemberData(nameof(HrRoles), MemberType = typeof(SignedInCaller))]
    public async Task GetAll_WithNoEmployeeFilter_IsEveryonesAttendance_ForHrStaff(string role)
    {
        SignInAs(null, role);

        var result = await _sut.GetAll(null, From, To, 1, 20, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _service.Verify(s => s.GetAllAsync(null, null, From, To, 1, 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAll_WithNoEmployeeFilter_IsTheirDirectReportsAttendance_ForAManager()
    {
        SignInAs(Caller, "Manager");

        var result = await _sut.GetAll(null, From, To, 1, 20, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _service.Verify(s => s.GetAllAsync(null, Caller, From, To, 1, 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAll_WithNoEmployeeFilter_ReturnsForbid_ForAManagerWithNoEmployeeIdClaim()
    {
        SignInAs(null, "Manager");

        var result = await _sut.GetAll(null, null, null, 1, 20, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAll_ForADirectReport_IsAllowedThrough_ForTheirManager()
    {
        SignInAs(Caller, "Manager");

        var result = await _sut.GetAll(DirectReport, null, null, 1, 20, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _service.Verify(s => s.GetAllAsync(DirectReport, null, null, null, 1, 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAll_ForSomeoneOutsideTheirTeam_ReturnsForbid_ForAManager()
    {
        SignInAs(Caller, "Manager");

        var result = await _sut.GetAll(Stranger, null, null, 1, 20, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _service.VerifyNoOtherCalls();
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

    [Fact]
    public async Task GetSummary_ForADirectReport_IsAllowedThrough_ForTheirManager()
    {
        SignInAs(Caller, "Manager");

        var result = await _sut.GetSummary(DirectReport, From, To, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetSummary_ForSomeoneOutsideTheirTeam_ReturnsForbid_ForAManager()
    {
        SignInAs(Caller, "Manager");

        var result = await _sut.GetSummary(Stranger, From, To, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _service.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(HrRoles), MemberType = typeof(SignedInCaller))]
    public async Task GetSummary_ForAnotherEmployee_IsAllowedThrough_ForHrStaff(string role)
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
    [MemberData(nameof(DeciderRoles), MemberType = typeof(SignedInCaller))]
    public async Task TimeIn_ForSomeoneElse_ReturnsForbid_EvenForHrStaffAndTheirManager(string role)
    {
        // Clocking is self-service only. Corrections go through the import and sync endpoints.
        SignInAs(Caller, role);

        var result = await _sut.TimeIn(TimeInFor(DirectReport), CancellationToken.None);

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
    [MemberData(nameof(DeciderRoles), MemberType = typeof(SignedInCaller))]
    public async Task TimeOut_ForSomeoneElse_ReturnsForbid_EvenForHrStaffAndTheirManager(string role)
    {
        SignInAs(Caller, role);

        var result = await _sut.TimeOut(TimeOutFor(DirectReport), CancellationToken.None);

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
            nameof(AttendanceController.TimeIn), nameof(AttendanceController.TimeOut),
            // HR only: guarded by attendance.manage, pinned in PermissionEquivalenceTests.AddedEndpoints.
            nameof(AttendanceController.SetBiometricId)
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
