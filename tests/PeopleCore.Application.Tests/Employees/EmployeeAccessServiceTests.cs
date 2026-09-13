using FluentAssertions;
using Moq;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Employees.Services;
using Xunit;

namespace PeopleCore.Application.Tests.Employees;

/// <summary>
/// The one place the reach of each role is decided: HR staff reach everyone, a Manager reaches
/// their direct reports, everybody reaches themselves.
/// </summary>
public class EmployeeAccessServiceTests
{
    private static readonly Guid Caller = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DirectReport = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Stranger = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly EmployeeAccessService _sut;

    public EmployeeAccessServiceTests()
    {
        SignInAs(Caller);
        _employees.Setup(r => r.IsDirectReportAsync(DirectReport, Caller, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _sut = new EmployeeAccessService(_currentUser.Object, _employees.Object);
    }

    private void SignInAs(Guid? employeeId, params string[] roles)
    {
        _currentUser.Setup(c => c.EmployeeId).Returns(employeeId);
        _currentUser.Setup(c => c.IsInRole(It.IsAny<string>())).Returns((string r) => roles.Contains(r));
    }

    public static TheoryData<string> HrRoles => new() { "Admin", "HRManager" };

    // ---- HR staff --------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(HrRoles))]
    public async Task HrStaff_ReachEveryone_WithoutLookingUpReportingLines(string role)
    {
        SignInAs(null, role);

        _sut.IsHrStaff.Should().BeTrue();
        (await _sut.CanManageAsync(Stranger)).Should().BeTrue();
        (await _sut.CanViewAsync(Stranger)).Should().BeTrue();
        _sut.GetUnfilteredListScope().Should().Be(EmployeeListScope.Everyone);
        _employees.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AManagerWhoIsAlsoHr_IsNotNarrowedToTheirTeam()
    {
        SignInAs(Caller, "Manager", "HRManager");

        (await _sut.CanManageAsync(Stranger)).Should().BeTrue();
        _sut.GetUnfilteredListScope().Should().Be(EmployeeListScope.Everyone);
    }

    // ---- Managers --------------------------------------------------------------------------

    [Fact]
    public async Task AManager_ManagesAndViewsTheirDirectReport()
    {
        SignInAs(Caller, "Manager");

        _sut.IsHrStaff.Should().BeFalse();
        (await _sut.CanManageAsync(DirectReport)).Should().BeTrue();
        (await _sut.CanViewAsync(DirectReport)).Should().BeTrue();
    }

    [Fact]
    public async Task AManager_NeitherManagesNorViewsSomeoneOutsideTheirTeam()
    {
        SignInAs(Caller, "Manager");

        (await _sut.CanManageAsync(Stranger)).Should().BeFalse();
        (await _sut.CanViewAsync(Stranger)).Should().BeFalse();
    }

    [Fact]
    public async Task AManager_ViewsButDoesNotManageThemselves()
    {
        SignInAs(Caller, "Manager");

        (await _sut.CanViewAsync(Caller)).Should().BeTrue();
        (await _sut.CanManageAsync(Caller)).Should().BeFalse("managing yourself is not what the role grants");
    }

    [Fact]
    public void AManagersUnfilteredList_IsTheirDirectReports()
    {
        SignInAs(Caller, "Manager");

        _sut.GetUnfilteredListScope().Should().Be(EmployeeListScope.DirectReportsOf(Caller));
    }

    [Fact]
    public async Task AManagerWithNoEmployeeIdClaim_HasNoTeam()
    {
        // No employee id means no one reports to them; a null must never match an employee whose
        // ReportingManagerId is also null.
        SignInAs(null, "Manager");

        (await _sut.CanManageAsync(Stranger)).Should().BeFalse();
        (await _sut.CanViewAsync(Stranger)).Should().BeFalse();
        _sut.GetUnfilteredListScope().IsAllowed.Should().BeFalse();
        _employees.VerifyNoOtherCalls();
    }

    // ---- Everybody else --------------------------------------------------------------------

    [Fact]
    public async Task AnEmployee_ViewsOnlyThemselves_AndManagesNoOne()
    {
        (await _sut.CanViewAsync(Caller)).Should().BeTrue();
        (await _sut.CanViewAsync(DirectReport)).Should().BeFalse("a reporting line only counts for the Manager role");
        (await _sut.CanManageAsync(DirectReport)).Should().BeFalse();
        _sut.GetUnfilteredListScope().IsAllowed.Should().BeFalse();
        _employees.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ACallerWithNoEmployeeIdClaim_ViewsNoOne()
    {
        SignInAs(null);

        (await _sut.CanViewAsync(Stranger)).Should().BeFalse();
        _sut.GetUnfilteredListScope().IsAllowed.Should().BeFalse();
    }
}
