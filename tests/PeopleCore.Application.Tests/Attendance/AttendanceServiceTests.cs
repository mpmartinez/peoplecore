using FluentAssertions;
using Moq;
using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Attendance.Services;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Domain.Entities.Attendance;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Attendance;

public class AttendanceServiceTests
{
    private readonly Mock<IAttendanceRepository> _repo = new();
    private readonly Mock<IHolidayService> _holidayService = new();
    private readonly Mock<IEmployeeRepository> _employeeRepo = new();
    private readonly AttendanceService _sut;

    public AttendanceServiceTests()
    {
        _sut = new AttendanceService(_repo.Object, _holidayService.Object, _employeeRepo.Object);
    }

    private static Employee MakeEmployee(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        EmployeeNumber = "EMP-001",
        FirstName = "Juan",
        LastName = "dela Cruz",
        DateOfBirth = new DateOnly(1990, 1, 1),
        Gender = "Male",
        WorkEmail = "juan@test.com",
        EmploymentType = "FullTime",
        HireDate = new DateOnly(2020, 1, 1),
        IsActive = true
    };

    [Fact]
    public async Task TimeInAsync_WhenEmployeeNotFound_ThrowsKeyNotFoundException()
    {
        _employeeRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default))
                     .ReturnsAsync((Employee?)null);

        var act = () => _sut.TimeInAsync(new TimeInRequest(Guid.NewGuid(), DateTime.UtcNow));

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task TimeInAsync_WhenAlreadyClockedIn_ThrowsDomainException()
    {
        var emp = MakeEmployee();
        var existing = new AttendanceRecord
        {
            EmployeeId = emp.Id,
            AttendanceDate = DateOnly.FromDateTime(DateTime.UtcNow),
            TimeIn = DateTime.UtcNow.AddHours(-2)
        };
        _employeeRepo.Setup(r => r.GetByIdAsync(emp.Id, default)).ReturnsAsync(emp);
        _repo.Setup(r => r.GetByEmployeeAndDateAsync(emp.Id, It.IsAny<DateOnly>(), default))
             .ReturnsAsync(existing);

        var act = () => _sut.TimeInAsync(new TimeInRequest(emp.Id, DateTime.UtcNow));

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("*already clocked in*");
    }

    [Fact]
    public async Task TimeOutAsync_WhenNotClockedIn_ThrowsDomainException()
    {
        var emp = MakeEmployee();
        _employeeRepo.Setup(r => r.GetByIdAsync(emp.Id, default)).ReturnsAsync(emp);
        _repo.Setup(r => r.GetByEmployeeAndDateAsync(emp.Id, It.IsAny<DateOnly>(), default))
             .ReturnsAsync((AttendanceRecord?)null);

        var act = () => _sut.TimeOutAsync(new TimeOutRequest(emp.Id, DateTime.UtcNow));

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("*not clocked in*");
    }

    [Fact]
    public async Task TimeInAsync_At9AM_CalculatesLateMinutesCorrectly()
    {
        // Standard shift: 8:00 AM. Employee comes in at 9:05 AM = 65 minutes late.
        var emp = MakeEmployee();
        var timeIn = new DateTime(2025, 1, 6, 9, 5, 0, DateTimeKind.Utc);
        _employeeRepo.Setup(r => r.GetByIdAsync(emp.Id, default)).ReturnsAsync(emp);
        _repo.Setup(r => r.GetByEmployeeAndDateAsync(emp.Id, DateOnly.FromDateTime(timeIn), default))
             .ReturnsAsync((AttendanceRecord?)null);
        _holidayService.Setup(h => h.IsHolidayAsync(It.IsAny<DateOnly>(), default)).ReturnsAsync(false);
        _repo.Setup(r => r.AddAsync(It.IsAny<AttendanceRecord>(), default))
             .ReturnsAsync((AttendanceRecord a, CancellationToken _) => a);

        var result = await _sut.TimeInAsync(new TimeInRequest(emp.Id, timeIn));

        result.LateMinutes.Should().Be(65);
    }
}
