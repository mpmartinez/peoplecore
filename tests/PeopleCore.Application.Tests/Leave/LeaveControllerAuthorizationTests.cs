using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PeopleCore.API.Controllers.Leave;
using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Leave.DTOs;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Tests.Common;
using PeopleCore.Domain.Enums;
using Xunit;
using static PeopleCore.Application.Tests.Common.SignedInCaller;

namespace PeopleCore.Application.Tests.Leave;

/// <summary>
/// <see cref="LeaveController"/> sits behind a class-level [Authorize] only, and "signed in" says
/// nothing about WHICH employee's leave the caller may touch. Every employee id that reaches it -
/// in the route, the query string or the request body - is caller-supplied. These tests pin the
/// rules: HR staff (<c>Admin,HRManager</c>) reach anyone's leave; a Manager reaches their direct
/// reports'; everybody reaches their own; and filing or cancelling leave is self-service for
/// everyone, resolved from the employee_id claim.
/// </summary>
public class LeaveControllerAuthorizationTests
{
    private static readonly Guid RequestId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid LeaveTypeId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly Mock<ILeaveTypeService> _types = new();
    private readonly Mock<ILeaveRequestService> _requests = new();
    private readonly Mock<ILeaveBalanceService> _balances = new();
    private readonly Mock<ILeaveDocumentService> _documents = new();
    private readonly SignedInCaller _caller = new();
    private readonly LeaveController _sut;

    public LeaveControllerAuthorizationTests()
    {
        _requests.Setup(s => s.GetAllAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(),
                      It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(PagedResult<LeaveRequestDto>.Create([], 0, 1, 20));
        _balances.Setup(s => s.GetByEmployeeAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync([]);
        _sut = new LeaveController(
            _types.Object, _requests.Object, _balances.Object, _documents.Object, _caller.CurrentUser.Object, _caller.Access);
    }

    private void SignInAs(Guid? employeeId, params string[] roles) => _caller.As(employeeId, roles);

    private void VerifyNoServiceReached()
    {
        _requests.VerifyNoOtherCalls();
        _balances.VerifyNoOtherCalls();
    }

    private void RequestIsOwnedBy(Guid employeeId)
        => _requests.Setup(s => s.GetByIdAsync(RequestId, It.IsAny<CancellationToken>())).ReturnsAsync(RequestOf(employeeId));

    private static LeaveRequestDto RequestOf(Guid employeeId) => new(
        RequestId, employeeId, "Maria Santos", LeaveTypeId, "Vacation Leave",
        new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 6), 2, "Family trip",
        LeaveStatus.Pending, null, null, null, DateTime.UtcNow,
        null, 0, false, null, false);

    private static CreateLeaveRequestDto NewRequestFor(Guid employeeId) =>
        new(employeeId, LeaveTypeId, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 6), "Family trip");

    // ---- GET api/leave-requests ------------------------------------------------------------

