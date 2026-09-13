using FluentAssertions;
using Moq;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Leave.DTOs;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Leave.Services;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Leave;
using M2NET.Core.Enums;
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

    private static Employee MakeEmployee(Gender gender = Gender.Male) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeNumber = "EMP-001",
        FirstName = "Juan",
        LastName = "dela Cruz",
        DateOfBirth = new DateOnly(1990, 1, 1),
        Gender = gender,
        WorkEmail = "juan@test.com",
        EmploymentType = EmploymentType.Regular,
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
    public async Task ApproveAsync_RecordsTheApproverItIsGiven()
    {
        var emp = MakeEmployee();
        var lt = MakeLeaveType();
        var approverId = Guid.NewGuid();
        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(), EmployeeId = emp.Id, LeaveTypeId = lt.Id,
            StartDate = new DateOnly(2026, 10, 5), EndDate = new DateOnly(2026, 10, 6),
            TotalDays = 2, Status = LeaveStatus.Pending
        };
        _leaveRepo.Setup(r => r.GetByIdAsync(request.Id, default)).ReturnsAsync(request);
        _balanceRepo.Setup(r => r.GetByEmployeeAndTypeAsync(emp.Id, lt.Id, 2026, default))
            .ReturnsAsync(MakeBalance(emp.Id, lt.Id));

        var result = await _sut.ApproveAsync(request.Id, approverId);

        result.Status.Should().Be(LeaveStatus.Approved);
        result.ApprovedBy.Should().Be(approverId);
    }

    [Fact]
    public async Task ApproveAsync_OwnRequest_ThrowsDomainException_WithoutSpendingTheBalance()
    {
        // Holding an approver role (Manager, HRManager or Admin) is not a licence to sign off
        // your own leave: somebody else has to.
        var emp = MakeEmployee();
        var lt = MakeLeaveType();
        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(), EmployeeId = emp.Id, LeaveTypeId = lt.Id,
            StartDate = new DateOnly(2026, 10, 5), EndDate = new DateOnly(2026, 10, 6),
            TotalDays = 2, Status = LeaveStatus.Pending
        };
        _leaveRepo.Setup(r => r.GetByIdAsync(request.Id, default)).ReturnsAsync(request);
        _balanceRepo.Setup(r => r.GetByEmployeeAndTypeAsync(emp.Id, lt.Id, 2026, default))
            .ReturnsAsync(MakeBalance(emp.Id, lt.Id));

        var act = () => _sut.ApproveAsync(request.Id, emp.Id);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*your own leave*");
        request.Status.Should().Be(LeaveStatus.Pending);
        request.ApprovedBy.Should().BeNull();
        _balanceRepo.Verify(r => r.UpdateAsync(It.IsAny<LeaveBalance>(), It.IsAny<CancellationToken>()), Times.Never);
        _leaveRepo.Verify(r => r.UpdateAsync(It.IsAny<LeaveRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RejectAsync_OwnRequest_ThrowsDomainException_WithoutRejectingIt()
    {
        // Same rule as approving: an approver does not decide their own leave either way.
        var emp = MakeEmployee();
        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(), EmployeeId = emp.Id, LeaveTypeId = Guid.NewGuid(),
            StartDate = new DateOnly(2026, 10, 5), EndDate = new DateOnly(2026, 10, 6),
            TotalDays = 2, Status = LeaveStatus.Pending
        };
        _leaveRepo.Setup(r => r.GetByIdAsync(request.Id, default)).ReturnsAsync(request);

        var act = () => _sut.RejectAsync(request.Id, emp.Id, new RejectLeaveDto("Changed plans"));

        await act.Should().ThrowAsync<DomainException>().WithMessage("*your own leave*");
        request.Status.Should().Be(LeaveStatus.Pending);
        request.RejectionReason.Should().BeNull();
        _leaveRepo.Verify(r => r.UpdateAsync(It.IsAny<LeaveRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RejectAsync_SomeoneElsesRequest_RecordsTheReason()
    {
        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(), EmployeeId = Guid.NewGuid(), LeaveTypeId = Guid.NewGuid(),
            StartDate = new DateOnly(2026, 10, 5), EndDate = new DateOnly(2026, 10, 6),
            TotalDays = 2, Status = LeaveStatus.Pending
        };
        _leaveRepo.Setup(r => r.GetByIdAsync(request.Id, default)).ReturnsAsync(request);

        var result = await _sut.RejectAsync(request.Id, Guid.NewGuid(), new RejectLeaveDto("Peak season"));

        result.Status.Should().Be(LeaveStatus.Rejected);
        result.RejectionReason.Should().Be("Peak season");
        _leaveRepo.Verify(r => r.UpdateAsync(request, It.IsAny<CancellationToken>()), Times.Once);
    }

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
        var emp = MakeEmployee(gender: Gender.Male);
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
