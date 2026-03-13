using FluentAssertions;
using Moq;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.PayrollIntegration.Services;
using PeopleCore.Domain.Entities.Attendance;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Enums;

namespace PeopleCore.Application.Tests.PayrollIntegration;

public class PayrollExportServiceTests
{
    private readonly Mock<IEmployeeRepository> _employeeRepo = new();
    private readonly Mock<IAttendanceRepository> _attendanceRepo = new();
    private readonly Mock<ILeaveRequestRepository> _leaveRepo = new();
    private readonly Mock<IOvertimeRepository> _overtimeRepo = new();
    private readonly PayrollExportService _sut;

    public PayrollExportServiceTests()
    {
        _sut = new PayrollExportService(
            _employeeRepo.Object,
            _attendanceRepo.Object,
            _leaveRepo.Object,
            _overtimeRepo.Object);
    }

    [Fact]
    public async Task GetEmployeeMasterDataAsync_ReturnsOnlyActiveEmployees()
    {
        var active = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeNumber = "EMP001",
            FirstName = "Jane",
            LastName = "Doe",
            WorkEmail = "jane@test.com",
            IsActive = true,
            EmploymentStatus = EmploymentStatus.Regular,
            EmploymentType = "FullTime",
            HireDate = new DateOnly(2020, 1, 1),
            Department = new Domain.Entities.Organization.Department { Name = "Engineering" },
            Position = new Domain.Entities.Organization.Position { Title = "Developer" },
            GovernmentIds =
            [
                new EmployeeGovernmentId { IdType = GovernmentIdType.SSS, IdNumber = "SSS123" },
                new EmployeeGovernmentId { IdType = GovernmentIdType.TIN, IdNumber = "TIN456" }
            ]
        };

        var inactive = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeNumber = "EMP002",
            FirstName = "John",
            LastName = "Smith",
            WorkEmail = "john@test.com",
            IsActive = false,
            EmploymentStatus = EmploymentStatus.Contractual,
            EmploymentType = "FullTime",
            HireDate = new DateOnly(2019, 1, 1),
            GovernmentIds = []
        };

        _employeeRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([active, inactive]);

        var result = await _sut.GetEmployeeMasterDataAsync();

        result.Should().HaveCount(1);
        result[0].EmployeeNumber.Should().Be("EMP001");
        result[0].SssNumber.Should().Be("SSS123");
        result[0].TinNumber.Should().Be("TIN456");
        result[0].PhilHealthNumber.Should().BeNull();
    }

    [Fact]
    public async Task GetAttendanceSummaryAsync_SingleEmployee_AggregatesCorrectly()
    {
        var employeeId = Guid.NewGuid();
        var from = new DateOnly(2025, 1, 1);
        var to = new DateOnly(2025, 1, 31);

        var employee = new Employee
        {
            Id = employeeId,
            EmployeeNumber = "EMP001",
            FirstName = "Jane",
            LastName = "Doe",
            WorkEmail = "jane@test.com",
            IsActive = true,
            EmploymentStatus = EmploymentStatus.Regular,
            EmploymentType = "FullTime",
            HireDate = new DateOnly(2020, 1, 1),
            GovernmentIds = []
        };

        var records = new List<AttendanceRecord>
        {
            new() { EmployeeId = employeeId, Employee = employee, AttendanceDate = new DateOnly(2025, 1, 2), IsPresent = true, LateMinutes = 10, UndertimeMinutes = 0, OvertimeMinutes = 30, IsHoliday = false },
            new() { EmployeeId = employeeId, Employee = employee, AttendanceDate = new DateOnly(2025, 1, 3), IsPresent = true, LateMinutes = 0, UndertimeMinutes = 15, OvertimeMinutes = 0, IsHoliday = true, HolidayType = HolidayType.RegularHoliday },
            new() { EmployeeId = employeeId, Employee = employee, AttendanceDate = new DateOnly(2025, 1, 6), IsPresent = false, LateMinutes = 0, UndertimeMinutes = 0, OvertimeMinutes = 0, IsHoliday = false },
        };

        _employeeRepo.Setup(r => r.GetByIdAsync(employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);
        _attendanceRepo.Setup(r => r.GetByEmployeeAndPeriodAsync(employeeId, from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

        var result = await _sut.GetAttendanceSummaryAsync(from, to, employeeId);

        result.Should().HaveCount(1);
        var summary = result[0];
        summary.DaysPresent.Should().Be(2);
        summary.TotalLateMinutes.Should().Be(10);
        summary.TotalUndertimeMinutes.Should().Be(15);
        summary.TotalApprovedOvertimeMinutes.Should().Be(30);
        summary.RegularHolidaysWorked.Should().Be(1);
        summary.SpecialHolidaysWorked.Should().Be(0);
    }

    [Fact]
    public async Task GetApprovedLeavesAsync_MapsLeaveRequestsCorrectly()
    {
        var from = new DateOnly(2025, 1, 1);
        var to = new DateOnly(2025, 1, 31);

        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeNumber = "EMP001",
            FirstName = "Jane",
            LastName = "Doe",
            WorkEmail = "jane@test.com",
            IsActive = true,
            EmploymentStatus = EmploymentStatus.Regular,
            EmploymentType = "FullTime",
            HireDate = new DateOnly(2020, 1, 1),
            GovernmentIds = []
        };

        var leaveType = new LeaveType
        {
            Id = Guid.NewGuid(),
            Code = "VL",
            Name = "Vacation Leave",
            IsPaid = true,
            MaxDaysPerYear = 15
        };

        var approvedAt = new DateTime(2025, 1, 5, 10, 0, 0, DateTimeKind.Utc);
        var leaveRequest = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            Employee = employee,
            LeaveTypeId = leaveType.Id,
            LeaveType = leaveType,
            StartDate = new DateOnly(2025, 1, 10),
            EndDate = new DateOnly(2025, 1, 12),
            TotalDays = 3,
            Status = LeaveStatus.Approved,
            ApprovedAt = approvedAt
        };

        _leaveRepo.Setup(r => r.GetApprovedByPeriodAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync([leaveRequest]);

        var result = await _sut.GetApprovedLeavesAsync(from, to);

        result.Should().HaveCount(1);
        var dto = result[0];
        dto.LeaveTypeCode.Should().Be("VL");
        dto.LeaveTypeName.Should().Be("Vacation Leave");
        dto.IsPaid.Should().BeTrue();
        dto.TotalDays.Should().Be(3);
        dto.ApprovedAt.Should().Be(approvedAt);
        dto.EmployeeNumber.Should().Be("EMP001");
    }
}
