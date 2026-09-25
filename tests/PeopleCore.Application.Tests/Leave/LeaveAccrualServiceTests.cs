using FluentAssertions;
using M2NET.Core.Enums;
using Moq;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Leave.Services;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Enums;
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

    private static Employee MakeEmployee(DateOnly hireDate, DateOnly? separationDate = null, Gender gender = Gender.Male) => new()
    {
        Gender = gender,
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
        bool isActive = true,
        LeaveType? type = null) => new()
    {
        LeaveTypeId = leaveTypeId,
        // The repository loads each policy with its type; an ordinary type is active and accrued.
        LeaveType = type ?? new LeaveType { Id = leaveTypeId, Name = "Vacation Leave", Code = "VL" },
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

    // ---- which types accrue ----------------------------------------------------------------

    /// <summary>One employee 14 months in, and one policy for the given type; returns the employee.</summary>
    private Employee OnePolicyFor(LeaveType type, Gender gender = Gender.Male)
    {
        var employee = MakeEmployee(new DateOnly(2025, 1, 1), gender: gender);
        _employeeRepo.Setup(r => r.GetAllAsync(default))
            .ReturnsAsync(new List<Employee> { employee }.AsReadOnly());
        _accrualRepo.Setup(r => r.GetAllActivePoliciesAsync(default))
            .ReturnsAsync(new List<LeaveAccrualPolicy> { MakePolicy(type.Id, 0, null, 12m, type: type) }.AsReadOnly());
        _accrualRepo.Setup(r => r.TransactionExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), 2026, 3, default))
            .ReturnsAsync(false);
        return employee;
    }

    private void VerifyNothingAccrued()
    {
        _accrualRepo.Verify(r => r.AddTransactionAsync(It.IsAny<LeaveAccrualTransaction>(), default), Times.Never);
        _balanceRepo.Verify(r => r.AddAsync(It.IsAny<LeaveBalance>(), It.IsAny<CancellationToken>()), Times.Never);
        _balanceRepo.Verify(r => r.UpdateAsync(It.IsAny<LeaveBalance>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAccruals_SkipsAYearlyAllowanceType()
    {
        // A yearly allowance's balance is created when the employee first files in the year;
        // accruing on top of it would grant the allowance twice.
        OnePolicyFor(new LeaveType
        {
            Name = "Solo Parent Leave", Code = "SPL", MaxDaysPerYear = 7m,
            EntitlementKind = LeaveEntitlementKind.YearlyAllowance,
        });

        await _sut.RunAccrualsAsync(2026, 3);

        VerifyNothingAccrued();
    }

    [Fact]
    public async Task RunAccruals_SkipsAPerEventType()
    {
        OnePolicyFor(new LeaveType
        {
            Name = "Paternity Leave", Code = "PL", DaysPerEvent = 7m,
            EntitlementKind = LeaveEntitlementKind.PerEvent,
        });

        await _sut.RunAccrualsAsync(2026, 3);

        VerifyNothingAccrued();
    }

    [Fact]
    public async Task RunAccruals_SkipsAnInactiveType()
    {
        OnePolicyFor(new LeaveType { Name = "Vacation Leave", Code = "VL", MaxDaysPerYear = 15m, IsActive = false });

        await _sut.RunAccrualsAsync(2026, 3);

        VerifyNothingAccrued();
    }

    [Fact]
    public async Task RunAccruals_SkipsAFemaleType_ForAMaleEmployee()
    {
        OnePolicyFor(new LeaveType { Name = "Women's Leave", Code = "WL", MaxDaysPerYear = 12m, GenderRestriction = "Female" },
            Gender.Male);

        await _sut.RunAccrualsAsync(2026, 3);

        VerifyNothingAccrued();
    }

    [Fact]
    public async Task RunAccruals_AccruesAFemaleType_ForAFemaleEmployee()
    {
        var type = new LeaveType { Name = "Women's Leave", Code = "WL", MaxDaysPerYear = 12m, GenderRestriction = "Female" };
        var employee = OnePolicyFor(type, Gender.Female);

        await _sut.RunAccrualsAsync(2026, 3);

        _accrualRepo.Verify(r => r.AddTransactionAsync(
            It.Is<LeaveAccrualTransaction>(t => t.EmployeeId == employee.Id && t.LeaveTypeId == type.Id && t.DaysAccrued == 1m),
            default), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RunAccruals_TreatsABlankGenderRestriction_AsAnyGender(string blank)
    {
        // Saving a type stores a blank restriction as null; a row saved before that still accrues.
        var type = new LeaveType { Name = "Vacation Leave", Code = "VL", MaxDaysPerYear = 12m, GenderRestriction = blank };
        var employee = OnePolicyFor(type, Gender.Male);

        await _sut.RunAccrualsAsync(2026, 3);

        _accrualRepo.Verify(r => r.AddTransactionAsync(
            It.Is<LeaveAccrualTransaction>(t => t.EmployeeId == employee.Id && t.LeaveTypeId == type.Id),
            default), Times.Once);
    }

    // ---- monthly amounts -------------------------------------------------------------------

    /// <summary>Runs all twelve months of 2026 for one employee well past the policy's minimum tenure; returns each month's amount.</summary>
    private async Task<List<decimal>> AYearOfMonthlyAccrual(decimal daysPerYear)
    {
        var type = new LeaveType { Name = "Service Incentive Leave", Code = "SIL", MaxDaysPerYear = daysPerYear };
        var employee = MakeEmployee(new DateOnly(2020, 1, 1));
        _employeeRepo.Setup(r => r.GetAllAsync(default)).ReturnsAsync(new List<Employee> { employee }.AsReadOnly());
        _accrualRepo.Setup(r => r.GetAllActivePoliciesAsync(default))
            .ReturnsAsync(new List<LeaveAccrualPolicy> { MakePolicy(type.Id, 12, null, daysPerYear, type: type) }.AsReadOnly());
        _accrualRepo.Setup(r => r.TransactionExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), default))
            .ReturnsAsync(false);
        var amounts = new List<decimal>();
        _accrualRepo.Setup(r => r.AddTransactionAsync(It.IsAny<LeaveAccrualTransaction>(), default))
            .Callback((LeaveAccrualTransaction t, CancellationToken _) => amounts.Add(t.DaysAccrued));

        for (var month = 1; month <= 12; month++)
            await _sut.RunAccrualsAsync(2026, month);

        return amounts;
    }

    [Fact]
    public async Task MonthlyAccrual_OfFiveDays_TotalsExactlyFive_InTwoDecimalAmounts()
    {
        // 5/12 is 0.41666...; stored at two decimals (numeric(5,2)) as 0.42 a month it would
        // total 5.04. Rounding the running total instead keeps the year at exactly 5.
        var amounts = await AYearOfMonthlyAccrual(5m);

        amounts.Should().HaveCount(12);
        amounts.Sum().Should().Be(5.00m);
        amounts.Should().OnlyContain(a => a == Math.Round(a, 2) && (a == 0.41m || a == 0.42m));
        amounts.Should().Equal(0.42m, 0.41m, 0.42m, 0.42m, 0.41m, 0.42m, 0.42m, 0.41m, 0.42m, 0.42m, 0.41m, 0.42m);
    }

    [Fact]
    public async Task MonthlyAccrual_ThatDividesEvenlyByTwelve_IsTheSameEveryMonth()
    {
        var amounts = await AYearOfMonthlyAccrual(15m);

        amounts.Should().HaveCount(12).And.OnlyContain(a => a == 1.25m);
        amounts.Sum().Should().Be(15m);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(10)]
    [InlineData(13.5)]
    public async Task MonthlyAccrual_AlwaysTotalsTheYearsDays(double daysPerYear)
    {
        var amounts = await AYearOfMonthlyAccrual((decimal)daysPerYear);

        amounts.Sum().Should().Be((decimal)daysPerYear);
        amounts.Should().OnlyContain(a => a == Math.Round(a, 2));
    }
}
