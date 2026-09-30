using FluentAssertions;
using PeopleCore.Application.Payroll.FinalPay;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Domain.Entities.Payroll;
using Xunit;
using static PeopleCore.Application.Tests.Payroll.OpeningBalanceFakes;

namespace PeopleCore.Application.Tests.Payroll;

/// <summary>
/// Final pay's 13th month and its de minimis leave days count what the employee was paid before
/// PeopleCore: the 13th month her opening balance for the last working day's year, the leave days
/// her balance for the pay year.
/// </summary>
public partial class FinalPayServiceTests
{
    /// <summary>The service as the app builds it, reading earlier-this-year figures with <paramref name="balances"/>.</summary>
    private FinalPayService WithOpeningBalances(params PayrollOpeningBalance[] balances) => new(
        _separations.Object, _runs.Object, _compensations.Object, _loans.Object, _allowances.Object,
        _leaveBalances.Object, _leaveTypes.Object, _shifts.Object, _attendance.Object, _settings.Object,
        new Bir2316Service(_runs.Object, _employees.Object, _companies.Object, _bir2316Inputs.Object),
        new PayrollComputationService(), TimeProvider.System,
        yearToDate: new PayrollYearToDate(_runs.Object, Holding(balances).Object));

    [Fact]
    public async Task CreateAsync_The13thMonth_CountsTheBasicAndThe13thMonthOnTheOpeningBalance()
    {
        // The worked example - February's Paid run (36,500) and this period's 15,600 - with January,
        // paid before PeopleCore, on her opening balance: 36,500 basic and a 1,000 13th month.
        var sut = WithOpeningBalances(OpeningBalance(_employee.Id, basicSalary: 36_500m, thirteenthMonthPaid: 1_000m));

        await sut.CreateAsync(_separation.Id, Request());

        // (36,500 balance + 36,500 February + 15,600) / 12 = 88,600 / 12 = 7,383.33, less the
        // 1,000 already paid = 6,383.33. (Without the balance: 52,100 / 12 = 4,341.67.)
        SavedEntry.ThirteenthMonth.Should().Be(6_383.33m);
    }

    [Fact]
    public async Task CreateAsync_WithOnlyAnotherEmployeesBalance_IsTheWorkedExample()
    {
        var sut = WithOpeningBalances(OpeningBalance(Guid.NewGuid(), basicSalary: 36_500m, thirteenthMonthPaid: 1_000m,
            deMinimisLeaveDays: 10m));

        await sut.CreateAsync(_separation.Id, Request());

        // 52,100 / 12 = 4,341.67; the 5 vacation days are all de minimis: 6,000.
        SavedEntry.ThirteenthMonth.Should().Be(4_341.67m);
        SavedEntry.LeaveConversionNonTaxable.Should().Be(6_000m);
    }

    [Fact]
    public async Task CreateAsync_Leave_CountsTheOpeningBalancesDeMinimisDaysAsUsed()
    {
        // 6 of 2026's ten de minimis days were converted before PeopleCore.
        var sut = WithOpeningBalances(OpeningBalance(_employee.Id, deMinimisLeaveDays: 6m));

        await sut.CreateAsync(_separation.Id, Request());

        // The 5 vacation days are 5 x 1,200 = 6,000; only 10 - 6 = 4 of them are de minimis
        // (4,800), the fifth is other benefits (1,200).
        SavedEntry.LeaveConversionPay.Should().Be(6_000m);
        SavedEntry.LeaveConversionNonTaxable.Should().Be(4_800m);
        SavedEntry.LeaveConversionOtherBenefits.Should().Be(1_200m);
    }

    [Fact]
    public async Task LeavePaidOutAsync_OnAFinalPayComputedWithTheOpeningBalance_StillPrices()
    {
        // Computed as above (4,800 / 1,200); paying it re-prices the 5 days with the same 4 left.
        var sut = WithOpeningBalances(OpeningBalance(_employee.Id, deMinimisLeaveDays: 6m));
        await sut.CreateAsync(_separation.Id, Request());

        var paidOut = await sut.LeavePaidOutAsync(_savedRun!);

        paidOut.Should().ContainSingle().Which.Days.Should().Be(5m);
    }

