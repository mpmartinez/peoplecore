using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PeopleCore.API.Controllers.Leave;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Tests.Common;
using Xunit;
using static PeopleCore.Application.Tests.Common.SignedInCaller;

namespace PeopleCore.Application.Tests.Leave;

/// <summary>
/// An employee's accrual history is their leave balance line by line, so it follows the same rule
/// as GET api/leave-balances: HR staff read anyone's, a Manager their direct reports', everybody
/// else only their own.
/// </summary>
public class LeaveAccrualsControllerAuthorizationTests
{
    private readonly Mock<ILeaveAccrualService> _service = new();
    private readonly SignedInCaller _caller = new();
    private readonly LeaveAccrualsController _sut;

    public LeaveAccrualsControllerAuthorizationTests()
    {
        _sut = new LeaveAccrualsController(_service.Object, _caller.Access);
    }

    [Fact]
    public async Task GetEmployeeAccrualHistory_ForSelf_IsAllowedThrough()
    {
        var result = await _sut.GetEmployeeAccrualHistoryAsync(Caller, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _service.Verify(s => s.GetEmployeeAccrualHistoryAsync(Caller, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetEmployeeAccrualHistory_ForAnotherEmployee_ReturnsForbid_WithoutReachingTheService()
    {
        var result = await _sut.GetEmployeeAccrualHistoryAsync(Stranger, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetEmployeeAccrualHistory_ForACallerWithNoEmployeeIdClaim_ReturnsForbid()
    {
        _caller.As(null);

        var result = await _sut.GetEmployeeAccrualHistoryAsync(Stranger, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _service.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(HrRoles), MemberType = typeof(SignedInCaller))]
    public async Task GetEmployeeAccrualHistory_ForAnotherEmployee_IsAllowedThrough_ForHrStaff(string role)
    {
        _caller.As(Caller, role);

        var result = await _sut.GetEmployeeAccrualHistoryAsync(Stranger, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetEmployeeAccrualHistory_ForADirectReport_IsAllowedThrough_ForTheirManager()
    {
        _caller.As(Caller, "Manager");

        var result = await _sut.GetEmployeeAccrualHistoryAsync(DirectReport, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetEmployeeAccrualHistory_ForSomeoneOutsideTheirTeam_ReturnsForbid_ForAManager()
    {
        _caller.As(Caller, "Manager");

        var result = await _sut.GetEmployeeAccrualHistoryAsync(Stranger, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _service.VerifyNoOtherCalls();
    }
}
