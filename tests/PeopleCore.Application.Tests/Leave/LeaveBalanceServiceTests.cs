using FluentAssertions;
using Moq;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Leave.Services;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Enums;
using Xunit;

namespace PeopleCore.Application.Tests.Leave;

/// <summary>A balance row says whether its type is confidential, read from the loaded type.</summary>
public class LeaveBalanceServiceTests
{
    private readonly Mock<ILeaveBalanceRepository> _repo = new();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetByEmployeeAsync_CarriesTheTypesConfidentialFlag(bool isConfidential)
    {
        var employeeId = Guid.NewGuid();
        var type = new LeaveType { Name = "VAWC Leave", Code = "VAWC", IsConfidential = isConfidential };
        _repo.Setup(r => r.GetByEmployeeAsync(employeeId, 2026, It.IsAny<CancellationToken>()))
             .ReturnsAsync([new LeaveBalance { EmployeeId = employeeId, LeaveTypeId = type.Id, LeaveType = type, Year = 2026, TotalDays = 10 }]);

        var rows = await new LeaveBalanceService(_repo.Object).GetByEmployeeAsync(employeeId, 2026);

        rows.Should().ContainSingle().Which.IsConfidential.Should().Be(isConfidential);
    }

    [Theory]
    [InlineData(LeaveEntitlementKind.PerEvent)]
    [InlineData(LeaveEntitlementKind.YearlyAllowance)]
    public async Task CarryOverAsync_SkipsATypeThatIsNotAccrued_EvenIfItSaysItCarriesOver(LeaveEntitlementKind kind)
    {
        // Only an accrued type has a yearly balance to carry; a flag left on another kind (from
        // before validation refused it) must not create one.
        var type = new LeaveType { Name = "Solo Parent Leave", Code = "SPL", EntitlementKind = kind, IsCarryOver = true };
        _repo.Setup(r => r.GetByYearAsync(2026, It.IsAny<CancellationToken>()))
             .ReturnsAsync([new LeaveBalance { EmployeeId = Guid.NewGuid(), LeaveTypeId = type.Id, LeaveType = type, Year = 2026, TotalDays = 7 }]);

        await new LeaveBalanceService(_repo.Object).CarryOverAsync(2026, 2027);

        _repo.Verify(r => r.AddAsync(It.IsAny<LeaveBalance>(), It.IsAny<CancellationToken>()), Times.Never);
        _repo.Verify(r => r.UpdateAsync(It.IsAny<LeaveBalance>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CarryOverAsync_CarriesAnAccruedTypesRemainingDays_UpToItsMaximum()
    {
        var employeeId = Guid.NewGuid();
        var type = new LeaveType
        {
            Name = "Vacation Leave", Code = "VL", EntitlementKind = LeaveEntitlementKind.Accrued,
            IsCarryOver = true, CarryOverMaxDays = 5m
        };
        _repo.Setup(r => r.GetByYearAsync(2026, It.IsAny<CancellationToken>()))
             .ReturnsAsync([new LeaveBalance { EmployeeId = employeeId, LeaveTypeId = type.Id, LeaveType = type, Year = 2026, TotalDays = 15, UsedDays = 3 }]);
        LeaveBalance? added = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<LeaveBalance>(), It.IsAny<CancellationToken>()))
             .Callback((LeaveBalance b, CancellationToken _) => added = b)
             .ReturnsAsync((LeaveBalance b, CancellationToken _) => b);

        await new LeaveBalanceService(_repo.Object).CarryOverAsync(2026, 2027);

        added!.CarriedOverDays.Should().Be(5m);
        added.Year.Should().Be(2027);
    }
}
