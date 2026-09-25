using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using PeopleCore.API.Controllers.Analytics;
using PeopleCore.Application.Analytics.DTOs;
using PeopleCore.Application.Analytics.Interfaces;
using PeopleCore.Application.Analytics.Services;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Performance.Interfaces;
using PeopleCore.Application.Recruitment.Interfaces;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Leave;
using Xunit;

namespace PeopleCore.Application.Tests.Analytics;

/// <summary>
/// Leave analytics group balances by type. A confidential type (VAWC) is left out - from the rows,
/// the department view and the executive total - unless the caller holds <c>approvals.all</c>:
/// even a count of VAWC days in a small department can point at a person.
/// </summary>
public class AnalyticsConfidentialLeaveTests
{
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 12, 31);
    private static readonly Guid DepartmentId = Guid.NewGuid();

    private readonly Mock<ILeaveBalanceRepository> _balances = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly HRAnalyticsService _hr;

    public AnalyticsConfidentialLeaveTests()
    {
        var employee = new Employee { Id = Guid.NewGuid(), FirstName = "Maria", LastName = "Santos", DepartmentId = DepartmentId };
        var vacation = new LeaveType { Name = "Vacation Leave", Code = "VL" };
        var vawc = new LeaveType { Name = "VAWC Leave", Code = "VAWC", IsConfidential = true };

        _balances.Setup(r => r.GetByYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            Balance(employee, vacation, total: 15, used: 5),
            Balance(employee, vawc, total: 10, used: 4),
            // A balance whose type did not load is treated as confidential: fail closed.
            new LeaveBalance { EmployeeId = employee.Id, Employee = employee, Year = 2026, TotalDays = 7, UsedDays = 2 },
        ]);
        _employees.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([employee]);

        _hr = new HRAnalyticsService(
            _employees.Object, new Mock<IAttendanceRepository>().Object, new Mock<IOvertimeRepository>().Object,
            _balances.Object, new Mock<IApplicantRepository>().Object, new Mock<IPerformanceReviewRepository>().Object);
    }

    private static LeaveBalance Balance(Employee employee, LeaveType type, decimal total, decimal used) => new()
    {
        EmployeeId = employee.Id, Employee = employee, LeaveTypeId = type.Id, LeaveType = type,
        Year = 2026, TotalDays = total, UsedDays = used
    };

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    public async Task LeaveUtilization_LeavesConfidentialTypesOut_ByDefault(bool? filterByDepartment)
    {
        var response = await _hr.GetLeaveUtilizationAsync(From, To, filterByDepartment is null ? null : DepartmentId);

        response.Data.Select(l => l.LeaveType).Should().Equal("Vacation Leave");
    }

    [Fact]
    public async Task LeaveUtilization_ShowsConfidentialTypes_WhenTheCallerMaySeeThem()
    {
        var response = await _hr.GetLeaveUtilizationAsync(From, To, DepartmentId, showConfidentialTypes: true);

        response.Data.Select(l => l.LeaveType).Should().BeEquivalentTo("Unknown", "VAWC Leave", "Vacation Leave");
    }

    [Theory]
    [InlineData(false, 5)]
    [InlineData(true, 11)]
    public async Task LeaveSummary_CountsConfidentialDays_OnlyWhenTheCallerMaySeeThem(bool showConfidential, decimal expectedDays)
    {
        var executive = new ExecutiveAnalyticsService(
            _employees.Object, _balances.Object, new Mock<IPerformanceReviewRepository>().Object, _hr);

        var summary = await executive.GetLeaveSummaryAsync(From, To, showConfidentialTypes: showConfidential);

        summary.TotalDaysConsumed.Should().Be(expectedDays);
        summary.ByType.Should().HaveCount(showConfidential ? 3 : 1);
    }

    private static Mock<ICurrentUserService> CurrentUser(bool holdsApprovalsAll)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(c => c.HasPermission(It.IsAny<string>()))
                   .Returns((string key) => holdsApprovalsAll && key == Permissions.ApprovalsAll);
        return currentUser;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HrController_ShowsConfidentialTypes_OnlyToApprovalsAll(bool holdsApprovalsAll)
    {
        var service = new Mock<IHRAnalyticsService>();
        service.Setup(s => s.GetLeaveUtilizationAsync(From, To, DepartmentId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new AnalyticsResponse<LeaveUtilization>(new AnalyticsPeriod(From, To), [], DateTime.UtcNow));
        var controller = new HRAnalyticsController(
            service.Object, new MemoryCache(new MemoryCacheOptions()), CurrentUser(holdsApprovalsAll).Object);

        var result = await controller.GetLeaveUtilization(From, To, DepartmentId, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        service.Verify(s => s.GetLeaveUtilizationAsync(From, To, DepartmentId, holdsApprovalsAll, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HrController_DoesNotServeAnApprovalsAllCallersCachedResult_ToAnyoneElse()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new Mock<IHRAnalyticsService>();
        service.Setup(s => s.GetLeaveUtilizationAsync(From, To, null, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((DateOnly _, DateOnly _, Guid? _, bool show, CancellationToken _) =>
                   new AnalyticsResponse<LeaveUtilization>(new AnalyticsPeriod(From, To),
                       show ? [new LeaveUtilization("VAWC Leave", 10, 4, 40)] : [], DateTime.UtcNow));

        await new HRAnalyticsController(service.Object, cache, CurrentUser(true).Object)
            .GetLeaveUtilization(From, To, null, CancellationToken.None);
        var result = await new HRAnalyticsController(service.Object, cache, CurrentUser(false).Object)
            .GetLeaveUtilization(From, To, null, CancellationToken.None);

        var body = (AnalyticsResponse<LeaveUtilization>)((OkObjectResult)result).Value!;
        body.Data.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExecutiveController_ShowsConfidentialTypes_OnlyToApprovalsAll(bool holdsApprovalsAll)
    {
        var service = new Mock<IExecutiveAnalyticsService>();
        service.Setup(s => s.GetLeaveSummaryAsync(From, To, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new LeaveSummary(0, 0, []));
        var controller = new ExecutiveAnalyticsController(
            service.Object, new MemoryCache(new MemoryCacheOptions()), CurrentUser(holdsApprovalsAll).Object);

        var result = await controller.GetLeaveSummary(From, To, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        service.Verify(s => s.GetLeaveSummaryAsync(From, To, holdsApprovalsAll, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecutiveController_DoesNotServeAnApprovalsAllCallersCachedResult_ToAnyoneElse()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new Mock<IExecutiveAnalyticsService>();
        service.Setup(s => s.GetLeaveSummaryAsync(From, To, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((DateOnly _, DateOnly _, bool show, CancellationToken _) => new LeaveSummary(show ? 4 : 0, 0, []));

        await new ExecutiveAnalyticsController(service.Object, cache, CurrentUser(true).Object)
            .GetLeaveSummary(From, To, CancellationToken.None);
        await new ExecutiveAnalyticsController(service.Object, cache, CurrentUser(false).Object)
            .GetLeaveSummary(From, To, CancellationToken.None);

        service.Verify(s => s.GetLeaveSummaryAsync(From, To, false, It.IsAny<CancellationToken>()), Times.Once);
    }
}
