using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PeopleCore.API.Controllers.Scheduling;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Scheduling.Interfaces;
using Xunit;

namespace PeopleCore.Application.Tests.Scheduling;

/// <summary>
/// <see cref="ShiftAssignmentsController.GetSchedule"/> is open to any signed-in user and takes the
/// employee id from the route. These tests pin who may read whose schedule: HR and managers
/// (<c>Admin,HRManager,Manager</c>) read anyone's; everybody else reads only their own.
/// </summary>
public class ShiftAssignmentsControllerAuthorizationTests
{
    private static readonly Guid Caller = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Stranger = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateOnly From = new(2026, 9, 14);
    private static readonly DateOnly To = new(2026, 9, 20);

    private readonly Mock<IShiftService> _service = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly ShiftAssignmentsController _sut;

    public ShiftAssignmentsControllerAuthorizationTests()
    {
        SignInAs(Caller);
        _sut = new ShiftAssignmentsController(_service.Object, _currentUser.Object);
    }

    private void SignInAs(Guid? employeeId, params string[] roles)
    {
        _currentUser.Setup(c => c.EmployeeId).Returns(employeeId);
        _currentUser.Setup(c => c.IsInRole(It.IsAny<string>())).Returns((string r) => roles.Contains(r));
    }

    public static TheoryData<string> ScheduleReaderRoles => new() { "Admin", "HRManager", "Manager" };

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

    [Theory]
    [MemberData(nameof(ScheduleReaderRoles))]
    public async Task GetSchedule_ForAnotherEmployee_IsAllowedThrough_ForScheduleReaders(string role)
    {
        SignInAs(Caller, role);

        var result = await _sut.GetSchedule(Stranger, From, To, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public void EveryActionTakingAnEmployeeId_IsGuardedByTheseTestsOrByAnHrOnlyRole()
    {
        // AssignShift carries an employee id in its body, but only HR may call it at all.
        var takingAnEmployeeId = typeof(ShiftAssignmentsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetParameters().Any(p =>
                string.Equals(p.Name, "employeeId", StringComparison.OrdinalIgnoreCase)
                || p.ParameterType.GetProperty("EmployeeId") is not null))
            .Where(m => m.GetCustomAttributes<AuthorizeAttribute>().All(a => a.Roles != "Admin,HRManager"))
            .Select(m => m.Name);

        takingAnEmployeeId.Should().BeSubsetOf([nameof(ShiftAssignmentsController.GetSchedule)]);
    }
}
