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
/// <see cref="OvertimeController"/> sits behind a class-level [Authorize] only, and every employee
/// id that reaches it is caller-supplied. These tests pin the rules: HR and managers
/// (<c>Admin,HRManager,Manager</c>) read anyone's overtime; everybody else reads only their own;
/// filing overtime is self-service for everyone; and the approver or rejecter is the employee in
/// the caller's employee_id claim, never one named in the body - the service's "direct reporting
/// manager only" rule is worthless if the caller picks who the manager is.
/// </summary>
public class OvertimeControllerAuthorizationTests
{
    private static readonly Guid Caller = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Stranger = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid RequestId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly Mock<IOvertimeService> _service = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly OvertimeController _sut;

    public OvertimeControllerAuthorizationTests()
    {
        // Default caller: a rank-and-file employee holding no privileged role.
        SignInAs(Caller);
        _sut = new OvertimeController(_service.Object, _currentUser.Object);
    }

    private void SignInAs(Guid? employeeId, params string[] roles)
    {
        _currentUser.Setup(c => c.EmployeeId).Returns(employeeId);
        _currentUser.Setup(c => c.IsInRole(It.IsAny<string>())).Returns((string r) => roles.Contains(r));
    }

    public static TheoryData<string> OvertimeReaderRoles => new() { "Admin", "HRManager", "Manager" };

    private static CreateOvertimeRequestDto NewRequestFor(Guid employeeId) => new(
        employeeId, new DateOnly(2026, 9, 10),
        new DateTime(2026, 9, 10, 18, 0, 0), new DateTime(2026, 9, 10, 20, 30, 0), "Month-end close");

    // ---- GET api/overtime-requests ---------------------------------------------------------

    [Fact]
    public async Task GetAll_ForOwnRequests_IsAllowedThrough()
    {
        var result = await _sut.GetAll(Caller, "Pending", 1, 20, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _service.Verify(s => s.GetAllAsync(Caller, "Pending", 1, 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAll_ForAnotherEmployee_ReturnsForbid_WithoutReachingTheService()
    {
        var result = await _sut.GetAll(Stranger, null, 1, 20, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAll_WithNoEmployeeFilter_ReturnsForbid_ForAnUnprivilegedCaller()
    {
        var result = await _sut.GetAll(null, null, 1, 20, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAll_WithNoEmployeeFilter_ReturnsForbid_ForACallerWithNoEmployeeIdClaim()
    {
        // A missing claim (null) must not "equal" a missing filter (null).
        SignInAs(null);

        var result = await _sut.GetAll(null, null, 1, 20, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _service.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(OvertimeReaderRoles))]
    public async Task GetAll_WithNoEmployeeFilter_IsAllowedThrough_ForOvertimeReaders(string role)
    {
        SignInAs(null, role);

        var result = await _sut.GetAll(null, "Pending", 1, 20, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _service.Verify(s => s.GetAllAsync(null, "Pending", 1, 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- POST api/overtime-requests --------------------------------------------------------

    [Fact]
    public async Task Create_ForSelf_IsAllowedThrough()
    {
        var result = await _sut.Create(NewRequestFor(Caller), CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(201);
        _service.Verify(s => s.CreateAsync(
            It.Is<CreateOvertimeRequestDto>(d => d.EmployeeId == Caller), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_ForAnotherEmployee_ReturnsForbid_WithoutFilingAnything()
    {
        var result = await _sut.Create(NewRequestFor(Stranger), CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _service.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(OvertimeReaderRoles))]
    public async Task Create_ForAnotherEmployee_ReturnsForbid_EvenForOvertimeReaders(string role)
    {
        // A manager filing overtime for a report could then approve it themselves.
        SignInAs(Caller, role);

        var result = await _sut.Create(NewRequestFor(Stranger), CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Create_ForACallerWithNoEmployeeIdClaim_ReturnsForbid()
    {
        SignInAs(null, "Admin");

        var result = await _sut.Create(NewRequestFor(Stranger), CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _service.VerifyNoOtherCalls();
    }

    // ---- PUT api/overtime-requests/{id}/approve --------------------------------------------

    [Fact]
    public void Approve_TakesNoApproverFromTheCaller()
    {
        // It used to take an ApproveOvertimeDto whose ApproverId went straight into the service's
        // "only the direct reporting manager" check, so anyone in a manager role could approve any
        // overtime by naming the right manager. Pinning the parameter set stops it coming back.
        var parameters = typeof(OvertimeController)
            .GetMethod(nameof(OvertimeController.Approve), BindingFlags.Public | BindingFlags.Instance)!
            .GetParameters().Select(p => p.Name);

        parameters.Should().BeEquivalentTo(["id", "ct"]);
    }

    [Fact]
    public async Task Approve_ApprovesAsTheEmployeeInTheClaim()
    {
        SignInAs(Caller, "Manager");

        var result = await _sut.Approve(RequestId, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _service.Verify(s => s.ApproveAsync(RequestId, Caller, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Approve_ForACallerWithNoEmployeeIdClaim_ReturnsForbid()
    {
        SignInAs(null, "Admin");

        var result = await _sut.Approve(RequestId, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _service.VerifyNoOtherCalls();
    }

    // ---- PUT api/overtime-requests/{id}/reject ---------------------------------------------

    [Fact]
    public void Reject_TakesNoEmployeeIdFromTheCaller()
    {
        var parameters = typeof(OvertimeController)
            .GetMethod(nameof(OvertimeController.Reject), BindingFlags.Public | BindingFlags.Instance)!
            .GetParameters();

        parameters.Select(p => p.Name).Should().BeEquivalentTo(["id", "dto", "ct"]);
        typeof(RejectOvertimeDto).GetProperties().Select(p => p.Name).Should().Equal(nameof(RejectOvertimeDto.RejectionReason));
    }

    [Fact]
    public async Task Reject_RejectsAsTheEmployeeInTheClaim()
    {
        SignInAs(Caller, "Manager");
        var dto = new RejectOvertimeDto("Not pre-approved");

        var result = await _sut.Reject(RequestId, dto, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _service.Verify(s => s.RejectAsync(RequestId, Caller, dto, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reject_ForACallerWithNoEmployeeIdClaim_ReturnsForbid()
    {
        SignInAs(null, "Admin");

        var result = await _sut.Reject(RequestId, new RejectOvertimeDto("Not pre-approved"), CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public void EveryActionTakingAnEmployeeId_IsCoveredByTheseTests()
    {
        // A new action accepting an employee id (directly or inside a DTO) would otherwise ship
        // with no ownership check and nobody would notice. Add it here once it is guarded and tested.
        string[] covered = [nameof(OvertimeController.GetAll), nameof(OvertimeController.Create)];

        var takingAnEmployeeId = typeof(OvertimeController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetParameters().Any(p =>
                p.Name is not null && (p.Name.Equals("employeeId", StringComparison.OrdinalIgnoreCase)
                                       || p.Name.Equals("approverId", StringComparison.OrdinalIgnoreCase))
                || p.ParameterType.GetProperty("EmployeeId") is not null
                || p.ParameterType.GetProperty("ApproverId") is not null))
            .Select(m => m.Name);

        takingAnEmployeeId.Should().BeSubsetOf(covered);
    }
}
