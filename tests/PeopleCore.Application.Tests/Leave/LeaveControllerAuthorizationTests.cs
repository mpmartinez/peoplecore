using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PeopleCore.API.Controllers.Leave;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Leave.DTOs;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Domain.Enums;
using Xunit;

namespace PeopleCore.Application.Tests.Leave;

/// <summary>
/// <see cref="LeaveController"/> sits behind a class-level [Authorize] only, and "signed in" says
/// nothing about WHICH employee's leave the caller may touch. Every employee id that reaches it -
/// in the route, the query string or the request body - is caller-supplied. These tests pin the
/// rules: HR and managers (<c>Admin,HRManager,Manager</c>, the same roles that approve and reject)
/// read anyone's leave; everybody else reads only their own; and filing or cancelling leave is
/// self-service for everyone, resolved from the employee_id claim.
/// </summary>
public class LeaveControllerAuthorizationTests
{
    private static readonly Guid Caller = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Stranger = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid RequestId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid LeaveTypeId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly Mock<ILeaveTypeService> _types = new();
    private readonly Mock<ILeaveRequestService> _requests = new();
    private readonly Mock<ILeaveBalanceService> _balances = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly LeaveController _sut;

    public LeaveControllerAuthorizationTests()
    {
        // Default caller: a rank-and-file employee holding no privileged role.
        SignInAs(Caller);
        _sut = new LeaveController(_types.Object, _requests.Object, _balances.Object, _currentUser.Object);
    }

    private void SignInAs(Guid? employeeId, params string[] roles)
    {
        _currentUser.Setup(c => c.EmployeeId).Returns(employeeId);
        _currentUser.Setup(c => c.IsInRole(It.IsAny<string>())).Returns((string r) => roles.Contains(r));
    }

    public static TheoryData<string> LeaveReaderRoles => new() { "Admin", "HRManager", "Manager" };

    private void VerifyNoServiceReached()
    {
        _requests.VerifyNoOtherCalls();
        _balances.VerifyNoOtherCalls();
    }

