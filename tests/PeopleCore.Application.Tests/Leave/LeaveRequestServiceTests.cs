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
using PeopleCore.Domain.Interfaces;
using Xunit;

namespace PeopleCore.Application.Tests.Leave;

/// <summary>
/// Filing, approving and cancelling leave under the statutory rules. Days are counted by the real
/// <see cref="LeaveDayCounter"/> over a shift repository with no assignments, so non-calendar types
/// fall back to Monday-Friday, and no date is a holiday unless a test says so.
/// </summary>
public class LeaveRequestServiceTests
{
    private readonly Mock<ILeaveRequestRepository> _leaveRepo = new();
    private readonly Mock<ILeaveBalanceRepository> _balanceRepo = new();
    private readonly Mock<IHolidayService> _holidayService = new();
    private readonly Mock<IShiftAssignmentRepository> _shiftRepo = new();
    private readonly Mock<IEmployeeRepository> _employeeRepo = new();
    private readonly Mock<ILeaveTypeRepository> _leaveTypeRepo = new();
    private readonly LeaveRequestService _sut;

    public LeaveRequestServiceTests()
    {
        _shiftRepo.Setup(r => r.GetActiveForPeriodAsync(
                      It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync([]);
        _holidayService.Setup(h => h.IsHolidayAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync((HolidayType?)null);
        _leaveRepo.Setup(r => r.GetPendingAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync([]);
        _leaveRepo.Setup(r => r.AddAsync(It.IsAny<LeaveRequest>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync((LeaveRequest l, CancellationToken _) => l);
        _balanceRepo.Setup(r => r.AddAsync(It.IsAny<LeaveBalance>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((LeaveBalance b, CancellationToken _) => b);

        var counter = new LeaveDayCounter(_shiftRepo.Object, _holidayService.Object);
        _sut = new LeaveRequestService(_leaveRepo.Object, _balanceRepo.Object, counter, _employeeRepo.Object, _leaveTypeRepo.Object);
    }

    private static Employee MakeEmployee(
        Gender gender = Gender.Male,
        CivilStatus civilStatus = CivilStatus.Single,
        DateOnly? hireDate = null) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeNumber = "EMP-001",
        FirstName = "Juan",
        LastName = "dela Cruz",
        DateOfBirth = new DateOnly(1990, 1, 1),
        Gender = gender,
        CivilStatus = civilStatus,
        WorkEmail = "juan@test.com",
        EmploymentType = EmploymentType.Regular,
        HireDate = hireDate ?? new DateOnly(2020, 1, 1),
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

    private static LeaveType Paternity() => new()
    {
        Id = Guid.NewGuid(), Name = "Paternity Leave", Code = "PL",
        EntitlementKind = LeaveEntitlementKind.PerEvent, DaysPerEvent = 7, MaxEvents = 4,
        GenderRestriction = "Male", RequiresMarried = true, RequiresDocument = true
    };

    private static LeaveType Maternity() => new()
    {
        Id = Guid.NewGuid(), Name = "Maternity Leave", Code = "ML",
        EntitlementKind = LeaveEntitlementKind.PerEvent, DaysPerEvent = 105, CountsCalendarDays = true,
        IsMaternity = true, GenderRestriction = "Female", RequiresDocument = true
    };

    private static LeaveType SoloParent() => new()
    {
        Id = Guid.NewGuid(), Name = "Solo Parent Leave", Code = "SPL",
        EntitlementKind = LeaveEntitlementKind.YearlyAllowance, MaxDaysPerYear = 7,
        RequiresSoloParentId = true, MinServiceMonths = 6
    };

    private void Known(Employee emp, LeaveType lt)
    {
        _employeeRepo.Setup(r => r.GetByIdAsync(emp.Id, It.IsAny<CancellationToken>())).ReturnsAsync(emp);
        _leaveTypeRepo.Setup(r => r.GetByIdAsync(lt.Id, It.IsAny<CancellationToken>())).ReturnsAsync(lt);
    }

    private LeaveBalance HasBalance(Employee emp, LeaveType lt, int year, decimal total, decimal used = 0)
    {
        var balance = new LeaveBalance
        {
            EmployeeId = emp.Id, LeaveTypeId = lt.Id, Year = year, TotalDays = total, UsedDays = used
        };
        _balanceRepo.Setup(r => r.GetByEmployeeAndTypeAsync(emp.Id, lt.Id, year, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(balance);
        return balance;
    }

    private LeaveRequest Stored(Employee emp, LeaveType lt, DateOnly start, DateOnly end,
        LeaveStatus status = LeaveStatus.Pending, decimal totalDays = 0, decimal daysInStartYear = 0)
    {
        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(), EmployeeId = emp.Id, LeaveTypeId = lt.Id,
            StartDate = start, EndDate = end, TotalDays = totalDays, DaysInStartYear = daysInStartYear,
            Status = status
        };
        _leaveRepo.Setup(r => r.GetByIdAsync(request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(request);
        return request;
    }

    // ── Existing behaviour ───────────────────────────────────────────────────

    [Fact]
    public async Task ApproveAsync_RecordsTheApproverItIsGiven()
    {
        var emp = MakeEmployee();
        var lt = MakeLeaveType();
        Known(emp, lt);
        var approverId = Guid.NewGuid();
        var request = Stored(emp, lt, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 6), totalDays: 2, daysInStartYear: 2);
        HasBalance(emp, lt, 2026, 10);

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
        Known(emp, lt);
        var request = Stored(emp, lt, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 6), totalDays: 2, daysInStartYear: 2);
        HasBalance(emp, lt, 2026, 10);

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
        Known(emp, lt);
        HasBalance(emp, lt, 2025, total: 2);

        // Mon-Fri = 5 working days, but balance only has 2
        var dto = new CreateLeaveRequestDto(emp.Id, lt.Id, new DateOnly(2025, 3, 10), new DateOnly(2025, 3, 14), null);

        var act = () => _sut.CreateAsync(dto);

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("You have 2 days of Vacation Leave left for 2025.");
    }

    [Fact]
    public async Task CreateAsync_WhenDatesOverlap_ThrowsDomainException()
    {
        var emp = MakeEmployee();
        var lt = MakeLeaveType();
        Known(emp, lt);
        HasBalance(emp, lt, 2025, total: 15);
        _leaveRepo.Setup(r => r.HasOverlapAsync(emp.Id, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(true);

        var dto = new CreateLeaveRequestDto(emp.Id, lt.Id, new DateOnly(2025, 3, 10), new DateOnly(2025, 3, 12), null);

        var act = () => _sut.CreateAsync(dto);

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("Employee has an overlapping leave request for these dates.");
    }

    [Fact]
    public async Task CreateAsync_WhenMaternityLeaveAndMaleEmployee_ThrowsDomainException()
    {
        var emp = MakeEmployee(gender: Gender.Male);
        var lt = MakeLeaveType(genderRestriction: "Female");
        Known(emp, lt);
        HasBalance(emp, lt, 2025, total: 10);

        var dto = new CreateLeaveRequestDto(emp.Id, lt.Id, new DateOnly(2025, 3, 10), new DateOnly(2025, 3, 12), null);

        var act = () => _sut.CreateAsync(dto);

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("Employee is not eligible for this leave type.");
    }

    [Fact]
    public async Task CreateAsync_ExcludesWeekendsFromTotalDays()
    {
        // Mon 2025-03-10 to Sun 2025-03-16 = 5 working days (no shift assignment: Monday-Friday)
        var emp = MakeEmployee();
        var lt = MakeLeaveType();
        Known(emp, lt);
        HasBalance(emp, lt, 2025, total: 15);

        var dto = new CreateLeaveRequestDto(emp.Id, lt.Id, new DateOnly(2025, 3, 10), new DateOnly(2025, 3, 16), null);

        var result = await _sut.CreateAsync(dto);

        result.TotalDays.Should().Be(5);
    }

    // ── Each check surfaced through Create ───────────────────────────────────

    [Fact]
    public async Task CreateAsync_InactiveType_IsRefused()
    {
        var emp = MakeEmployee();
        var lt = MakeLeaveType();
        lt.IsActive = false;
        Known(emp, lt);
        HasBalance(emp, lt, 2025, total: 15);

        var act = () => _sut.CreateAsync(new CreateLeaveRequestDto(emp.Id, lt.Id, new DateOnly(2025, 3, 10), new DateOnly(2025, 3, 10), null));

        await act.Should().ThrowAsync<DomainException>().WithMessage("Vacation Leave is no longer available.");
        _leaveRepo.Verify(r => r.AddAsync(It.IsAny<LeaveRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_NotEnoughService_IsRefused()
    {
        var emp = MakeEmployee(hireDate: new DateOnly(2025, 1, 15));
        var lt = SoloParent();
        lt.RequiresSoloParentId = false;
        Known(emp, lt);

        var act = () => _sut.CreateAsync(new CreateLeaveRequestDto(emp.Id, lt.Id, new DateOnly(2025, 3, 10), new DateOnly(2025, 3, 10), null));

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("Solo Parent Leave needs 6 months of service; you'll qualify on Jul 15, 2025.");
    }

    [Fact]
    public async Task CreateAsync_NotMarried_IsRefused()
    {
        var emp = MakeEmployee(civilStatus: CivilStatus.Single);
        var lt = Paternity();
        Known(emp, lt);

        var act = () => _sut.CreateAsync(new CreateLeaveRequestDto(emp.Id, lt.Id, new DateOnly(2025, 3, 10), new DateOnly(2025, 3, 10), null));

        await act.Should().ThrowAsync<DomainException>().WithMessage("Paternity Leave is for married employees.");
    }

    [Fact]
    public async Task CreateAsync_NoSoloParentId_IsRefused()
    {
        var emp = MakeEmployee();
        var lt = SoloParent();
        Known(emp, lt);

        var act = () => _sut.CreateAsync(new CreateLeaveRequestDto(emp.Id, lt.Id, new DateOnly(2025, 3, 10), new DateOnly(2025, 3, 10), null));

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("Solo Parent Leave needs a valid solo parent ID on your record; ask HR to add it.");
    }

    [Fact]
    public async Task CreateAsync_MaternityCaseMissing_IsRefused()
    {
        var emp = MakeEmployee(gender: Gender.Female);
        var lt = Maternity();
        Known(emp, lt);

        var act = () => _sut.CreateAsync(new CreateLeaveRequestDto(emp.Id, lt.Id, new DateOnly(2025, 3, 1), new DateOnly(2025, 3, 10), null));

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("Choose whether this is a live birth or a miscarriage or emergency termination.");
    }

    [Fact]
    public async Task CreateAsync_FatherAllocationOverSeven_IsRefused()
    {
        var emp = MakeEmployee(gender: Gender.Female);
        var lt = Maternity();
        Known(emp, lt);

        var act = () => _sut.CreateAsync(new CreateLeaveRequestDto(emp.Id, lt.Id, new DateOnly(2025, 3, 1), new DateOnly(2025, 3, 10), null,
            MaternityCase.LiveBirth, DaysAllocatedToFather: 8));

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("Up to 7 days can be allocated to the father, for a live birth only.");
    }

    [Fact]
    public async Task CreateAsync_SpanningThreeYears_IsRefused()
    {
        var emp = MakeEmployee();
        var lt = MakeLeaveType();
        Known(emp, lt);

        var act = () => _sut.CreateAsync(new CreateLeaveRequestDto(emp.Id, lt.Id, new DateOnly(2025, 12, 29), new DateOnly(2027, 1, 4), null));

        await act.Should().ThrowAsync<DomainException>().WithMessage("A leave request can't span more than two years.");
    }

    [Fact]
    public async Task CreateAsync_OnlyWeekendDays_IsRefused()
    {
        var emp = MakeEmployee();
        var lt = MakeLeaveType();
        Known(emp, lt);
        HasBalance(emp, lt, 2025, total: 15);

        // Sat 2025-03-15 to Sun 2025-03-16
        var act = () => _sut.CreateAsync(new CreateLeaveRequestDto(emp.Id, lt.Id, new DateOnly(2025, 3, 15), new DateOnly(2025, 3, 16), null));

        await act.Should().ThrowAsync<DomainException>().WithMessage("There are no working days in that range.");
    }

    [Fact]
    public async Task CreateAsync_OverThePerEventLimit_IsRefused()
    {
        var emp = MakeEmployee(civilStatus: CivilStatus.Married);
        var lt = Paternity();
        Known(emp, lt);

        // Mon 2025-03-10 to Wed 2025-03-19 = 8 working days
        var act = () => _sut.CreateAsync(new CreateLeaveRequestDto(emp.Id, lt.Id, new DateOnly(2025, 3, 10), new DateOnly(2025, 3, 19), null));

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("Paternity Leave is up to 7 days each time; this request is 8.");
    }

    // ── Pending holds ────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_PendingRequestsHoldTheirDays()
    {
        var emp = MakeEmployee();
        var lt = MakeLeaveType();
        Known(emp, lt);
        HasBalance(emp, lt, 2025, total: 5);
        var pending = new LeaveRequest
        {
            EmployeeId = emp.Id, LeaveTypeId = lt.Id, Status = LeaveStatus.Pending,
            StartDate = new DateOnly(2025, 3, 3), EndDate = new DateOnly(2025, 3, 5), TotalDays = 3, DaysInStartYear = 3
        };
        _leaveRepo.Setup(r => r.GetPendingAsync(emp.Id, lt.Id, null, It.IsAny<CancellationToken>())).ReturnsAsync([pending]);

        // Mon-Wed = 3 days, but 5 - 3 held = 2 available
        var act = () => _sut.CreateAsync(new CreateLeaveRequestDto(emp.Id, lt.Id, new DateOnly(2025, 3, 10), new DateOnly(2025, 3, 12), null));

        await act.Should().ThrowAsync<DomainException>().WithMessage("You have 2 days of Vacation Leave left for 2025.");
    }

    [Fact]
    public async Task ApproveAsync_ExcludesTheRequestItselfFromPendingHolds()
    {
        var emp = MakeEmployee();
        var lt = MakeLeaveType();
        Known(emp, lt);
        HasBalance(emp, lt, 2025, total: 3);
        var request = Stored(emp, lt, new DateOnly(2025, 3, 10), new DateOnly(2025, 3, 12), totalDays: 3, daysInStartYear: 3);
        // Only the request itself is pending - asked for with excludeId = its own id, which returns nothing.
        _leaveRepo.Setup(r => r.GetPendingAsync(emp.Id, lt.Id, null, It.IsAny<CancellationToken>())).ReturnsAsync([request]);

        var result = await _sut.ApproveAsync(request.Id, Guid.NewGuid());

        result.Status.Should().Be(LeaveStatus.Approved);
        _leaveRepo.Verify(r => r.GetPendingAsync(emp.Id, lt.Id, request.Id, It.IsAny<CancellationToken>()), Times.Once);
        _leaveRepo.Verify(r => r.HasOverlapAsync(emp.Id, request.StartDate, request.EndDate, request.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Yearly allowance ─────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_YearlyAllowance_CreatesTheYearsBalanceOnFirstFiling()
    {
        var emp = MakeEmployee();
        emp.SoloParentIdNumber = "SP-1";
        emp.SoloParentIdValidUntil = new DateOnly(2030, 1, 1);
        var lt = SoloParent();
        Known(emp, lt);

        var result = await _sut.CreateAsync(new CreateLeaveRequestDto(emp.Id, lt.Id, new DateOnly(2025, 3, 10), new DateOnly(2025, 3, 12), null));

        result.TotalDays.Should().Be(3);
        _balanceRepo.Verify(r => r.AddAsync(It.Is<LeaveBalance>(b =>
            b.EmployeeId == emp.Id && b.LeaveTypeId == lt.Id && b.Year == 2025 && b.TotalDays == 7 && b.UsedDays == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_YearlyAllowance_WithNoBalance_CountsTheFullAllowance()
    {
        var emp = MakeEmployee();
        emp.SoloParentIdNumber = "SP-1";
        emp.SoloParentIdValidUntil = new DateOnly(2030, 1, 1);
        var lt = SoloParent();
        Known(emp, lt);

        // Mon 2025-03-10 to Wed 2025-03-19 = 8 working days, one more than the allowance of 7
        var act = () => _sut.CreateAsync(new CreateLeaveRequestDto(emp.Id, lt.Id, new DateOnly(2025, 3, 10), new DateOnly(2025, 3, 19), null));

        await act.Should().ThrowAsync<DomainException>().WithMessage("You have 7 days of Solo Parent Leave left for 2025.");
        _balanceRepo.Verify(r => r.AddAsync(It.IsAny<LeaveBalance>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_YearlyAllowance_WithAnExistingBalance_AddsNoRow()
    {
        var emp = MakeEmployee();
        emp.SoloParentIdNumber = "SP-1";
        emp.SoloParentIdValidUntil = new DateOnly(2030, 1, 1);
        var lt = SoloParent();
        Known(emp, lt);
        HasBalance(emp, lt, 2025, total: 7, used: 2);

        await _sut.CreateAsync(new CreateLeaveRequestDto(emp.Id, lt.Id, new DateOnly(2025, 3, 10), new DateOnly(2025, 3, 12), null));

        _balanceRepo.Verify(r => r.AddAsync(It.IsAny<LeaveBalance>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Per event ────────────────────────────────────────────────────────────

    [Fact]
    public async Task PerEvent_FilingAndApproving_TouchesNoBalance()
    {
        var emp = MakeEmployee(civilStatus: CivilStatus.Married);
        var lt = Paternity();
        Known(emp, lt);

        var created = await _sut.CreateAsync(new CreateLeaveRequestDto(emp.Id, lt.Id, new DateOnly(2025, 3, 10), new DateOnly(2025, 3, 18), null));
        created.TotalDays.Should().Be(7);

        var request = Stored(emp, lt, new DateOnly(2025, 3, 10), new DateOnly(2025, 3, 18), totalDays: 7, daysInStartYear: 7);
        request.DocumentStorageKey = "leave-requests/x/y.pdf";
        var approved = await _sut.ApproveAsync(request.Id, Guid.NewGuid());

        approved.Status.Should().Be(LeaveStatus.Approved);
        _balanceRepo.Verify(r => r.GetByEmployeeAndTypeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _balanceRepo.Verify(r => r.AddAsync(It.IsAny<LeaveBalance>(), It.IsAny<CancellationToken>()), Times.Never);
        _balanceRepo.Verify(r => r.UpdateAsync(It.IsAny<LeaveBalance>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PerEvent_FourthApprovedEvent_BlocksTheFifth()
    {
        var emp = MakeEmployee(civilStatus: CivilStatus.Married);
        var lt = Paternity();
        Known(emp, lt);
        _leaveRepo.Setup(r => r.CountApprovedAsync(emp.Id, lt.Id, It.IsAny<CancellationToken>())).ReturnsAsync(4);

        var act = () => _sut.CreateAsync(new CreateLeaveRequestDto(emp.Id, lt.Id, new DateOnly(2025, 3, 10), new DateOnly(2025, 3, 12), null));

        await act.Should().ThrowAsync<DomainException>().WithMessage("You've used Paternity Leave 4 times, the most allowed.");
    }

    [Fact]
    public async Task PerEvent_ThirdApprovedEvent_StillAllowsTheFourth()
    {
        var emp = MakeEmployee(civilStatus: CivilStatus.Married);
        var lt = Paternity();
        Known(emp, lt);
        _leaveRepo.Setup(r => r.CountApprovedAsync(emp.Id, lt.Id, It.IsAny<CancellationToken>())).ReturnsAsync(3);

        var result = await _sut.CreateAsync(new CreateLeaveRequestDto(emp.Id, lt.Id, new DateOnly(2025, 3, 10), new DateOnly(2025, 3, 12), null));

        result.Status.Should().Be(LeaveStatus.Pending);
    }

    // ── Two years ────────────────────────────────────────────────────────────

    [Fact]
    public async Task TwoYearAccrued_FilingSplitsTheDays_ApprovalChargesBothYears_CancelRefundsBoth()
    {
        var emp = MakeEmployee();
        var lt = MakeLeaveType();
        Known(emp, lt);
        var balance2025 = HasBalance(emp, lt, 2025, total: 10);
        var balance2026 = HasBalance(emp, lt, 2026, total: 10);
        LeaveRequest? saved = null;
        _leaveRepo.Setup(r => r.AddAsync(It.IsAny<LeaveRequest>(), It.IsAny<CancellationToken>()))
                  .Callback((LeaveRequest l, CancellationToken _) => saved = l)
                  .ReturnsAsync((LeaveRequest l, CancellationToken _) => l);

        // Mon 2025-12-29 .. Wed 2025-12-31 = 3 days; Thu 2026-01-01 .. Fri 2026-01-02 = 2 days
        await _sut.CreateAsync(new CreateLeaveRequestDto(emp.Id, lt.Id, new DateOnly(2025, 12, 29), new DateOnly(2026, 1, 2), null));

        saved!.TotalDays.Should().Be(5);
        saved.DaysInStartYear.Should().Be(3);

        _leaveRepo.Setup(r => r.GetByIdAsync(saved.Id, It.IsAny<CancellationToken>())).ReturnsAsync(saved);
        await _sut.ApproveAsync(saved.Id, Guid.NewGuid());

        balance2025.UsedDays.Should().Be(3);
        balance2026.UsedDays.Should().Be(2);

        await _sut.CancelAsync(saved.Id, emp.Id);

        saved.Status.Should().Be(LeaveStatus.Cancelled);
        balance2025.UsedDays.Should().Be(0);
        balance2026.UsedDays.Should().Be(0);
    }

    [Fact]
    public async Task ApproveAsync_SetsTotalDaysAndDaysInStartYearFromTheRecount()
    {
        var emp = MakeEmployee();
        var lt = MakeLeaveType();
        Known(emp, lt);
        HasBalance(emp, lt, 2025, total: 10);
        var request = Stored(emp, lt, new DateOnly(2025, 3, 10), new DateOnly(2025, 3, 11), totalDays: 9, daysInStartYear: 9);

        var result = await _sut.ApproveAsync(request.Id, Guid.NewGuid());

        result.TotalDays.Should().Be(2);
        request.DaysInStartYear.Should().Be(2);
    }

    [Fact]
    public async Task ApproveAsync_AccruedYearWithNoBalanceRow_IsRefused()
    {
        var emp = MakeEmployee();
        var lt = MakeLeaveType();
        Known(emp, lt);
        var request = Stored(emp, lt, new DateOnly(2025, 3, 10), new DateOnly(2025, 3, 11), totalDays: 2, daysInStartYear: 2);

        var act = () => _sut.ApproveAsync(request.Id, Guid.NewGuid());

        await act.Should().ThrowAsync<DomainException>().WithMessage("You have 0 days of Vacation Leave left for 2025.");
        request.Status.Should().Be(LeaveStatus.Pending);
    }

    // ── Approval re-checks ───────────────────────────────────────────────────

    [Fact]
    public async Task ApproveAsync_TypeDeactivatedAfterFiling_IsRefused()
    {
        var emp = MakeEmployee();
        var lt = MakeLeaveType();
        Known(emp, lt);
        var balance = HasBalance(emp, lt, 2025, total: 10);
        var request = Stored(emp, lt, new DateOnly(2025, 3, 10), new DateOnly(2025, 3, 11), totalDays: 2, daysInStartYear: 2);
        lt.IsActive = false;

        var act = () => _sut.ApproveAsync(request.Id, Guid.NewGuid());

        await act.Should().ThrowAsync<DomainException>().WithMessage("Vacation Leave is no longer available.");
        request.Status.Should().Be(LeaveStatus.Pending);
        balance.UsedDays.Should().Be(0);
    }

    // ── Maternity ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Maternity_WithASoloParentId_Allows120CalendarDays()
    {
        var emp = MakeEmployee(gender: Gender.Female);
        emp.SoloParentIdNumber = "SP-9";
        emp.SoloParentIdValidUntil = new DateOnly(2030, 1, 1);
        var lt = Maternity();
        Known(emp, lt);
        var start = new DateOnly(2025, 3, 1);

        var result = await _sut.CreateAsync(new CreateLeaveRequestDto(emp.Id, lt.Id, start, start.AddDays(119), null, MaternityCase.LiveBirth));

        result.TotalDays.Should().Be(120);
        result.MaternityCase.Should().Be(MaternityCase.LiveBirth);
    }

    [Fact]
    public async Task Maternity_WithoutASoloParentId_120DaysIsRefused()
    {
        var emp = MakeEmployee(gender: Gender.Female);
        var lt = Maternity();
        Known(emp, lt);
        var start = new DateOnly(2025, 3, 1);

        var act = () => _sut.CreateAsync(new CreateLeaveRequestDto(emp.Id, lt.Id, start, start.AddDays(119), null, MaternityCase.LiveBirth));

        await act.Should().ThrowAsync<DomainException>()
                 .WithMessage("Maternity leave for a live birth is up to 105 days; this request is 120.");
    }

    [Fact]
    public async Task CreateAsync_NonMaternityType_ClearsMaternityFields()
    {
        var emp = MakeEmployee();
        var lt = MakeLeaveType();
        Known(emp, lt);
        HasBalance(emp, lt, 2025, total: 10);

        var result = await _sut.CreateAsync(new CreateLeaveRequestDto(emp.Id, lt.Id, new DateOnly(2025, 3, 10), new DateOnly(2025, 3, 10), null,
            MaternityCase.LiveBirth, DaysAllocatedToFather: 5));

        result.MaternityCase.Should().BeNull();
        result.DaysAllocatedToFather.Should().Be(0);
    }

    // ── Documents ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApproveAsync_DocumentRequired_WithoutADocument_IsRefused_AndWithOne_Approves()
    {
        var emp = MakeEmployee(civilStatus: CivilStatus.Married);
        var lt = Paternity();
        Known(emp, lt);
        var request = Stored(emp, lt, new DateOnly(2025, 3, 10), new DateOnly(2025, 3, 12), totalDays: 3, daysInStartYear: 3);

        var act = () => _sut.ApproveAsync(request.Id, Guid.NewGuid());

        await act.Should().ThrowAsync<DomainException>().WithMessage("Paternity Leave needs a supporting document.");
        request.Status.Should().Be(LeaveStatus.Pending);

        request.DocumentStorageKey = "leave-requests/r/d.pdf";
        request.DocumentFileName = "marriage-certificate.pdf";
        var result = await _sut.ApproveAsync(request.Id, Guid.NewGuid());

        result.Status.Should().Be(LeaveStatus.Approved);
        result.HasDocument.Should().BeTrue();
        result.DocumentFileName.Should().Be("marriage-certificate.pdf");
    }

    [Fact]
    public async Task GetByIdAsync_MapsTheNewFields()
    {
        var emp = MakeEmployee(gender: Gender.Female);
        var lt = Maternity();
        var request = Stored(emp, lt, new DateOnly(2025, 3, 1), new DateOnly(2025, 6, 13), totalDays: 105, daysInStartYear: 105);
        request.MaternityCase = MaternityCase.LiveBirth;
        request.DaysAllocatedToFather = 7;

        var result = await _sut.GetByIdAsync(request.Id);

        result.MaternityCase.Should().Be(MaternityCase.LiveBirth);
        result.DaysAllocatedToFather.Should().Be(7);
        result.HasDocument.Should().BeFalse();
        result.DocumentFileName.Should().BeNull();
    }
}
