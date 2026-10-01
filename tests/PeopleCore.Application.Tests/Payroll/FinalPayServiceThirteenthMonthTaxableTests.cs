using FluentAssertions;
using Moq;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using Xunit;
using static PeopleCore.Application.Tests.Payroll.OpeningBalanceFakes;

namespace PeopleCore.Application.Tests.Payroll;

/// <summary>
/// A final pay's entry stores the taxable part of its 13th month against the pay year's exemption
/// for 13th month and other benefits - what the year's other Paid runs and her opening balance for
/// the pay year used (<see cref="Application.Payroll.Services.YearToDate.ExemptUsed"/>), as the 2316
/// and the summary's leave split count it - not just the 13th month paid in the last working day's
/// year that the engine is given. The tax and every other figure are as they were.
/// <para>
/// The worked example's figures (see FinalPayServiceTests): 13th month (36,500 February + 15,600
/// this period) / 12 = 4,341.67; gross 208,441.67; settled tax -2,000 (a refund of February's
/// withholding, the year's taxable income being far under 250,000); net 204,579.17.
/// </para>
/// </summary>
public partial class FinalPayServiceTests
{
    [Fact]
    public async Task CreateAsync_TheWorkedExample_StoresNothingTaxable()
    {
        // 4,341.67, nothing used earlier: all within the 90,000.
        await _sut.CreateAsync(_separation.Id, Request());

        SavedEntry.ThirteenthMonth.Should().Be(4_341.67m);
        SavedEntry.ThirteenthMonthTaxable.Should().Be(0m);
        SavedEntry.ThirteenthMonthExempt.Should().Be(4_341.67m);
    }

    [Fact]
    public async Task CreateAsync_StoresThe13thMonthsTaxablePart_PastTheExemptionTheOpeningBalancesUsed()
    {
        // Before PeopleCore she was paid 1,100,000 of basic and an 88,000 13th month.
        var sut = WithOpeningBalances(OpeningBalance(_employee.Id, basicSalary: 1_100_000m, thirteenthMonthPaid: 88_000m));

        await sut.CreateAsync(_separation.Id, Request());

        // 13th month: (1,100,000 + 36,500 + 15,600) / 12 = 1,152,100 / 12 = 96,008.33, less the
        // 88,000 paid = 8,008.33. 90,000 - 88,000 = 2,000 of the exemption left: 2,000 exempt,
        // 8,008.33 - 2,000 = 6,008.33 taxable.
        SavedEntry.ThirteenthMonth.Should().Be(8_008.33m);
        SavedEntry.ThirteenthMonthTaxable.Should().Be(6_008.33m);
        SavedEntry.ThirteenthMonthExempt.Should().Be(2_000m);
    }

    [Fact]
    public async Task CreateAsync_CountsTheOpeningBalancesOtherBenefits_AgainstThe13thMonthsExemption()
    {
        // Before PeopleCore she was paid 88,000 of other benefits (and no 13th month).
        var sut = WithOpeningBalances(OpeningBalance(_employee.Id, otherBenefitsPaid: 88_000m));

        var summary = await sut.CreateAsync(_separation.Id, Request());

        // 13th month 4,341.67 (the balance has no basic or 13th month). Exemption used in 2026:
        // 88,000, so 2,000 is left: 2,000 exempt, 4,341.67 - 2,000 = 2,341.67 taxable. (The 13th
        // month paid earlier alone - 0 - would leave all 90,000 and store 0.)
        SavedEntry.ThirteenthMonth.Should().Be(4_341.67m);
        SavedEntry.ThirteenthMonthTaxable.Should().Be(2_341.67m);
        SavedEntry.ThirteenthMonthExempt.Should().Be(2_000m);

        // Unchanged: the 2,341.67 taxable lifts the year's taxable income to 49,237.50 + 2,341.67 =
        // 51,579.17, still under 250,000, so the settle is the worked example's -2,000.
        SavedEntry.WithholdingTax.Should().Be(-2_000m);
        SavedEntry.GrossPay.Should().Be(208_441.67m);
        SavedEntry.NetPay.Should().Be(204_579.17m);
        summary.LeaveConversionNonTaxable.Should().Be(6_000m, "the 5 vacation days are all de minimis");
    }

    [Fact]
    public async Task CreateAsync_CountsEarlierLeaveBeyondDeMinimis_AgainstThe13thMonthsExemption()
    {
        // February also converted 89,000 of leave, none of it de minimis: 89,000 of other benefits.
        var february = _februaryRun.Employees.Single();
        february.LeaveConversionPay = 89_000m;
        february.LeaveConversionNonTaxable = 0m;

        await _sut.CreateAsync(_separation.Id, Request());

        // 13th month 4,341.67 (leave isn't basic). Exemption used in 2026: 89,000, so 1,000 is
        // left: 1,000 exempt, 4,341.67 - 1,000 = 3,341.67 taxable. (The 13th month paid earlier
        // alone - 0 - would store 0.)
        SavedEntry.ThirteenthMonth.Should().Be(4_341.67m);
        SavedEntry.ThirteenthMonthTaxable.Should().Be(3_341.67m);
        SavedEntry.ThirteenthMonthExempt.Should().Be(1_000m);

        // Unchanged: taxable income 49,237.50 + 3,341.67 = 52,579.17, under 250,000 -> -2,000.
        SavedEntry.WithholdingTax.Should().Be(-2_000m);
        SavedEntry.GrossPay.Should().Be(208_441.67m);
        SavedEntry.NetPay.Should().Be(204_579.17m);
    }

