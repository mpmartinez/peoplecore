using FluentAssertions;
using Moq;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using Xunit;
using static PeopleCore.Application.Tests.Payroll.OpeningBalanceFakes;

namespace PeopleCore.Application.Tests.Payroll;

/// <summary>
/// A regular run stores the taxable part of each entry's 13th month, worked out against the
/// exemption the year's earlier Paid runs and the employee's opening balance used, and hands it to
/// the payslip and the payroll page on the run's DTO.
/// </summary>
public partial class PayrollRunServiceTests
{
    [Fact]
    public async Task CreateAsync_StoresThe13thMonthsTaxablePart_PastTheExemptionTheEarlierRunsUsed()
    {
        // CreateAsync_Pays13thMonthFromTheYearsBasicLessWhatWasAlreadyPaid: 120,000 a month, paid
        // semi-monthly; an earlier Paid run with 1,380,000 of basic and a 60,000 13th month.
        var employeeId = Guid.NewGuid();
        var savedRun = SetupRoundTripRepositories(new EmployeeCompensation
        {
            EmployeeId = employeeId, BasicSalary = 120_000m, PayFrequency = PayFrequency.SemiMonthly, TaxCode = "S"
        });
        var earlier = new PayrollRun
        {
            RunNumber = "PAY-2026-000", Status = PayrollRunStatus.Paid, PayDate = new DateOnly(2026, 1, 5)
        };
        earlier.Employees.Add(new PayrollRunEmployee
        {
            EmployeeId = employeeId, RegularPay = 1_380_000m, ThirteenthMonth = 60_000m
        });
        _runRepo.Setup(r => r.GetPaidRunsInYearAsync(2026, It.IsAny<CancellationToken>())).ReturnsAsync([earlier]);

        var dto = await _sut.CreateAsync(With13thMonth(RoundTripRequest(employeeId)), CancellationToken.None);

        // 13th month: (1,380,000 + this half-month's 60,000) / 12 = 120,000 due, less 60,000 paid =
        // 60,000. The earlier run used 60,000 of the exemption, so 90,000 - 60,000 = 30,000 is left:
        // 30,000 exempt, 60,000 - 30,000 = 30,000 taxable. The tax stays 17,881.25.
        var entry = savedRun()!.Employees.Single();
        entry.ThirteenthMonth.Should().Be(60_000m);
        entry.ThirteenthMonthTaxable.Should().Be(30_000m);
        entry.ThirteenthMonthExempt.Should().Be(30_000m);
        entry.WithholdingTax.Should().Be(17_881.25m);
        dto.Employees.Single().ThirteenthMonthTaxable.Should().Be(30_000m);
    }

    [Fact]
    public async Task CreateAsync_StoresThe13thMonthsTaxablePart_PastTheExemptionTheOpeningBalanceUsed()
    {
        // CreateAsync_TaxesThe13thMonthPastTheExemptionTheOpeningBalancesOtherBenefitsUsed: the run
        // has 690,000 of basic; the balance 690,000 of basic, a 60,000 13th month and 30,000 of
        // other benefits.
        var (employeeId, savedRun) = At120000WithAPaidRun();
        var sut = WithOpeningBalances(OpeningBalance(employeeId, basicSalary: 690_000m, thirteenthMonthPaid: 60_000m,
            otherBenefitsPaid: 30_000m));

        var dto = await sut.CreateAsync(With13thMonth(RoundTripRequest(employeeId)), CancellationToken.None);

        // 13th month: (690,000 + 690,000 + 60,000) / 12 = 120,000 due, less 60,000 paid = 60,000.
        // The balance used 60,000 + 30,000 = 90,000 of the exemption - none left - so all 60,000 is
        // taxable. The tax stays 25,381.25.
        var entry = savedRun()!.Employees.Single();
        entry.ThirteenthMonth.Should().Be(60_000m);
        entry.ThirteenthMonthTaxable.Should().Be(60_000m);
        entry.ThirteenthMonthExempt.Should().Be(0m);
        entry.WithholdingTax.Should().Be(25_381.25m);
        dto.Employees.Single().ThirteenthMonthTaxable.Should().Be(60_000m);
    }

    [Fact]
    public async Task CreateAsync_WithoutThe13thMonth_StoresNothingTaxable()
    {
        var (employeeId, savedRun) = At120000WithAPaidRun();
        var sut = WithOpeningBalances(OpeningBalance(employeeId, otherBenefitsPaid: 90_000m));

        var dto = await sut.CreateAsync(RoundTripRequest(employeeId), CancellationToken.None);

        savedRun()!.Employees.Single().ThirteenthMonthTaxable.Should().Be(0m);
        dto.Employees.Single().ThirteenthMonthTaxable.Should().Be(0m);
    }

    [Fact]
    public async Task GetAsync_AnEntryComputedBeforeTheSplitWasStored_HasNoTaxablePart()
    {
        // Null on entries computed before this change; the DTO carries the null through.
        var (employeeId, savedRun) = At120000WithAPaidRun();
        await _sut.CreateAsync(With13thMonth(RoundTripRequest(employeeId)), CancellationToken.None);
        var run = savedRun()!;
        run.Employees.Single().ThirteenthMonthTaxable = null;

        var dto = await _sut.GetAsync(run.Id);

        dto!.Employees.Single().ThirteenthMonthTaxable.Should().BeNull();
    }
}
