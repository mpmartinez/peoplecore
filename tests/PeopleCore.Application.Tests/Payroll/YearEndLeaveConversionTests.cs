using FluentAssertions;
using Moq;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Enums;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

/// <summary>
/// YearEndLeaveConversion: an employee's convertible days for a year. Days = max(0, that year's balance less the employee's Pending holds of the type charged
/// to that year); only active, paid, Accrued ConvertsAtYearEnd types count, and only balances that come to more
/// than 0 days are returned.
/// </summary>
public class YearEndLeaveConversionTests
{
    private const int Year = 2026;

    private readonly Mock<ILeaveBalanceRepository> _balances = new();
    private readonly Mock<ILeaveRequestRepository> _requests = new();
    private readonly Mock<ILeaveTypeRepository> _leaveTypes = new();

    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly LeaveType _sil = new() { Name = "Sick Leave", Code = "SIL", IsActive = true, ConvertsAtYearEnd = true };
    private readonly LeaveType _vl = new() { Name = "Vacation Leave", Code = "VL", IsActive = true, ConvertsAtYearEnd = true };

    private readonly YearEndLeaveConversion _sut;

    public YearEndLeaveConversionTests()
    {
        _sut = new YearEndLeaveConversion(_balances.Object, _requests.Object, _leaveTypes.Object);
    }

    private LeaveBalance Balance(LeaveType type, decimal remaining)
        => new() { EmployeeId = _employeeId, LeaveTypeId = type.Id, LeaveType = type, Year = Year, TotalDays = remaining };

    private LeaveRequest Pending(Guid leaveTypeId, DateOnly start, DateOnly end, decimal totalDays, decimal daysInStartYear)
        => new()
        {
            EmployeeId = _employeeId, LeaveTypeId = leaveTypeId, StartDate = start, EndDate = end,
            TotalDays = totalDays, DaysInStartYear = daysInStartYear, Status = LeaveStatus.Pending
        };

    private void GivenTypes(params LeaveType[] types)
        => _leaveTypes.Setup(t => t.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(types);

    private void GivenBalances(params LeaveBalance[] balances)
        => _balances.Setup(b => b.GetByEmployeeAsync(_employeeId, Year, It.IsAny<CancellationToken>())).ReturnsAsync(balances);

    private void GivenPending(Guid leaveTypeId, params LeaveRequest[] requests)
        => _requests.Setup(r => r.GetPendingAsync(_employeeId, leaveTypeId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(requests);

    [Fact]
    public async Task DaysAsync_Sil_5RemainingWith2Pending_Gives3()
    {
        GivenTypes(_sil);
        GivenBalances(Balance(_sil, 5m));
        GivenPending(_sil.Id, Pending(_sil.Id, new DateOnly(Year, 3, 1), new DateOnly(Year, 3, 2), 2m, 2m));

        var result = await _sut.DaysAsync(_employeeId, Year);

        result.Should().ContainSingle();
        result[0].Balance.LeaveTypeId.Should().Be(_sil.Id);
        result[0].Days.Should().Be(3m);
    }

    [Fact]
    public async Task DaysAsync_Vl_1RemainingWith3Pending_GivesNothing_Floored()
    {
        GivenTypes(_vl);
        GivenBalances(Balance(_vl, 1m));
        GivenPending(_vl.Id, Pending(_vl.Id, new DateOnly(Year, 3, 1), new DateOnly(Year, 3, 3), 3m, 3m));

        var result = await _sut.DaysAsync(_employeeId, Year);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DaysAsync_SkipsAnInactiveYearEndType()
    {
        var inactive = new LeaveType { Name = "Old", Code = "OLD", IsActive = false, ConvertsAtYearEnd = true };
        GivenTypes(inactive);
        GivenBalances(Balance(inactive, 5m));

        var result = await _sut.DaysAsync(_employeeId, Year);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DaysAsync_SkipsATypeWithoutTheSetting()
    {
        var noSetting = new LeaveType { Name = "Emergency", Code = "EL", IsActive = true, ConvertsAtYearEnd = false };
        GivenTypes(noSetting);
        GivenBalances(Balance(noSetting, 5m));

        var result = await _sut.DaysAsync(_employeeId, Year);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DaysAsync_SkipsAYearlyAllowanceType_ThatHasTheSettingOn()
    {
        var yearly = new LeaveType
        {
            Name = "Birthday", Code = "BDAY", IsActive = true, ConvertsAtYearEnd = true,
            EntitlementKind = LeaveEntitlementKind.YearlyAllowance
        };
        GivenTypes(yearly);
        GivenBalances(Balance(yearly, 1m));

        var result = await _sut.DaysAsync(_employeeId, Year);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DaysAsync_SkipsAnUnpaidType_ThatHasTheSettingOn()
    {
        var unpaid = new LeaveType { Name = "Unpaid", Code = "LWOP", IsActive = true, ConvertsAtYearEnd = true, IsPaid = false };
        GivenTypes(unpaid);
        GivenBalances(Balance(unpaid, 5m));

        var result = await _sut.DaysAsync(_employeeId, Year);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DaysAsync_NoBalanceRow_GivesNothing()
    {
        GivenTypes(_sil);
        GivenBalances();

        var result = await _sut.DaysAsync(_employeeId, Year);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DaysAsync_APendingRequestSpanningNewYear_ChargesOnlyItsYearsPart()
    {
        // Filed 2025-12-29 to 2026-01-02: 3 days charged to 2025, 2 days charged to 2026.
        GivenTypes(_sil);
        GivenBalances(Balance(_sil, 5m));
        GivenPending(_sil.Id, Pending(_sil.Id, new DateOnly(2025, 12, 29), new DateOnly(2026, 1, 2), 5m, 3m));

        var result = await _sut.DaysAsync(_employeeId, Year);

        result.Should().ContainSingle();
        result[0].Days.Should().Be(3m);   // 5 remaining - 2 held in 2026
    }
}
