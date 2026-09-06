using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PeopleCore.API.Controllers.Payroll;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Payroll.Interfaces;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

/// <summary>
/// Covers the design spec's required regression guard: a caller with no employee_id claim must
/// be refused outright, not quietly handed some arbitrary employee's payslip. Both self-service
/// actions resolve the caller from <see cref="ICurrentUserService.EmployeeId"/> alone (see
/// <see cref="ReportsController"/>'s remarks) and both explicitly return <see cref="ForbidResult"/>
/// when that claim is missing - this test is what stands guard on that behaviour going forward.
/// </summary>
public class ReportsControllerTests
{
    private readonly Mock<IPayslipService> _payslips = new();
    private readonly Mock<IPayrollRunService> _runs = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly ReportsController _sut;

    public ReportsControllerTests()
    {
        _currentUser.Setup(c => c.EmployeeId).Returns((Guid?)null);
        _sut = new ReportsController(_payslips.Object, _runs.Object, _currentUser.Object);
    }

    [Fact]
    public async Task GetMyPayslip_WhenTheCallerHasNoEmployeeIdClaim_ReturnsForbid()
    {
        var result = await _sut.GetMyPayslip(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _payslips.Verify(
            p => p.GenerateForSelfServiceAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetMyPayslips_WhenTheCallerHasNoEmployeeIdClaim_ReturnsForbid()
    {
        var result = await _sut.GetMyPayslips(CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        _payslips.Verify(
            p => p.GetMyPayslipsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