    private static LeaveRequestDto RequestOf(Guid employeeId) => new(
        RequestId, employeeId, "Maria Santos", LeaveTypeId, "Vacation Leave",
        new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 6), 2, "Family trip",
        LeaveStatus.Pending, null, null, null, DateTime.UtcNow);

    private static CreateLeaveRequestDto NewRequestFor(Guid employeeId) =>
        new(employeeId, LeaveTypeId, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 6), "Family trip");

    // ---- GET api/leave-requests ------------------------------------------------------------

    [Fact]
    public async Task GetAll_ForOwnRequests_IsAllowedThrough()
    {
        var result = await _sut.GetAll(Caller, "Pending", 1, 20, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _requests.Verify(s => s.GetAllAsync(Caller, "Pending", 1, 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAll_ForAnotherEmployee_ReturnsForbid_WithoutReachingTheService()
    {
        var result = await _sut.GetAll(Stranger, null, 1, 20, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        VerifyNoServiceReached();
    }

    [Fact]
    public async Task GetAll_WithNoEmployeeFilter_ReturnsForbid_ForAnUnprivilegedCaller()
    {
        // No employeeId means "every employee's requests, reasons included".
        var result = await _sut.GetAll(null, null, 1, 20, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        VerifyNoServiceReached();
    }

    [Fact]
    public async Task GetAll_WithNoEmployeeFilter_ReturnsForbid_ForACallerWithNoEmployeeIdClaim()
    {
        // The trap: a missing claim (null) "equals" a missing filter (null). Treating that as
        // "self" would hand an unlinked account the whole organisation's leave.
        SignInAs(null);

        var result = await _sut.GetAll(null, null, 1, 20, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        VerifyNoServiceReached();
    }

    [Theory]
    [MemberData(nameof(LeaveReaderRoles))]
    public async Task GetAll_WithNoEmployeeFilter_IsAllowedThrough_ForLeaveReaders(string role)
    {
        SignInAs(null, role);

        var result = await _sut.GetAll(null, "Pending", 1, 20, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _requests.Verify(s => s.GetAllAsync(null, "Pending", 1, 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- GET api/leave-requests/{id} -------------------------------------------------------

    [Fact]
    public async Task GetById_ForOwnRequest_IsAllowedThrough()
    {
        _requests.Setup(s => s.GetByIdAsync(RequestId, It.IsAny<CancellationToken>())).ReturnsAsync(RequestOf(Caller));

        var result = await _sut.GetById(RequestId, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<LeaveRequestDto>();
    }

    [Fact]
    public async Task GetById_ForAnotherEmployeesRequest_ReturnsForbid()
    {
        _requests.Setup(s => s.GetByIdAsync(RequestId, It.IsAny<CancellationToken>())).ReturnsAsync(RequestOf(Stranger));

        var result = await _sut.GetById(RequestId, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>("the request's reason and dates belong to someone else");
    }

    [Fact]
    public async Task GetById_ForACallerWithNoEmployeeIdClaim_ReturnsForbid()
    {
        SignInAs(null);
        _requests.Setup(s => s.GetByIdAsync(RequestId, It.IsAny<CancellationToken>())).ReturnsAsync(RequestOf(Stranger));

        var result = await _sut.GetById(RequestId, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
    }

    [Theory]
    [MemberData(nameof(LeaveReaderRoles))]
    public async Task GetById_ForAnotherEmployeesRequest_IsAllowedThrough_ForLeaveReaders(string role)
    {
        SignInAs(Caller, role);
        _requests.Setup(s => s.GetByIdAsync(RequestId, It.IsAny<CancellationToken>())).ReturnsAsync(RequestOf(Stranger));

        var result = await _sut.GetById(RequestId, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    // ---- POST api/leave-requests -----------------------------------------------------------

    [Fact]
    public async Task Create_ForSelf_IsAllowedThrough_AndFilesForTheCaller()
    {
        var result = await _sut.Create(NewRequestFor(Caller), CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(201);
        _requests.Verify(s => s.CreateAsync(
            It.Is<CreateLeaveRequestDto>(d => d.EmployeeId == Caller), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_ForAnotherEmployee_ReturnsForbid_WithoutFilingAnything()
    {
        var result = await _sut.Create(NewRequestFor(Stranger), CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        VerifyNoServiceReached();
    }

    [Theory]
    [MemberData(nameof(LeaveReaderRoles))]
    public async Task Create_ForAnotherEmployee_ReturnsForbid_EvenForLeaveReaders(string role)
    {
        // Filing leave is self-service only: seeing someone's leave is not a licence to spend
        // their balance.
        SignInAs(Caller, role);

        var result = await _sut.Create(NewRequestFor(Stranger), CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        VerifyNoServiceReached();
    }

    [Fact]
    public async Task Create_ForACallerWithNoEmployeeIdClaim_ReturnsForbid()
    {
        SignInAs(null, "Admin");

        var result = await _sut.Create(NewRequestFor(Stranger), CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        VerifyNoServiceReached();
    }

    // ---- PUT api/leave-requests/{id}/cancel ------------------------------------------------

    [Fact]
    public void Cancel_TakesNoEmployeeIdFromTheCaller()
    {
        // It used to take [FromQuery] Guid employeeId and pass it straight to the service's
        // ownership check, so the check compared the request against whoever the caller claimed
        // to be. Pinning the exact parameter set stops an identifier coming back under any name.
        var parameters = typeof(LeaveController)
            .GetMethod(nameof(LeaveController.Cancel), BindingFlags.Public | BindingFlags.Instance)!
            .GetParameters().Select(p => p.Name);

        parameters.Should().BeEquivalentTo(["id", "ct"]);
    }

    [Fact]
    public async Task Cancel_CancelsAsTheEmployeeInTheClaim()
    {
        var result = await _sut.Cancel(RequestId, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        _requests.Verify(s => s.CancelAsync(RequestId, Caller, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancel_ForACallerWithNoEmployeeIdClaim_ReturnsForbid()
    {
        SignInAs(null, "Admin");

        var result = await _sut.Cancel(RequestId, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        VerifyNoServiceReached();
    }

    // ---- GET api/leave-balances/{employeeId} -----------------------------------------------

    [Fact]
    public async Task GetBalances_ForSelf_IsAllowedThrough()
    {
        var result = await _sut.GetBalances(Caller, 2026, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _balances.Verify(s => s.GetByEmployeeAsync(Caller, 2026, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetBalances_ForAnotherEmployee_ReturnsForbid_WithoutReachingTheService()
    {
        var result = await _sut.GetBalances(Stranger, null, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        VerifyNoServiceReached();
    }

    [Fact]
    public async Task GetBalances_ForACallerWithNoEmployeeIdClaim_ReturnsForbid()
    {
        SignInAs(null);

        var result = await _sut.GetBalances(Stranger, null, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        VerifyNoServiceReached();
    }

    [Theory]
    [MemberData(nameof(LeaveReaderRoles))]
    public async Task GetBalances_ForAnotherEmployee_IsAllowedThrough_ForLeaveReaders(string role)
    {
        SignInAs(Caller, role);

        var result = await _sut.GetBalances(Stranger, null, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public void EveryActionTakingAnEmployeeId_IsCoveredByTheseTests()
    {
        // A new action accepting an employee id (directly or inside a DTO) would otherwise ship
        // with no ownership check and nobody would notice. Add it here once it is guarded and tested.
        string[] covered = [nameof(LeaveController.GetAll), nameof(LeaveController.Create), nameof(LeaveController.GetBalances)];

        var takingAnEmployeeId = typeof(LeaveController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetParameters().Any(p =>
                string.Equals(p.Name, "employeeId", StringComparison.OrdinalIgnoreCase)
                || p.ParameterType.GetProperty("EmployeeId") is not null))
            .Select(m => m.Name);

        takingAnEmployeeId.Should().BeSubsetOf(covered);
    }
}
