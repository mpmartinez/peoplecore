using FluentAssertions;
using PeopleCore.Domain.Payroll;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

/// <summary>
/// Every expectation here is a value printed in the DOLE Handbook on Workers' Statutory
/// Monetary Benefits, 2024 Edition, section A. The point of the exercise is that the rule
/// reproduces all forty of them — if it does, a combination the attendance schema cannot yet
/// record is already rated correctly.
/// </summary>
public class DolePremiumRatesTests
{
    // ─── Base: first eight hours, no night shift, no overtime ─────────────────
    [Theory]
    [InlineData(WorkDayType.Ordinary, 1.00)]
    [InlineData(WorkDayType.RestDay, 1.30)]
    [InlineData(WorkDayType.SpecialNonWorking, 1.30)]
    [InlineData(WorkDayType.SpecialNonWorkingOnRestDay, 1.50)]
    [InlineData(WorkDayType.DoubleSpecialNonWorking, 1.50)]
    [InlineData(WorkDayType.DoubleSpecialNonWorkingOnRestDay, 1.95)]
    [InlineData(WorkDayType.RegularHoliday, 2.00)]
    [InlineData(WorkDayType.RegularHolidayOnRestDay, 2.60)]
    [InlineData(WorkDayType.DoubleRegularHoliday, 3.00)]
    [InlineData(WorkDayType.DoubleRegularHolidayOnRestDay, 3.90)]
    public void Base_rates_match_the_handbook(WorkDayType day, decimal expected)
    {
        DolePremiumRates.Rate(day).Should().Be(expected);
    }

    // ─── Night shift: base x 1.10 ─────────────────────────────────────────────
    [Theory]
    [InlineData(WorkDayType.Ordinary, 1.10)]
    [InlineData(WorkDayType.RestDay, 1.43)]
    [InlineData(WorkDayType.SpecialNonWorking, 1.43)]
    [InlineData(WorkDayType.SpecialNonWorkingOnRestDay, 1.65)]
    [InlineData(WorkDayType.DoubleSpecialNonWorking, 1.65)]
    [InlineData(WorkDayType.DoubleSpecialNonWorkingOnRestDay, 2.145)]
    [InlineData(WorkDayType.RegularHoliday, 2.20)]
    [InlineData(WorkDayType.RegularHolidayOnRestDay, 2.86)]
    [InlineData(WorkDayType.DoubleRegularHoliday, 3.30)]
    [InlineData(WorkDayType.DoubleRegularHolidayOnRestDay, 4.29)]
    public void Night_shift_rates_match_the_handbook(WorkDayType day, decimal expected)
    {
        DolePremiumRates.Rate(day, nightShift: true).Should().Be(expected);
    }

    // ─── Overtime: 1.25 on an ordinary day, 1.30 on every premium day ─────────
    [Theory]
    [InlineData(WorkDayType.Ordinary, 1.25)]
    [InlineData(WorkDayType.RestDay, 1.69)]
    [InlineData(WorkDayType.SpecialNonWorking, 1.69)]
    [InlineData(WorkDayType.SpecialNonWorkingOnRestDay, 1.95)]
    [InlineData(WorkDayType.DoubleSpecialNonWorking, 1.95)]
    [InlineData(WorkDayType.DoubleSpecialNonWorkingOnRestDay, 2.535)]
    [InlineData(WorkDayType.RegularHoliday, 2.60)]
    [InlineData(WorkDayType.RegularHolidayOnRestDay, 3.38)]
    [InlineData(WorkDayType.DoubleRegularHoliday, 3.90)]
    [InlineData(WorkDayType.DoubleRegularHolidayOnRestDay, 5.07)]
    public void Overtime_rates_match_the_handbook(WorkDayType day, decimal expected)
    {
        DolePremiumRates.Rate(day, overtime: true).Should().Be(expected);
    }

    // ─── Night shift and overtime together ────────────────────────────────────
    [Theory]
    [InlineData(WorkDayType.Ordinary, 1.375)]
    [InlineData(WorkDayType.RestDay, 1.859)]
    [InlineData(WorkDayType.SpecialNonWorking, 1.859)]
    [InlineData(WorkDayType.SpecialNonWorkingOnRestDay, 2.145)]
    [InlineData(WorkDayType.DoubleSpecialNonWorking, 2.145)]
    [InlineData(WorkDayType.DoubleSpecialNonWorkingOnRestDay, 2.7885)]
    [InlineData(WorkDayType.RegularHoliday, 2.86)]
    [InlineData(WorkDayType.RegularHolidayOnRestDay, 3.718)]
    [InlineData(WorkDayType.DoubleRegularHoliday, 4.29)]
    [InlineData(WorkDayType.DoubleRegularHolidayOnRestDay, 5.577)]
    public void Night_shift_overtime_rates_match_the_handbook(WorkDayType day, decimal expected)
    {
        DolePremiumRates.Rate(day, nightShift: true, overtime: true).Should().Be(expected);
    }

    [Fact]
    public void Overtime_is_25_percent_only_on_an_ordinary_day()
    {
        DolePremiumRates.OvertimeFactor(WorkDayType.Ordinary).Should().Be(1.25m);

        foreach (var day in Enum.GetValues<WorkDayType>().Where(d => d != WorkDayType.Ordinary))
            DolePremiumRates.OvertimeFactor(day).Should().Be(1.30m,
                "every premium day takes 30%, not the ordinary-day 25%");
    }

    [Fact]
    public void Premium_is_the_part_payable_on_top_of_regular_time()
    {
        // The cases the payroll engine adds to an already-paid day.
        DolePremiumRates.Premium(WorkDayType.RegularHoliday).Should().Be(1.00m);
        DolePremiumRates.Premium(WorkDayType.SpecialNonWorking).Should().Be(0.30m);
        DolePremiumRates.Premium(WorkDayType.Ordinary, nightShift: true).Should().Be(0.10m);
    }

    [Fact]
    public void Rates_are_not_rounded_away()
    {
        // 1.859 and 2.7885 are published to three and four decimals; rounding the rate rather
        // than the money would lose centavos on every overtime hour.
        DolePremiumRates.Rate(WorkDayType.RestDay, nightShift: true, overtime: true)
            .Should().Be(1.859m);
        DolePremiumRates.Rate(WorkDayType.DoubleSpecialNonWorkingOnRestDay, nightShift: true, overtime: true)
            .Should().Be(2.7885m);
    }
}
