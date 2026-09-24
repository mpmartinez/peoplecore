using FluentAssertions;
using Microsoft.AspNetCore.Http;
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
/// Confidential leave (VAWC) is kept from managers: it is left out of their approval queue, only
/// <c>approvals.all</c> may decide it, anyone else viewing it sees a masked "Leave" with no reason
/// or document, and its balance rows are left out. And a request's supporting document: uploaded
/// by its owner, opened by the owner, <c>approvals.all</c>, or a team approver who manages the
/// employee - unless the type is confidential.
/// </summary>
public class LeaveControllerConfidentialTests
{
    private static readonly Guid RequestId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid VawcTypeId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid VacationTypeId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly Mock<ILeaveTypeService> _types = new();
    private readonly Mock<ILeaveRequestService> _requests = new();
    private readonly Mock<ILeaveBalanceService> _balances = new();
    private readonly Mock<ILeaveDocumentService> _documents = new();
    private readonly SignedInCaller _caller = new();
    private readonly LeaveController _sut;

    public LeaveControllerConfidentialTests()
    {
        _requests.Setup(s => s.GetAllAsync(It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(),
                      It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(PagedResult<LeaveRequestDto>.Create([], 0, 1, 20));
        _documents.Setup(s => s.GetDownloadUrlAsync(RequestId, It.IsAny<CancellationToken>())).ReturnsAsync("https://storage/signed");
        _sut = new LeaveController(
            _types.Object, _requests.Object, _balances.Object, _documents.Object,
            _caller.CurrentUser.Object, _caller.Access);
    }

    private void SignInAs(Guid? employeeId, params string[] roles) => _caller.As(employeeId, roles);

    private static LeaveRequestDto Vawc(Guid employeeId, Guid? id = null) => new(
        id ?? RequestId, employeeId, "Maria Santos", VawcTypeId, "VAWC Leave",
        new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 6), 2, "Protection order hearing",
        LeaveStatus.Pending, null, null, null, DateTime.UtcNow,
        null, 0, true, "barangay-protection-order.pdf", IsConfidential: true);

    private static LeaveRequestDto Vacation(Guid employeeId, Guid? id = null) => new(
        id ?? RequestId, employeeId, "Maria Santos", VacationTypeId, "Vacation Leave",
        new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 6), 2, "Family trip",
        LeaveStatus.Pending, null, null, null, DateTime.UtcNow,
        null, 0, true, "itinerary.pdf", IsConfidential: false);

    private void RequestIs(LeaveRequestDto dto)
        => _requests.Setup(s => s.GetByIdAsync(RequestId, It.IsAny<CancellationToken>())).ReturnsAsync(dto);

