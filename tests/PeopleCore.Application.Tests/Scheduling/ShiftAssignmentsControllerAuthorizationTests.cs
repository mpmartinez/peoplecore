using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PeopleCore.API.Authorization;
using PeopleCore.API.Controllers.Scheduling;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Application.Scheduling.Interfaces;
using PeopleCore.Application.Tests.Common;
using Xunit;
using static PeopleCore.Application.Tests.Common.SignedInCaller;

namespace PeopleCore.Application.Tests.Scheduling;

/// <summary>
/// <see cref="ShiftAssignmentsController.GetSchedule"/> is open to any signed-in user and takes the
/// employee id from the route. These tests pin who may read whose schedule: HR staff
/// (<c>Admin,HRManager</c>) read anyone's; a Manager their direct reports'; everybody else only their own.
/// </summary>
public class ShiftAssignmentsControllerAuthorizationTests
{
    private static readonly DateOnly From = new(2026, 9, 14);
    private static readonly DateOnly To = new(2026, 9, 20);

    private readonly Mock<IShiftService> _service = new();
    private readonly SignedInCaller _caller = new();
    private readonly ShiftAssignmentsController _sut;

    public ShiftAssignmentsControllerAuthorizationTests()
    {
        _sut = new ShiftAssignmentsController(_service.Object, _caller.Access);
    }

    private void SignInAs(Guid? employeeId, params string[] roles) => _caller.As(employeeId, roles);

    [Fact]
    public async Task GetSchedule_ForSelf_IsAllowedThrough()
    {
        var result = await _sut.GetSchedule(Caller, From, To, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _service.Verify(s => s.GetEmployeeScheduleAsync(Caller, From, To, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetSchedule_ForAnotherEmployee_ReturnsForbid_WithoutReachingTheService()
    {
        var result = await _sut.GetSchedule(Stranger, From, To, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetSchedule_ForACallerWithNoEmployeeIdClaim_ReturnsForbid()
    {
        SignInAs(null);

        var result = await _sut.GetSchedule(Stranger, From, To, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetSchedule_ForADirectReport_IsAllowedThrough_ForTheirManager()
    {
        SignInAs(Caller, "Manager");

        var result = await _sut.GetSchedule(DirectReport, From, To, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetSchedule_ForSomeoneOutsideTheirTeam_ReturnsForbid_ForAManager()
    {
        SignInAs(Caller, "Manager");

        var result = await _sut.GetSchedule(Stranger, From, To, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _service.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(HrRoles), MemberType = typeof(SignedInCaller))]
    public async Task GetSchedule_ForAnotherEmployee_IsAllowedThrough_ForHrStaff(string role)
    {
        SignInAs(Caller, role);

        var result = await _sut.GetSchedule(Stranger, From, To, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public void EveryActionTakingAnEmployeeId_IsGuardedByTheseTestsOrByASchedulingOnlyPermission()
    {
        // AssignShift carries an employee id in its body, but only a caller who may manage
        // scheduling can call it at all. That exemption must be narrow: an action guarded by
        // [RequirePermission(SchedulingManage, ApprovalsTeam)] would also admit a Manager, who
        // has no scheduling-manage permission at all - so only an action whose permission
        // requirement is exactly [SchedulingManage] may be exempt.
        var takingAnEmployeeId = typeof(ShiftAssignmentsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetParameters().Any(p =>
                string.Equals(p.Name, "employeeId", StringComparison.OrdinalIgnoreCase)
                || p.ParameterType.GetProperty("EmployeeId") is not null))
            .Where(m => !m.GetCustomAttributes<RequirePermissionAttribute>()
                .Any(a => a.AnyOf.Count == 1 && a.AnyOf[0] == Permissions.SchedulingManage))
            .Select(m => m.Name);

        takingAnEmployeeId.Should().BeSubsetOf([nameof(ShiftAssignmentsController.GetSchedule)]);
    }
}
