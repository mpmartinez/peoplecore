using FluentAssertions;
using Moq;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Domain.Entities.Leave;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

/// <summary>
/// LeavePayout: the pricing and recording shared by final pay and year-end leave conversion.
/// </summary>
public class LeavePayoutTests
{
    private static LeaveType Type(bool countsAsVacation) => new() { CountsAsVacationForDeMinimis = countsAsVacation };

    private static LeaveBalance Balance(LeaveType type) => new() { LeaveType = type };

    [Fact]
    public void Price_8VacationAnd5Sil_At1000_Gives10000And3000()
    {
        var vl = Type(countsAsVacation: true);
        var sil = Type(countsAsVacation: true);
        var days = new[]
        {
            new LeavePaidOut(Balance(vl), 8m),
            new LeavePaidOut(Balance(sil), 5m),
        };

        var (deMinimis, otherBenefits) = LeavePayout.Price(days, dailyRate: 1_000m);

        deMinimis.Should().Be(10_000m);
        otherBenefits.Should().Be(3_000m);
    }

    [Fact]
    public async Task RecordAsync_AddsTheDaysToUsedDays_AndSavesEachBalance()
    {
        var balance = new LeaveBalance { LeaveType = Type(true), TotalDays = 10m };
        var repo = new Mock<ILeaveBalanceRepository>();
        var now = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc);

        await LeavePayout.RecordAsync([new LeavePaidOut(balance, 3m)], repo.Object, now);

        balance.UsedDays.Should().Be(3m);
        balance.UpdatedAt.Should().Be(now);
        repo.Verify(r => r.UpdateAsync(balance, It.IsAny<CancellationToken>()), Times.Once);
    }
}