    private void EmployeeHasRequests(Guid employeeId, params LeaveRequestDto[] items)
        => _requests.Setup(s => s.GetAllAsync(employeeId, null, It.IsAny<string?>(), 1, 20, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(PagedResult<LeaveRequestDto>.Create(items, items.Length, 1, 20));

    private static void ShouldBeMasked(LeaveRequestDto dto)
    {
        dto.LeaveTypeId.Should().Be(Guid.Empty);
        dto.LeaveTypeName.Should().Be("Leave");
        dto.Reason.Should().BeNull();
        dto.HasDocument.Should().BeFalse();
        dto.DocumentFileName.Should().BeNull();
        dto.IsConfidential.Should().BeFalse();
    }

    private static IReadOnlyList<LeaveRequestDto> ItemsOf(IActionResult result)
        => result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<PagedResult<LeaveRequestDto>>().Which.Items;

    private static LeaveRequestDto BodyOf(IActionResult result)
        => result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<LeaveRequestDto>().Which;

    // ---- the approval queue ----------------------------------------------------------------

    [Fact]
    public async Task Queue_ExcludesConfidentialLeave_ForAManager()
    {
        SignInAs(Caller, "Manager");

        await _sut.GetAll(null, "Pending", 1, 20, CancellationToken.None);

        _requests.Verify(s => s.GetAllAsync(null, Caller, "Pending", 1, 20, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [MemberData(nameof(HrRoles), MemberType = typeof(SignedInCaller))]
    public async Task Queue_IncludesConfidentialLeave_ForApprovalsAll(string role)
    {
        SignInAs(Caller, role);

        await _sut.GetAll(null, "Pending", 1, 20, CancellationToken.None);

        _requests.Verify(s => s.GetAllAsync(null, null, "Pending", 1, 20, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EmployeeList_IsNotFiltered_ItIsMasked()
    {
        // Naming an employee shows their leave, confidential included, but masked below.
        SignInAs(Caller, "Manager");

        await _sut.GetAll(DirectReport, null, 1, 20, CancellationToken.None);

        _requests.Verify(s => s.GetAllAsync(DirectReport, null, null, 1, 20, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- deciding --------------------------------------------------------------------------

    [Fact]
    public async Task Approve_ConfidentialLeave_ReturnsForbid_ForTheEmployeesManager()
    {
        SignInAs(Caller, "Manager");
        RequestIs(Vawc(DirectReport));

        var result = await _sut.Approve(RequestId, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _requests.Verify(s => s.ApproveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reject_ConfidentialLeave_ReturnsForbid_ForTheEmployeesManager()
    {
        SignInAs(Caller, "Manager");
        RequestIs(Vawc(DirectReport));

        var result = await _sut.Reject(RequestId, new RejectLeaveDto("No"), CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _requests.Verify(s => s.RejectAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<RejectLeaveDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Approve_NonConfidentialLeave_IsStillAllowed_ForTheEmployeesManager()
    {
        SignInAs(Caller, "Manager");
        RequestIs(Vacation(DirectReport));
        _requests.Setup(s => s.ApproveAsync(RequestId, Caller, It.IsAny<CancellationToken>())).ReturnsAsync(Vacation(DirectReport));

        var result = await _sut.Approve(RequestId, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Theory]
    [MemberData(nameof(HrRoles), MemberType = typeof(SignedInCaller))]
    public async Task Approve_ConfidentialLeave_IsAllowed_ForApprovalsAll(string role)
    {
        SignInAs(Caller, role);
        RequestIs(Vawc(Stranger));
        _requests.Setup(s => s.ApproveAsync(RequestId, Caller, It.IsAny<CancellationToken>())).ReturnsAsync(Vawc(Stranger));

        var result = await _sut.Approve(RequestId, CancellationToken.None);

        BodyOf(result).LeaveTypeName.Should().Be("VAWC Leave");
    }

    [Theory]
    [MemberData(nameof(HrRoles), MemberType = typeof(SignedInCaller))]
    public async Task Reject_ConfidentialLeave_IsAllowed_ForApprovalsAll(string role)
    {
        SignInAs(Caller, role);
        RequestIs(Vawc(Stranger));
        var dto = new RejectLeaveDto("Incomplete");

        var result = await _sut.Reject(RequestId, dto, CancellationToken.None);

        _requests.Verify(s => s.RejectAsync(RequestId, Caller, dto, It.IsAny<CancellationToken>()), Times.Once);
        result.Should().BeOfType<OkObjectResult>();
    }

    // ---- viewing someone else's leave ------------------------------------------------------

    [Fact]
    public async Task EmployeeList_MasksConfidentialLeave_ForTheirManager()
    {
        SignInAs(Caller, "Manager");
        EmployeeHasRequests(DirectReport, Vawc(DirectReport, Guid.NewGuid()), Vacation(DirectReport, Guid.NewGuid()));

        var items = ItemsOf(await _sut.GetAll(DirectReport, null, 1, 20, CancellationToken.None));

        ShouldBeMasked(items[0]);
        items[0].StartDate.Should().Be(new DateOnly(2026, 10, 5), "the dates still show - it is leave, just not which");
        items[1].LeaveTypeName.Should().Be("Vacation Leave");
        items[1].Reason.Should().Be("Family trip");
        items[1].HasDocument.Should().BeTrue();
    }

    [Fact]
    public async Task EmployeeList_ShowsEverything_ToTheEmployee()
    {
        EmployeeHasRequests(Caller, Vawc(Caller));

        var items = ItemsOf(await _sut.GetAll(Caller, null, 1, 20, CancellationToken.None));

        items[0].LeaveTypeName.Should().Be("VAWC Leave");
        items[0].Reason.Should().Be("Protection order hearing");
        items[0].IsConfidential.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(HrRoles), MemberType = typeof(SignedInCaller))]
    public async Task EmployeeList_ShowsEverything_ToApprovalsAll(string role)
    {
        SignInAs(Caller, role);
        EmployeeHasRequests(Stranger, Vawc(Stranger));

        var items = ItemsOf(await _sut.GetAll(Stranger, null, 1, 20, CancellationToken.None));

        items[0].LeaveTypeName.Should().Be("VAWC Leave");
        items[0].Reason.Should().Be("Protection order hearing");
    }

    [Fact]
    public async Task GetById_MasksConfidentialLeave_ForTheirManager()
    {
        SignInAs(Caller, "Manager");
        RequestIs(Vawc(DirectReport));

        ShouldBeMasked(BodyOf(await _sut.GetById(RequestId, CancellationToken.None)));
    }

    [Fact]
    public async Task GetById_ShowsEverything_ToTheEmployee()
    {
        RequestIs(Vawc(Caller));

        BodyOf(await _sut.GetById(RequestId, CancellationToken.None)).LeaveTypeName.Should().Be("VAWC Leave");
    }

    [Theory]
    [MemberData(nameof(HrRoles), MemberType = typeof(SignedInCaller))]
    public async Task GetById_ShowsEverything_ToApprovalsAll(string role)
    {
        SignInAs(Caller, role);
        RequestIs(Vawc(Stranger));

        BodyOf(await _sut.GetById(RequestId, CancellationToken.None)).Reason.Should().Be("Protection order hearing");
    }

    // ---- balances --------------------------------------------------------------------------

    private void EmployeeHasBalances(Guid employeeId) =>
        _balances.Setup(s => s.GetByEmployeeAsync(employeeId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(
                 [
                     new LeaveBalanceDto(Guid.NewGuid(), employeeId, "Maria Santos", VacationTypeId, "Vacation Leave", 2026, 15, 2, 0, 13, IsConfidential: false),
                     new LeaveBalanceDto(Guid.NewGuid(), employeeId, "Maria Santos", VawcTypeId, "VAWC Leave", 2026, 10, 2, 0, 8, IsConfidential: true),
                 ]);

    private static IReadOnlyList<LeaveBalanceDto> BalancesOf(IActionResult result)
        => (IReadOnlyList<LeaveBalanceDto>)result.Should().BeOfType<OkObjectResult>().Which.Value!;

    [Fact]
    public async Task Balances_HideConfidentialTypes_FromTheirManager()
    {
        SignInAs(Caller, "Manager");
        EmployeeHasBalances(DirectReport);

        var rows = BalancesOf(await _sut.GetBalances(DirectReport, 2026, CancellationToken.None));

        rows.Select(r => r.LeaveTypeName).Should().Equal("Vacation Leave");
    }

    [Fact]
    public async Task Balances_ShowConfidentialTypes_ToTheEmployee()
    {
        EmployeeHasBalances(Caller);

        var rows = BalancesOf(await _sut.GetBalances(Caller, 2026, CancellationToken.None));

        rows.Should().HaveCount(2);
    }

    [Theory]
    [MemberData(nameof(HrRoles), MemberType = typeof(SignedInCaller))]
    public async Task Balances_ShowConfidentialTypes_ToApprovalsAll(string role)
    {
        SignInAs(Caller, role);
        EmployeeHasBalances(Stranger);

        var rows = BalancesOf(await _sut.GetBalances(Stranger, 2026, CancellationToken.None));

        rows.Should().HaveCount(2);
    }

    // ---- opening a document ----------------------------------------------------------------

    private static string? UrlOf(IActionResult result)
    {
        var body = result.Should().BeOfType<OkObjectResult>().Which.Value!;
        return (string?)body.GetType().GetProperty("url")!.GetValue(body);
    }

    [Fact]
    public async Task OpenDocument_IsAllowed_ForTheOwner()
    {
        RequestIs(Vawc(Caller));

        UrlOf(await _sut.GetDocument(RequestId, CancellationToken.None)).Should().Be("https://storage/signed");
    }

    [Theory]
    [MemberData(nameof(HrRoles), MemberType = typeof(SignedInCaller))]
    public async Task OpenDocument_IsAllowed_ForApprovalsAll_EvenOnConfidentialLeave(string role)
    {
        SignInAs(Caller, role);
        RequestIs(Vawc(Stranger));

        UrlOf(await _sut.GetDocument(RequestId, CancellationToken.None)).Should().Be("https://storage/signed");
    }

    [Fact]
    public async Task OpenDocument_IsAllowed_ForTheManagerOfADirectReport_OnVacationLeave()
    {
        SignInAs(Caller, "Manager");
        RequestIs(Vacation(DirectReport));

        UrlOf(await _sut.GetDocument(RequestId, CancellationToken.None)).Should().Be("https://storage/signed");
    }

    [Fact]
    public async Task OpenDocument_ReturnsForbid_ForTheManagerOfADirectReport_OnConfidentialLeave()
    {
        SignInAs(Caller, "Manager");
        RequestIs(Vawc(DirectReport));

        var result = await _sut.GetDocument(RequestId, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _documents.Verify(s => s.GetDownloadUrlAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OpenDocument_ReturnsForbid_ForAManagerOutsideTheirTeam()
    {
        SignInAs(Caller, "Manager");
        RequestIs(Vacation(Stranger));

        (await _sut.GetDocument(RequestId, CancellationToken.None)).Should().BeOfType<ForbidResult>();
        _documents.Verify(s => s.GetDownloadUrlAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OpenDocument_ReturnsForbid_ForAnUnrelatedEmployee()
    {
        RequestIs(Vacation(Stranger));

        (await _sut.GetDocument(RequestId, CancellationToken.None)).Should().BeOfType<ForbidResult>();
        _documents.Verify(s => s.GetDownloadUrlAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OpenDocument_ReturnsForbid_ForACallerWithNoEmployeeIdClaim()
    {
        SignInAs(null);
        RequestIs(Vacation(Stranger));

        (await _sut.GetDocument(RequestId, CancellationToken.None)).Should().BeOfType<ForbidResult>();
    }

    // ---- uploading a document --------------------------------------------------------------

    private static IFormFile APdf() => new FormFile(new MemoryStream(new byte[] { 1, 2, 3 }), 0, 3, "file", "certificate.pdf")
    {
        Headers = new HeaderDictionary(),
        ContentType = "application/pdf"
    };

    [Fact]
    public async Task UploadDocument_UploadsAsTheEmployeeInTheClaim()
    {
        _documents.Setup(s => s.UploadAsync(RequestId, Caller, It.IsAny<Stream>(), "certificate.pdf", "application/pdf", 3, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(Vacation(Caller));

        var result = await _sut.UploadDocument(RequestId, APdf(), CancellationToken.None);

        BodyOf(result).Id.Should().Be(RequestId);
        _documents.Verify(s => s.UploadAsync(RequestId, Caller, It.IsAny<Stream>(), "certificate.pdf", "application/pdf", 3, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadDocument_ReturnsForbid_ForACallerWithNoEmployeeIdClaim()
    {
        SignInAs(null, "Admin");

        var result = await _sut.UploadDocument(RequestId, APdf(), CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _documents.VerifyNoOtherCalls();
    }
}