    [Fact]
    public async Task CreateAsync_PaidInTheNextYear_CountsThePayYearsExemption_NotTheLastWorkingDaysYears()
    {
        // As CreateAsync_PaidInTheNextYear_TakesThe13thMonthFromTheLastWorkingDaysYear_AndSettlesThePayYear,
        // with more basic and a larger advance: last working day 2026-12-11, paid 2027-01-15; 2026's
        // Paid runs 1,200,000 + 36,500 basic with an 85,000 13th month. Her 2027 opening balance
        // has 80,000 of other benefits.
        _separation.LastWorkingDay = new DateOnly(2026, 12, 11);
        _paidRuns.Clear();
        var janToOct = new PayrollRun
        {
            RunNumber = "PAY-2026-010", PeriodStart = new DateOnly(2026, 1, 1), PeriodEnd = new DateOnly(2026, 10, 31),
            PayDate = new DateOnly(2026, 10, 31), Frequency = PayFrequency.Monthly, Status = PayrollRunStatus.Paid,
        };
        janToOct.Employees.Add(new PayrollRunEmployee
        {
            PayrollRunId = janToOct.Id, EmployeeId = _employee.Id, RegularPay = 1_200_000m, ThirteenthMonth = 85_000m,
        });
        var november = new PayrollRun
        {
            RunNumber = "PAY-2026-011", PeriodStart = new DateOnly(2026, 11, 1), PeriodEnd = new DateOnly(2026, 11, 30),
            PayDate = new DateOnly(2026, 11, 30), Frequency = PayFrequency.Monthly, Status = PayrollRunStatus.Paid,
        };
        november.Employees.Add(new PayrollRunEmployee { PayrollRunId = november.Id, EmployeeId = _employee.Id, RegularPay = 36_500m });
        _paidRuns.AddRange([janToOct, november]);
        var sut = WithOpeningBalances(OpeningBalance(_employee.Id, year: 2027, otherBenefitsPaid: 80_000m));

        await sut.CreateAsync(_separation.Id, Request(payDate: new DateOnly(2027, 1, 15)));

        // Period Dec 1-11: 11 x 1,200 = 13,200. 13th month on 2026's basic:
        // (1,200,000 + 36,500 + 13,200) / 12 = 1,249,700 / 12 = 104,141.67, less 85,000 = 19,141.67.
        // 2027's exemption used: the 80,000 on its balance (no 2027 runs), 10,000 left: 10,000
        // exempt, 19,141.67 - 10,000 = 9,141.67 taxable. (2026's 85,000 13th month would have left
        // 5,000 and stored 14,141.67.)
        SavedEntry.RegularPay.Should().Be(13_200m);
        SavedEntry.ThirteenthMonth.Should().Be(19_141.67m);
        SavedEntry.ThirteenthMonthTaxable.Should().Be(9_141.67m);
        SavedEntry.ThirteenthMonthExempt.Should().Be(10_000m);

        // Unchanged: 2027's certificate taxes 13,200 - 2,862.50 = 10,337.50 + 9,141.67 = 19,479.17,
        // under 250,000, with nothing withheld in 2027: the settle is 0.
        SavedEntry.WithholdingTax.Should().Be(0m);
    }

    [Fact]
    public async Task UpdateAndRecompute_StoreThePayYearsFigureToo()
    {
        // As CreateAsync_CountsTheOpeningBalancesOtherBenefits_AgainstThe13thMonthsExemption: 2,341.67.
        var sut = WithOpeningBalances(OpeningBalance(_employee.Id, otherBenefitsPaid: 88_000m));
        await sut.CreateAsync(_separation.Id, Request());
        var run = _savedRun!;
        _separation.FinalPayRun = run;
        IReadOnlyList<PayrollRunEmployee>? replaced = null;
        _runs.Setup(r => r.ReplaceEntriesAsync(run, It.IsAny<IReadOnlyList<PayrollRunEmployee>>(), It.IsAny<CancellationToken>()))
             .Callback((PayrollRun _, IReadOnlyList<PayrollRunEmployee> entries, CancellationToken _) => replaced = entries)
             .Returns(Task.CompletedTask);

        await sut.UpdateAsync(_separation.Id, Request());
        var recomputed = await sut.RecomputeAsync(run);

        replaced.Should().ContainSingle().Which.ThirteenthMonthTaxable.Should().Be(2_341.67m);
        recomputed.Should().ContainSingle().Which.ThirteenthMonthTaxable.Should().Be(2_341.67m);
    }
}