    [Fact]
    public async Task GetAll_ForOwnRequests_IsAllowedThrough()
    {
        var result = await _sut.GetAll(Caller, "Pending", 1, 20, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _requests.Verify(s => s.GetAllAsync(Caller, null, "Pending", 1, 20, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAll_ForAnotherEmployee_ReturnsForbid_WithoutReachingTheService()
    {
        var result = await _sut.GetAll(Stranger, null, 1, 20, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        VerifyNoServiceReached();
    }

    [Fact]
    public async Task GetAll_ForADirectReport_IsAllowedThrough_ForTheirManager()
    {
        SignInAs(Caller, "Manager");

        var result = await _sut.GetAll(DirectReport, null, 1, 20, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _requests.Verify(s => s.GetAllAsync(DirectReport, null, null, 1, 20, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAll_ForSomeoneOutsideTheirTeam_ReturnsForbid_ForAManager()
    {
        SignInAs(Caller, "Manager");

        var result = await _sut.GetAll(Stranger, null, 1, 20, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        VerifyNoServiceReached();
    }

    [Theory]
    [MemberData(nameof(HrRoles), MemberType = typeof(SignedInCaller))]
    public async Task GetAll_ForAnyEmployee_IsAllowedThrough_ForHrStaff(string role)
    {
        SignInAs(null, role);

        var result = await _sut.GetAll(Stranger, null, 1, 20, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
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
    [MemberData(nameof(HrRoles), MemberType = typeof(SignedInCaller))]
    public async Task GetAll_WithNoEmployeeFilter_IsEveryonesRequests_ForHrStaff(string role)
    {
        SignInAs(null, role);

        var result = await _sut.GetAll(null, "Pending", 1, 20, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _requests.Verify(s => s.GetAllAsync(null, null, "Pending", 1, 20, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAll_WithNoEmployeeFilter_IsTheirDirectReportsRequests_ForAManager()
    {
        // The approval queue: narrowed to the team in the query itself, so the count and pages match.
        SignInAs(Caller, "Manager");

        var result = await _sut.GetAll(null, "Pending", 1, 20, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _requests.Verify(s => s.GetAllAsync(null, Caller, "Pending", 1, 20, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAll_WithNoEmployeeFilter_ReturnsForbid_ForAManagerWithNoEmployeeIdClaim()
    {
        // Nobody reports to an account that is not an employee; a null manager id must not become
        // "no filter".
        SignInAs(null, "Manager");

        var result = await _sut.GetAll(null, null, 1, 20, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        VerifyNoServiceReached();
    }

    // ---- GET api/leave-requests/{id} -------------------------------------------------------

    [Fact]
    public async Task GetById_ForOwnRequest_IsAllowedThrough()
    {
        RequestIsOwnedBy(Caller);

        var result = await _sut.GetById(RequestId, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<LeaveRequestDto>();
    }

    [Fact]
    public async Task GetById_ForAnotherEmployeesRequest_ReturnsForbid()
    {
        RequestIsOwnedBy(Stranger);

        var result = await _sut.GetById(RequestId, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>("the request's reason and dates belong to someone else");
    }

    [Fact]
    public async Task GetById_ForACallerWithNoEmployeeIdClaim_ReturnsForbid()
    {
        SignInAs(null);
        RequestIsOwnedBy(Stranger);

        var result = await _sut.GetById(RequestId, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
    }

    [Theory]
    [MemberData(nameof(HrRoles), MemberType = typeof(SignedInCaller))]
    public async Task GetById_ForAnotherEmployeesRequest_IsAllowedThrough_ForHrStaff(string role)
    {
        SignInAs(Caller, role);
        RequestIsOwnedBy(Stranger);

        var result = await _sut.GetById(RequestId, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetById_ForADirectReportsRequest_IsAllowedThrough_ForTheirManager()
    {
        SignInAs(Caller, "Manager");
        RequestIsOwnedBy(DirectReport);

        var result = await _sut.GetById(RequestId, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetById_ForARequestOutsideTheirTeam_ReturnsForbid_ForAManager()
    {
        SignInAs(Caller, "Manager");
        RequestIsOwnedBy(Stranger);

        var result = await _sut.GetById(RequestId, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
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
    [MemberData(nameof(DeciderRoles), MemberType = typeof(SignedInCaller))]
    public async Task Create_ForSomeoneElse_ReturnsForbid_EvenForHrStaffAndTheirManager(string role)
    {
        // Filing leave is self-service only: seeing someone's leave is not a licence to spend
        // their balance.
        SignInAs(Caller, role);

        var result = await _sut.Create(NewRequestFor(DirectReport), CancellationToken.None);

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

    // ---- PUT api/leave-requests/{id}/approve -----------------------------------------------

    [Fact]
    public void Approve_TakesNoApproverFromTheCaller()
    {
        // It used to take an ApproveLeaveDto whose ApproverId was stored as ApprovedBy. The web
        // client sent {}, so every approval was recorded against Guid.Empty, and any other client
        // could record the approval against whoever it liked.
        var parameters = typeof(LeaveController)
            .GetMethod(nameof(LeaveController.Approve), BindingFlags.Public | BindingFlags.Instance)!
            .GetParameters().Select(p => p.Name);

        parameters.Should().BeEquivalentTo(["id", "ct"]);
    }

    [Theory]
    [MemberData(nameof(HrRoles), MemberType = typeof(SignedInCaller))]
    public async Task Approve_AnyonesRequest_ApprovesAsTheEmployeeInTheClaim_ForHrStaff(string role)
    {
        SignInAs(Caller, role);
        RequestIsOwnedBy(Stranger);

        var result = await _sut.Approve(RequestId, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _requests.Verify(s => s.ApproveAsync(RequestId, Caller, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Approve_ADirectReportsRequest_IsAllowedThrough_ForTheirManager()
    {
        SignInAs(Caller, "Manager");
        RequestIsOwnedBy(DirectReport);

        var result = await _sut.Approve(RequestId, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _requests.Verify(s => s.ApproveAsync(RequestId, Caller, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Approve_ARequestOutsideTheirTeam_ReturnsForbid_ForAManager()
    {
        SignInAs(Caller, "Manager");
        RequestIsOwnedBy(Stranger);

        var result = await _sut.Approve(RequestId, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _requests.Verify(s => s.ApproveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Approve_TheirOwnRequest_IsLeftToTheServiceToRefuseWithItsReason()
    {
        // A Manager does not "manage" themselves, but a bare 403 would hide why; the service
        // refuses self-approval with "You cannot approve your own leave request".
        SignInAs(Caller, "Manager");
        RequestIsOwnedBy(Caller);

        await _sut.Approve(RequestId, CancellationToken.None);

        _requests.Verify(s => s.ApproveAsync(RequestId, Caller, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Approve_ForACallerWithNoEmployeeIdClaim_ReturnsForbid()
    {
        // The approval is recorded against an employee; an account with no employee record has
        // nobody to record.
        SignInAs(null, "Admin");

        var result = await _sut.Approve(RequestId, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        VerifyNoServiceReached();
    }

    // ---- PUT api/leave-requests/{id}/reject ------------------------------------------------

    [Fact]
    public void Reject_TakesNoEmployeeIdFromTheCaller()
    {
        var parameters = typeof(LeaveController)
            .GetMethod(nameof(LeaveController.Reject), BindingFlags.Public | BindingFlags.Instance)!
            .GetParameters().Select(p => p.Name);

        parameters.Should().BeEquivalentTo(["id", "dto", "ct"]);
        typeof(RejectLeaveDto).GetProperties().Select(p => p.Name).Should().Equal(nameof(RejectLeaveDto.RejectionReason));
    }

    [Theory]
    [MemberData(nameof(HrRoles), MemberType = typeof(SignedInCaller))]
    public async Task Reject_AnyonesRequest_RejectsAsTheEmployeeInTheClaim_ForHrStaff(string role)
    {
        SignInAs(Caller, role);
        RequestIsOwnedBy(Stranger);
        var dto = new RejectLeaveDto("Peak season");

        var result = await _sut.Reject(RequestId, dto, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _requests.Verify(s => s.RejectAsync(RequestId, Caller, dto, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reject_ADirectReportsRequest_IsAllowedThrough_ForTheirManager()
    {
        SignInAs(Caller, "Manager");
        RequestIsOwnedBy(DirectReport);
        var dto = new RejectLeaveDto("Peak season");

        var result = await _sut.Reject(RequestId, dto, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _requests.Verify(s => s.RejectAsync(RequestId, Caller, dto, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reject_ARequestOutsideTheirTeam_ReturnsForbid_ForAManager()
    {
        SignInAs(Caller, "Manager");
        RequestIsOwnedBy(Stranger);

        var result = await _sut.Reject(RequestId, new RejectLeaveDto("Peak season"), CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _requests.Verify(s => s.RejectAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<RejectLeaveDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reject_ForACallerWithNoEmployeeIdClaim_ReturnsForbid()
    {
        // Without an employee there is no way to tell whether the leave being rejected is the
        // caller's own.
        SignInAs(null, "Admin");

        var result = await _sut.Reject(RequestId, new RejectLeaveDto("Peak season"), CancellationToken.None);

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
    [MemberData(nameof(HrRoles), MemberType = typeof(SignedInCaller))]
    public async Task GetBalances_ForAnotherEmployee_IsAllowedThrough_ForHrStaff(string role)
    {
        SignInAs(Caller, role);

        var result = await _sut.GetBalances(Stranger, null, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetBalances_ForADirectReport_IsAllowedThrough_ForTheirManager()
    {
        SignInAs(Caller, "Manager");

        var result = await _sut.GetBalances(DirectReport, null, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetBalances_ForSomeoneOutsideTheirTeam_ReturnsForbid_ForAManager()
    {
        SignInAs(Caller, "Manager");

        var result = await _sut.GetBalances(Stranger, null, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        VerifyNoServiceReached();
    }

    // ---- POST api/leave-types/statutory ---------------------------------------------------

    [Fact]
    public async Task AddStatutoryLeaveTypes_IsAPostToLeaveTypesStatutory_ReturningWhatWasAddedAndSkipped()
    {
        // leave.manage is pinned by PermissionEquivalenceTests.
        var action = typeof(LeaveController).GetMethod(nameof(LeaveController.AddStatutoryLeaveTypes))!;
        action.GetCustomAttribute<HttpPostAttribute>()!.Template.Should().Be("leave-types/statutory");
        var outcome = new StatutoryLeaveResultDto(["ML"], ["SIL"]);
        _types.Setup(s => s.AddStatutoryAsync(It.IsAny<CancellationToken>())).ReturnsAsync(outcome);

        var result = await _sut.AddStatutoryLeaveTypes(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(outcome);
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
