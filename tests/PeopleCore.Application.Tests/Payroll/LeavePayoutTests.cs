using FluentAssertions;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Entities.Payroll;
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
    public void Price_WithOnly4DeMinimisDaysLeftInTheYear_PutsTheRestInOtherBenefits()
    {
        // 5 SIL days at 1,200 with 6 of the year's 10 de minimis days already used:
        // 4 x 1,200 = 4,800 de minimis, 1 x 1,200 = 1,200 other benefits.
        var days = new[] { new LeavePaidOut(Balance(Type(countsAsVacation: true)), 5m) };

        var (deMinimis, otherBenefits) = LeavePayout.Price(days, dailyRate: 1_200m, deMinimisDaysLeft: 4m);

        deMinimis.Should().Be(4_800m);
        otherBenefits.Should().Be(1_200m);
    }

    [Fact]
    public void Price_WithNoDeMinimisDaysLeft_IsAllOtherBenefits()
    {
        var days = new[] { new LeavePaidOut(Balance(Type(countsAsVacation: true)), 3m) };

        LeavePayout.Price(days, dailyRate: 1_200m, deMinimisDaysLeft: -2m).Should().Be((0m, 3_600m));
    }

    [Fact]
    public void DeMinimisDaysLeft_IsTenLessTheDaysEarlierEntriesPaidAsDeMinimis()
    {
        // 7,200 at 1,200 = 6 days and 1,000 at 500 = 2 days; an entry with none doesn't count.
        // 10 - 6 - 2 = 2.
        PayrollRunEmployee[] earlier =
        [
            new() { LeaveConversionPay = 7_200m, LeaveConversionNonTaxable = 7_200m, DailyRate = 1_200m },
            new() { LeaveConversionPay = 1_500m, LeaveConversionNonTaxable = 1_000m, DailyRate = 500m },
            new() { RegularPay = 36_500m, DailyRate = 1_200m },
        ];

        LeavePayout.DeMinimisDaysLeft(earlier).Should().Be(2m);
        LeavePayout.DeMinimisDaysLeft([]).Should().Be(10m);
    }

    [Fact]
    public void DeMinimisDaysLeft_NeverGoesBelowZero()
    {
        PayrollRunEmployee[] earlier =
        [
            new() { LeaveConversionPay = 12_000m, LeaveConversionNonTaxable = 12_000m, DailyRate = 1_000m },
            new() { LeaveConversionPay = 3_000m, LeaveConversionNonTaxable = 3_000m, DailyRate = 1_000m },
        ];

        LeavePayout.DeMinimisDaysLeft(earlier).Should().Be(0m);
    }

    [Fact]
    public void StillPrices_ComparesTheDeMinimisPartAsWellAsTheTotal()
    {
        var days = new[] { new LeavePaidOut(Balance(Type(countsAsVacation: true)), 3m) };
        var entry = new PayrollRunEmployee { DailyRate = 1_200m, LeaveConversionPay = 3_600m, LeaveConversionNonTaxable = 3_600m };

        LeavePayout.StillPrices(days, entry, deMinimisDaysLeft: 10m).Should().BeTrue();
        // Same 3,600 total, but only 2 days are de minimis now: 2,400 / 1,200.
        LeavePayout.StillPrices(days, entry, deMinimisDaysLeft: 2m).Should().BeFalse();
        // Same split rule, a day fewer: 2,400 total.
        LeavePayout.StillPrices([new LeavePaidOut(days[0].Balance, 2m)], entry, deMinimisDaysLeft: 10m).Should().BeFalse();
    }

    [Fact]
    public void Apply_AddsTheDaysToUsedDays_WithoutSaving_AndGivesEachBalanceOnce()
    {
        var balance = new LeaveBalance { LeaveType = Type(true), TotalDays = 10m };
        var other = new LeaveBalance { LeaveType = Type(true), TotalDays = 5m };
        var now = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc);

        var changed = LeavePayout.Apply(
            [new LeavePaidOut(balance, 3m), new LeavePaidOut(other, 1m), new LeavePaidOut(balance, 2m)], now);

        balance.UsedDays.Should().Be(5m);
        balance.UpdatedAt.Should().Be(now);
        other.UsedDays.Should().Be(1m);
        changed.Should().HaveCount(2).And.Contain(balance).And.Contain(other);
    }

    [Fact]
    public void DeMinimisDaysUsed_IsEachEarlierEntrysDeMinimisOverItsDailyRate_ToTheCentiDay()
    {
        // 2,400 at 1,200 = 2 days; 1,000 at 1,500 = 0.666... -> 0.67; an entry with no daily rate
        // or no de minimis counts nothing. 2 + 0.67 = 2.67.
        var entries = new[]
        {
            new PayrollRunEmployee { DailyRate = 1_200m, LeaveConversionNonTaxable = 2_400m },
            new PayrollRunEmployee { DailyRate = 1_500m, LeaveConversionNonTaxable = 1_000m },
            new PayrollRunEmployee { DailyRate = 0m, LeaveConversionNonTaxable = 500m },
            new PayrollRunEmployee { DailyRate = 1_200m },
        };

        LeavePayout.DeMinimisDaysUsed(entries).Should().Be(2.67m);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(6.5, 3.5)]   // e.g. 2 days on a Paid run + 4.5 on the opening balance
    [InlineData(10, 0)]
    [InlineData(12, 0)]      // never below zero
    public void DeMinimisDaysLeft_IsTheTenLessTheDaysUsed(decimal used, decimal left)
        => LeavePayout.DeMinimisDaysLeft(used).Should().Be(left);
}
