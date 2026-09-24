using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PeopleCore.API.Controllers.PayrollExport;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.PayrollIntegration.DTOs;
using PeopleCore.Application.PayrollIntegration.Interfaces;
using PeopleCore.Application.PayrollIntegration.Services;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Enums;
using Xunit;

namespace PeopleCore.Application.Tests.PayrollIntegration;

/// <summary>
/// The approved-leaves export is for <c>payroll.manage</c>, which needs the dates and whether the
/// leave is paid - not which leave it was. A confidential type (VAWC) goes out as "Leave" with no
/// code unless the caller also holds <c>approvals.all</c>.
/// </summary>
public class PayrollExportConfidentialLeaveTests
{
    private readonly Mock<ILeaveRequestRepository> _leaveRepo = new();
    private readonly PayrollExportService _sut;
    private static readonly DateOnly From = new(2026, 10, 1);
    private static readonly DateOnly To = new(2026, 10, 31);

    public PayrollExportConfidentialLeaveTests()
    {
        _sut = new PayrollExportService(
            new Mock<IEmployeeRepository>().Object, new Mock<IAttendanceRepository>().Object,
            _leaveRepo.Object, new Mock<IOvertimeRepository>().Object);

        var employee = new Employee { Id = Guid.NewGuid(), EmployeeNumber = "EMP-001", FirstName = "Maria", LastName = "Santos" };
        _leaveRepo.Setup(r => r.GetApprovedByPeriodAsync(From, To, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(
                  [
                      Approved(employee, new LeaveType { Name = "VAWC Leave", Code = "VAWC", IsPaid = true, IsConfidential = true }, 5),
                      Approved(employee, new LeaveType { Name = "Vacation Leave", Code = "VL", IsPaid = true }, 20),
                  ]);
    }

    private static LeaveRequest Approved(Employee employee, LeaveType type, int day) => new()
    {
        EmployeeId = employee.Id, Employee = employee, LeaveTypeId = type.Id, LeaveType = type,
        StartDate = new DateOnly(2026, 10, day), EndDate = new DateOnly(2026, 10, day + 1), TotalDays = 2,
        Status = LeaveStatus.Approved, ApprovedAt = new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public async Task ConfidentialTypes_GoOutAsLeave_WithNoCode_ButKeepTheDatesAndPay()
    {
        var rows = await _sut.GetApprovedLeavesAsync(From, To, showConfidentialTypes: false);

        var vawc = rows.Single(r => r.StartDate == new DateOnly(2026, 10, 5));
        vawc.LeaveTypeName.Should().Be("Leave");
        vawc.LeaveTypeCode.Should().BeEmpty();
        vawc.IsPaid.Should().BeTrue();
        vawc.TotalDays.Should().Be(2);
        vawc.EndDate.Should().Be(new DateOnly(2026, 10, 6));

        var vacation = rows.Single(r => r.StartDate == new DateOnly(2026, 10, 20));
        vacation.LeaveTypeName.Should().Be("Vacation Leave");
        vacation.LeaveTypeCode.Should().Be("VL");
    }

    [Fact]
    public async Task ConfidentialTypes_AreNamed_WhenTheCallerMaySeeThem()
    {
        var rows = await _sut.GetApprovedLeavesAsync(From, To, showConfidentialTypes: true);

        rows.Single(r => r.StartDate == new DateOnly(2026, 10, 5)).LeaveTypeName.Should().Be("VAWC Leave");
        rows.Single(r => r.StartDate == new DateOnly(2026, 10, 5)).LeaveTypeCode.Should().Be("VAWC");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Controller_ShowsConfidentialTypes_OnlyToApprovalsAll(bool holdsApprovalsAll)
    {
        var service = new Mock<IPayrollExportService>();
        service.Setup(s => s.GetApprovedLeavesAsync(From, To, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Array.Empty<PayrollLeaveDeductionDto>());
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(c => c.HasPermission(It.IsAny<string>()))
                   .Returns((string key) => key == Permissions.PayrollManage || (holdsApprovalsAll && key == Permissions.ApprovalsAll));
        var controller = new PayrollExportController(service.Object, currentUser.Object);

        var result = await controller.GetApprovedLeaves(From, To, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        service.Verify(s => s.GetApprovedLeavesAsync(From, To, holdsApprovalsAll, It.IsAny<CancellationToken>()), Times.Once);
    }
}
