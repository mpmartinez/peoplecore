using FluentAssertions;
using Moq;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Leave.DTOs;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Leave.Services;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Leave;

public class LeaveRequestServiceTests
{
    private readonly Mock<ILeaveRequestRepository> _leaveRepo = new();
    private readonly Mock<ILeaveBalanceRepository> _balanceRepo = new();
    private readonly Mock<IHolidayService> _holidayService = new();
    private readonly Mock<IEmployeeRepository> _employeeRepo = new();
    private readonly Mock<ILeaveTypeRepository> _leaveTypeRepo = new();
    private readonly LeaveRequestService _sut;

    public LeaveRequestServiceTests()
    {
        _sut = new LeaveRequestService(_leaveRepo.Object, _balanceRepo.Object, _holidayService.Object, _employeeRepo.Object, _leaveTypeRepo.Object);
    }

    private static Employee MakeEmployee(string gender = "Male") => new()
    {
        Id = Guid.NewGuid(),
        EmployeeNumber = "EMP-001",
        FirstName = "Juan",
        LastName = "dela Cruz",
        DateOfBirth = new DateOnly(1990, 1, 1),
        Gender = gender,
        WorkEmail = "juan@test.com",
        EmploymentType = "FullTime",
        HireDate = new DateOnly(2020, 1, 1),
        IsActive = true
    };

    private static LeaveType MakeLeaveType(string? genderRestriction = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Vacation Leave",
        Code = "VL",
        MaxDaysPerYear = 15,
        IsPaid = true,
        GenderRestriction = genderRestriction
    };

    private static LeaveBalance MakeBalance(Guid employeeId, Guid leaveTypeId, decimal remaining = 10) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeId = employeeId,
        LeaveTypeId = leaveTypeId,
        Year = DateTime.UtcNow.Year,
        TotalDays = remaining,
        UsedDays = 0
    };

    [Fact]
    public async Task CreateAsync_WhenInsufficientBalance_ThrowsDomainException()
    {
        var emp = MakeEmployee();
        var lt = MakeLeaveType();
        var balance = MakeBalance(emp.Id, lt.Id, remaining: 2);
        balance.LeaveType = lt;

        _employeeRepo.Setup(r => r.GetByIdAsync(emp.Id, default)).ReturnsAsync(emp);
        _leaveTypeRepo.Setup(r => r.GetByIdAsync(lt.Id, default)).ReturnsAsync(lt);
        _balanceRepo.Setup(r => r.GetByEmployeeAndTypeAsync(emp.Id, lt.Id, It.IsAny<int>(), default)).ReturnsAsync(balance);
        _holidayService.Setup(h => h.IsHolidayAsync(It.IsAny<DateOnly>(), default)).ReturnsAsync((HolidayType?)null);
        _leaveRepo.Setup(r => r.HasOverlapAsync(emp.Id, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, default)).ReturnsAsync(false);

        // Mon-Fri = 5 working days, but balance only has 2
        var dto = new CreateLeaveRequestDto(emp.Id, lt.Id, new DateOnly(2025, 3, 10), new DateOnly(2025, 3, 14), null);

        var act = () => _sut.CreateAsync(dto);

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("*insufficient leave balance*");
    }

    [Fact]
    public async Task CreateAsync_WhenDatesOverlap_ThrowsDomainException()
    {
        var emp = MakeEmployee();
        var lt = MakeLeaveType();
        var balance = MakeBalance(emp.Id, lt.Id, remaining: 15);
        balance.LeaveType = lt;

        _employeeRepo.Setup(r => r.GetByIdAsync(emp.Id, default)).ReturnsAsync(emp);
        _leaveTypeRepo.Setup(r => r.GetByIdAsync(lt.Id, default)).ReturnsAsync(lt);
        _balanceRepo.Setup(r => r.GetByEmployeeAndTypeAsync(emp.Id, lt.Id, It.IsAny<int>(), default)).ReturnsAsync(balance);
        _holidayService.Setup(h => h.IsHolidayAsync(It.IsAny<DateOnly>(), default)).ReturnsAsync((HolidayType?)null);
        _leaveRepo.Setup(r => r.HasOverlapAsync(emp.Id, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, default)).ReturnsAsync(true);

        var dto = new CreateLeaveRequestDto(emp.Id, lt.Id, new DateOnly(2025, 3, 10), new DateOnly(2025, 3, 12), null);

        var act = () => _sut.CreateAsync(dto);

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("*overlapping leave request*");
    }

    [Fact]
    public async Task CreateAsync_WhenMaternityLeaveAndMaleEmployee_ThrowsDomainException()
    {
        var emp = MakeEmployee(gender: "Male");
        var lt = MakeLeaveType(genderRestriction: "Female");
        var balance = MakeBalance(emp.Id, lt.Id);
        balance.LeaveType = lt;

        _employeeRepo.Setup(r => r.GetByIdAsync(emp.Id, default)).ReturnsAsync(emp);
        _leaveTypeRepo.Setup(r => r.GetByIdAsync(lt.Id, default)).ReturnsAsync(lt);
        _balanceRepo.Setup(r => r.GetByEmployeeAndTypeAsync(emp.Id, lt.Id, It.IsAny<int>(), default)).ReturnsAsync(balance);

        var dto = new CreateLeaveRequestDto(emp.Id, lt.Id, new DateOnly(2025, 3, 10), new DateOnly(2025, 3, 12), null);

        var act = () => _sut.CreateAsync(dto);

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("*not eligible*");
    }

    [Fact]
    public async Task CreateAsync_ExcludesWeekendsFromTotalDays()
    {
        // Mon 2025-03-10 to Fri 2025-03-14 = 5 working days
        var emp = MakeEmployee();
        var lt = MakeLeaveType();
        var balance = MakeBalance(emp.Id, lt.Id, remaining: 15);
        balance.LeaveType = lt;

        _employeeRepo.Setup(r => r.GetByIdAsync(emp.Id, default)).ReturnsAsync(emp);
        _leaveTypeRepo.Setup(r => r.GetByIdAsync(lt.Id, default)).ReturnsAsync(lt);
        _balanceRepo.Setup(r => r.GetByEmployeeAndTypeAsync(emp.Id, lt.Id, It.IsAny<int>(), default)).ReturnsAsync(balance);
        _holidayService.Setup(h => h.IsHolidayAsync(It.IsAny<DateOnly>(), default)).ReturnsAsync((HolidayType?)null);
        _leaveRepo.Setup(r => r.HasOverlapAsync(emp.Id, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, default)).ReturnsAsync(false);
        _leaveRepo.Setup(r => r.AddAsync(It.IsAny<LeaveRequest>(), default))
                  .ReturnsAsync((LeaveRequest l, CancellationToken _) => l);

        var dto = new CreateLeaveRequestDto(emp.Id, lt.Id, new DateOnly(2025, 3, 10), new DateOnly(2025, 3, 14), null);

        var result = await _sut.CreateAsync(dto);

        result.TotalDays.Should().Be(5);
    }
}
