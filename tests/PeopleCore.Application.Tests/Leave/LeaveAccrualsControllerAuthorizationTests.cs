using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PeopleCore.API.Controllers.Leave;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Leave.Interfaces;
using Xunit;

namespace PeopleCore.Application.Tests.Leave;

/// <summary>
/// An employee's accrual history is their leave balance line by line, so it follows the same rule
/// as GET api/leave-balances: leave staff read anyone's, everybody else only their own.
/// </summary>
public class LeaveAccrualsControllerAuthorizationTests
{
    private static readonly Guid Caller = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Stranger = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly Mock<ILeaveAccrualService> _service = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly LeaveAccrualsController _sut;

    public LeaveAccrualsControllerAuthorizationTests()
    {
        SignInAs(Caller);
        _sut = new LeaveAccrualsController(_service.Object, _currentUser.Object);
    }

    private void SignInAs(Guid? employeeId, params string[] roles)
    {
        _currentUser.Setup(c => c.EmployeeId).Returns(employeeId);
        _currentUser.Setup(c => c.IsInRole(It.IsAny<string>())).Returns((string r) => roles.Contains(r));
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
        SignInAs(null);

        var result = await _sut.GetEmployeeAccrualHistoryAsync(Stranger, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _service.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("HRManager")]
    [InlineData("Manager")]
    public async Task GetEmployeeAccrualHistory_ForAnotherEmployee_IsAllowedThrough_ForLeaveStaff(string role)
    {
        SignInAs(Caller, role);

        var result = await _sut.GetEmployeeAccrualHistoryAsync(Stranger, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }
}
