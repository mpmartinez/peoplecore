using FluentAssertions;
using Moq;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Leave.Services;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Interfaces;
using Xunit;

namespace PeopleCore.Application.Tests.Leave;

public class LeaveAccrualServiceTests
{
    private readonly Mock<ILeaveAccrualRepository> _accrualRepo = new();
    private readonly Mock<ILeaveBalanceRepository> _balanceRepo = new();
    private readonly Mock<IEmployeeRepository> _employeeRepo = new();
    private readonly LeaveAccrualService _sut;

    public LeaveAccrualServiceTests()
    {
        _sut = new LeaveAccrualService(_accrualRepo.Object, _balanceRepo.Object, _employeeRepo.Object);
    }

    private static Employee MakeEmployee(DateOnly hireDate, DateOnly? separationDate = null) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeNumber = "EMP-001",
        FirstName = "Juan",
        LastName = "dela Cruz",
        DateOfBirth = new DateOnly(1990, 1, 1),
        HireDate = hireDate,
        SeparationDate = separationDate,
        IsActive = separationDate == null,
        WorkEmail = "juan@test.com"
    };

    private static LeaveAccrualPolicy MakePolicy(
        Guid leaveTypeId,
        int tenureMin,
        int? tenureMax,
        decimal daysPerYear,
        bool isActive = true) => new()
    {
        LeaveTypeId = leaveTypeId,
        TenureMonthsMin = tenureMin,
        TenureMonthsMax = tenureMax,
        DaysPerYear = daysPerYear,
        IsActive = isActive
    };

    // Test 1: When employee tenure matches a policy band, a transaction is created with
    // the correct pro-rated days (DaysPerYear / 12).
    [Fact]
    public async Task RunAccruals_MatchingPolicy_CreatesTransactionWithCorrectDays()
    {
        // Arrange
        var leaveTypeId = Guid.NewGuid();

        // HireDate = 2025-01-01 → tenure in March 2026 = 14 months → matches 12-23 band
        var employee = MakeEmployee(new DateOnly(2025, 1, 1));
        var employeeId = employee.Id;

        var policy = MakePolicy(leaveTypeId, tenureMin: 12, tenureMax: 23, daysPerYear: 15m);

        _employeeRepo
            .Setup(r => r.GetAllAsync(default))
            .ReturnsAsync(new List<Employee> { employee }.AsReadOnly());

        _accrualRepo
            .Setup(r => r.GetAllActivePoliciesAsync(default))
            .ReturnsAsync(new List<LeaveAccrualPolicy> { policy }.AsReadOnly());

        _accrualRepo
            .Setup(r => r.TransactionExistsAsync(employeeId, leaveTypeId, 2026, 3, default))
            .ReturnsAsync(false);

        // Act
        await _sut.RunAccrualsAsync(2026, 3);

        // Assert: transaction added with DaysAccrued = 15 / 12
        _accrualRepo.Verify(
            r => r.AddTransactionAsync(
                It.Is<LeaveAccrualTransaction>(t =>
                    t.EmployeeId == employeeId &&
                    t.LeaveTypeId == leaveTypeId &&
                    t.DaysAccrued == 15m / 12m),
                default),
            Times.Once);
    }

    // Test 2: When a transaction already exists for the same employee/type/period,
    // the service must skip it (idempotency guard).
    [Fact]
    public async Task RunAccruals_TransactionAlreadyExists_SkipsEmployee()
    {
        // Arrange
        var leaveTypeId = Guid.NewGuid();

        var employee = MakeEmployee(new DateOnly(2025, 1, 1));
        var employeeId = employee.Id;

        var policy = MakePolicy(leaveTypeId, tenureMin: 12, tenureMax: 23, daysPerYear: 15m);

        _employeeRepo
            .Setup(r => r.GetAllAsync(default))
            .ReturnsAsync(new List<Employee> { employee }.AsReadOnly());

        _accrualRepo
            .Setup(r => r.GetAllActivePoliciesAsync(default))
            .ReturnsAsync(new List<LeaveAccrualPolicy> { policy }.AsReadOnly());

        // Transaction already recorded — service must not add a duplicate
        _accrualRepo
            .Setup(r => r.TransactionExistsAsync(employeeId, leaveTypeId, 2026, 3, default))
            .ReturnsAsync(true);

        // Act
        await _sut.RunAccrualsAsync(2026, 3);

        // Assert
        _accrualRepo.Verify(
            r => r.AddTransactionAsync(It.IsAny<LeaveAccrualTransaction>(), default),
            Times.Never);
    }

    // Test 3: When the matching policy has DaysPerYear = 0 (probationary band),
    // no transaction should be created — zero-accrual means no record.
    [Fact]
    public async Task RunAccruals_ZeroDaysPolicy_NoTransactionCreated()
    {
        // Arrange
        var leaveTypeId = Guid.NewGuid();

        // HireDate = 2026-02-01 → tenure in March 2026 = 1 month → matches 0-11 band
        var employee = MakeEmployee(new DateOnly(2026, 2, 1));
        var employeeId = employee.Id;

        var policy = MakePolicy(leaveTypeId, tenureMin: 0, tenureMax: 11, daysPerYear: 0m);

        _employeeRepo
            .Setup(r => r.GetAllAsync(default))
            .ReturnsAsync(new List<Employee> { employee }.AsReadOnly());

        _accrualRepo
            .Setup(r => r.GetAllActivePoliciesAsync(default))
            .ReturnsAsync(new List<LeaveAccrualPolicy> { policy }.AsReadOnly());

        _accrualRepo
            .Setup(r => r.TransactionExistsAsync(employeeId, leaveTypeId, 2026, 3, default))
            .ReturnsAsync(false);

        // Act
        await _sut.RunAccrualsAsync(2026, 3);

        // Assert: zero-day accruals produce no transaction
        _accrualRepo.Verify(
            r => r.AddTransactionAsync(It.IsAny<LeaveAccrualTransaction>(), default),
            Times.Never);
    }
}