    [Fact]
    public async Task CreateAsync_PaidInTheNextYear_The13thMonthTakesTheLastWorkingDaysYearsBalance_AndTheLeaveThePayYears()
    {
        // As CreateAsync_PaidInTheNextYear_TakesThe13thMonthFromTheLastWorkingDaysYear_AndSettlesThePayYear:
        // last working day 2026-12-11, paid 2027-01-15; 2026's Paid runs 365,000 + 36,500 basic
        // with a 10,000 13th month. Her 2026 balance: 36,000 basic, 2,000 13th month and 8 de
        // minimis days. Her 2027 balance: 500,000 basic and 3 de minimis days.
        _separation.LastWorkingDay = new DateOnly(2026, 12, 11);
        _paidRuns.Clear();
        var janToOct = new PayrollRun
        {
            RunNumber = "PAY-2026-010", PeriodStart = new DateOnly(2026, 1, 1), PeriodEnd = new DateOnly(2026, 10, 31),
            PayDate = new DateOnly(2026, 10, 31), Frequency = Domain.Enums.PayFrequency.Monthly,
            Status = Domain.Enums.PayrollRunStatus.Paid,
        };
        janToOct.Employees.Add(new PayrollRunEmployee
        {
            PayrollRunId = janToOct.Id, EmployeeId = _employee.Id, RegularPay = 365_000m, ThirteenthMonth = 10_000m,
        });
        var november = new PayrollRun
        {
            RunNumber = "PAY-2026-011", PeriodStart = new DateOnly(2026, 11, 1), PeriodEnd = new DateOnly(2026, 11, 30),
            PayDate = new DateOnly(2026, 11, 30), Frequency = Domain.Enums.PayFrequency.Monthly,
            Status = Domain.Enums.PayrollRunStatus.Paid,
        };
        november.Employees.Add(new PayrollRunEmployee { PayrollRunId = november.Id, EmployeeId = _employee.Id, RegularPay = 36_500m });
        _paidRuns.AddRange([janToOct, november]);
        var sut = WithOpeningBalances(
            OpeningBalance(_employee.Id, year: 2026, basicSalary: 36_000m, thirteenthMonthPaid: 2_000m, deMinimisLeaveDays: 8m),
            OpeningBalance(_employee.Id, year: 2027, basicSalary: 500_000m, deMinimisLeaveDays: 3m));

        await sut.CreateAsync(_separation.Id, Request(payDate: new DateOnly(2027, 1, 15)));

        // 13th month on 2026: (365,000 + 36,500 + 36,000 + Dec 1-11's 13,200) / 12 = 450,700 / 12
        // = 37,558.33, less 10,000 + 2,000 paid = 25,558.33. 2027's 500,000 isn't counted.
        SavedEntry.ThirteenthMonth.Should().Be(25_558.33m);
        // Leave in 2027's ten days: 10 - 3 = 7 left, so all 5 vacation days are de minimis (6,000).
        // 2026's 8 days would have left 2 (2,400).
        SavedEntry.LeaveConversionNonTaxable.Should().Be(6_000m);
    }

    [Fact]
    public async Task TheSummary_CountsTheOpeningBalancesOtherBenefitsAgainstTheExemptionLeftForTheLeave()
    {
        // 15 vacation days: 10 de minimis = 12,000; 5 beyond = 6,000 of other benefits. The 13th
        // month is the worked example's 4,341.67. Before PeopleCore she was paid 80,000 of other
        // benefits, so the exemption left for the leave is 90,000 - 80,000 - 4,341.67 = 5,658.33
        // of the 6,000. (Without the balance: 85,658.33 left, all 6,000 exempt, 18,000 non-taxable.)
        _balances.Clear();
        _balances.Add(Balance("Vacation Leave", totalDays: 15m, convertible: true, countsAsVacation: true));
        var sut = WithOpeningBalances(OpeningBalance(_employee.Id, otherBenefitsPaid: 80_000m));

        var summary = await sut.CreateAsync(_separation.Id, Request());

        SavedEntry.ThirteenthMonth.Should().Be(4_341.67m);
        summary.LeaveConversionPay.Should().Be(18_000m);
        summary.LeaveConversionNonTaxable.Should().Be(17_658.33m);   // 12,000 + 5,658.33
        (await sut.GetAsync(_separation.Id))!.LeaveConversionNonTaxable.Should().Be(17_658.33m);
    }
}
