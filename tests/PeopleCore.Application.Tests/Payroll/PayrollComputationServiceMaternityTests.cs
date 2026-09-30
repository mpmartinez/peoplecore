using FluentAssertions;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

/// <summary>
/// PayrollComputationService.Compute, fed a MaternityInput (RA 11210): the SSS benefit advance,
/// tax-free and in gross pay only, and the offset that nets the SSS-covered days off regular pay.
/// <para>
/// Every case is a 30,000 monthly salary paid monthly, for January 2026 (the January 2025 SSS
/// schedule applies). Contributions, all on the monthly basic:
///   SSS: 30,000 falls in the 29,750-30,249.99 bracket, MSC 30,000 -> employee 1,500.00,
///        employer 3,030.00 (3,000 + 30 EC);
///   PhilHealth: 30,000 x 5% / 2 = 750.00 each;
///   Pag-IBIG: 10,000 (max fund salary) x 2% = 200.00 each.
/// Employee shares: 1,500 + 750 + 200 = 2,450.00.
/// </para>
/// <para>
/// Withholding without maternity: 30,000 - 2,450 = 27,550 a month, 330,600 a year ->
/// (330,600 - 250,000) x 15% = 12,090 a year -> 1,007.50 a month.
/// With a 6,000 offset: 24,000 - 2,450 = 21,550 a month, 258,600 a year ->
/// (258,600 - 250,000) x 15% = 1,290 a year -> 107.50 a month.
/// </para>
/// </summary>
public class PayrollComputationServiceMaternityTests
{
    private const decimal Salary = 30_000m;
    private const decimal EmployeeShares = 2_450m;
    private const decimal WithholdingOnFullSalary = 1_007.50m;
    private const decimal WithholdingOnReducedSalary = 107.50m;

    private readonly PayrollComputationService _sut = new();

    [Fact]
    public void Compute_without_maternity_records_neither_figure()
    {
        var result = _sut.Compute(NewEmployee(), JanuaryRun());

        result.RegularPay.Should().Be(Salary);
        result.MaternityBenefitAdvance.Should().Be(0m);
        result.MaternityBenefitOffset.Should().Be(0m);
        result.WithholdingTax.Should().Be(WithholdingOnFullSalary);
    }

    [Fact]
    public void Compute_with_an_offset_taxes_the_reduced_regular_pay_and_keeps_contributions_on_the_monthly_basic()
    {
        // Regular pay 30,000 - 6,000 offset = 24,000: the withholding base. Contributions stay on
        // the 30,000 monthly basic: 1,500 / 750 / 200 and 3,030 / 750 / 200.
        var result = _sut.Compute(NewEmployee(), JanuaryRun(), maternity: new MaternityInput(Advance: 0m, Offset: 6_000m));

        result.RegularPay.Should().Be(24_000m);
        result.MaternityBenefitOffset.Should().Be(6_000m);
        result.MaternityBenefitAdvance.Should().Be(0m);

        result.SSSEmployee.Should().Be(1_500m);
        result.SSSEmployer.Should().Be(3_030m);
        result.PhilHealthEmployee.Should().Be(750m);
        result.PhilHealthEmployer.Should().Be(750m);
        result.PagIbigEmployee.Should().Be(200m);
        result.PagIbigEmployer.Should().Be(200m);

        // (24,000 - 2,450) x 12 = 258,600 -> 1,290 a year -> 107.50.
        result.WithholdingTax.Should().Be(WithholdingOnReducedSalary);

        // Gross 24,000; net 24,000 - (2,450 + 107.50) = 21,442.50.
        result.GrossPay.Should().Be(24_000m);
        result.NetPay.Should().Be(21_442.50m);
    }

    [Fact]
    public void Compute_with_an_advance_raises_gross_by_exactly_the_advance_and_nothing_else()
    {
        var baseline = _sut.Compute(NewEmployee(), JanuaryRun());
        var result = _sut.Compute(NewEmployee(), JanuaryRun(), maternity: new MaternityInput(Advance: 70_000.35m, Offset: 0m));

        result.MaternityBenefitAdvance.Should().Be(70_000.35m);
        result.MaternityBenefitOffset.Should().Be(0m);

        // 30,000 + 70,000.35 = 100,000.35.
        result.GrossPay.Should().Be(baseline.GrossPay + 70_000.35m).And.Be(100_000.35m);

        // Not in the withholding base, the contribution base, the 13th month or other benefits.
        result.RegularPay.Should().Be(baseline.RegularPay);
        result.WithholdingTax.Should().Be(baseline.WithholdingTax).And.Be(WithholdingOnFullSalary);
        result.SSSEmployee.Should().Be(baseline.SSSEmployee);
        result.PhilHealthEmployee.Should().Be(baseline.PhilHealthEmployee);
        result.PagIbigEmployee.Should().Be(baseline.PagIbigEmployee);
        result.ThirteenthMonth.Should().Be(0m);
        result.ThirteenthMonthAndOtherBenefits.Should().Be(0m);
        result.TaxableAllowances.Should().Be(0m);
        result.NonTaxableAllowances.Should().Be(0m);
        result.FinalPayNonTaxable.Should().Be(0m);

        // Net 100,000.35 - (2,450 + 1,007.50) = 96,542.85.
        result.TotalDeductions.Should().Be(baseline.TotalDeductions);
        result.NetPay.Should().Be(96_542.85m);
    }

    [Fact]
    public void Compute_with_both_pays_the_advance_and_taxes_the_reduced_regular_pay()
    {
        var result = _sut.Compute(NewEmployee(), JanuaryRun(),
            maternity: new MaternityInput(Advance: 70_000.35m, Offset: 6_000m));

        result.RegularPay.Should().Be(24_000m);
        result.MaternityBenefitOffset.Should().Be(6_000m);
        result.MaternityBenefitAdvance.Should().Be(70_000.35m);
        result.WithholdingTax.Should().Be(WithholdingOnReducedSalary);
        (result.SSSEmployee + result.PhilHealthEmployee + result.PagIbigEmployee).Should().Be(EmployeeShares);

        // Gross 24,000 + 70,000.35 = 94,000.35; net 94,000.35 - (2,450 + 107.50) = 91,442.85.
        result.GrossPay.Should().Be(94_000.35m);
        result.NetPay.Should().Be(91_442.85m);
    }

    [Fact]
    public void Compute_strikes_the_13th_month_from_the_reduced_regular_pay_and_never_from_the_advance()
    {
        // Eleven earlier months of 30,000 = 330,000, and this month's 30,000 - 6,000 = 24,000:
        // (330,000 + 24,000) / 12 = 29,500. The 70,000.35 advance adds nothing to it.
        var result = _sut.Compute(NewEmployee(), JanuaryRun(), includeThirteenthMonth: true,
            basicEarnedEarlierInYear: 330_000m, maternity: new MaternityInput(70_000.35m, 6_000m));

        result.ThirteenthMonth.Should().Be(29_500m);
        result.ThirteenthMonthAndOtherBenefits.Should().Be(29_500m);
        // 29,500 is inside the 90,000 exemption, so the withholding is the reduced salary's alone.
        result.WithholdingTax.Should().Be(WithholdingOnReducedSalary);
        // 24,000 + 29,500 + 70,000.35 = 123,500.35.
        result.GrossPay.Should().Be(123_500.35m);
    }

    [Fact]
    public void Compute_takes_the_offset_after_absences()
    {
        // Daily rate 30,000 x 12 / 365 = 986.30; two days absent = 1,972.60, leaving 28,027.40.
        // The 6,000 offset then comes off that: 22,027.40.
        var result = _sut.Compute(NewEmployee(), JanuaryRun(),
            attendance: new PayrollAttendanceInput { AbsenceDays = 2m },
            maternity: new MaternityInput(0m, 6_000m));

        result.AbsenceDeduction.Should().Be(1_972.60m);
        result.RegularPay.Should().Be(22_027.40m);
        result.MaternityBenefitOffset.Should().Be(6_000m);
    }

    [Fact]
    public void Compute_caps_the_offset_at_regular_pay()
    {
        // An offset of 40,000 against 30,000 of regular pay takes all 30,000 and no more.
        // Withholding base 0 - 2,450 < 0 -> no tax.
        var result = _sut.Compute(NewEmployee(), JanuaryRun(), maternity: new MaternityInput(0m, 40_000m));

        result.RegularPay.Should().Be(0m);
        result.MaternityBenefitOffset.Should().Be(30_000m);
        result.WithholdingTax.Should().Be(0m);
        (result.SSSEmployee + result.PhilHealthEmployee + result.PagIbigEmployee).Should().Be(EmployeeShares);
    }

    [Fact]
    public void Compute_never_collects_a_loan_out_of_the_advance()
    {
        // All of regular pay is covered (offset 30,000), so nothing is left after the 2,450 of
        // contributions to collect a loan from: the advance is the SSS's benefit, not wages, and
        // the loan instalment stays on its balance. Net: 70,000.35 - 2,450 = 67,550.35.
        var employee = NewEmployee();
        employee.Loans.Add(new EmployeeLoan
        {
            EmployeeId = employee.EmployeeId, LoanType = LoanType.CompanyLoan, TotalAmount = 50_000m,
            MonthlyDeduction = 5_000m, RemainingBalance = 50_000m, StartDate = new DateOnly(2025, 6, 1)
        });

        var result = _sut.Compute(employee, JanuaryRun(), maternity: new MaternityInput(70_000.35m, 30_000m));

        result.LoanDeductions.Should().Be(0m);
        result.LoanDeductionLines.Should().BeEmpty();
        result.NetPay.Should().Be(67_550.35m);
    }

    [Fact]
    public void Compute_leaves_the_salary_differential_out_of_the_withholding_base_but_in_regular_pay_and_the_13th_month()
    {
        // RMC 105-2019: the pay for the leave days the SSS offset leaves - the salary differential -
        // is part of the maternity benefit and exempt. Offset 6,000 and differential 4,000 on the
        // 30,000: regular pay 24,000 still holds the differential. Withholding base
        // 24,000 - 4,000 - 2,450 = 17,550 a month, 210,600 a year: under 250,000, so no tax.
        // 13th month: (330,000 + 24,000) / 12 = 29,500 - the differential counts toward it.
        var result = _sut.Compute(NewEmployee(), JanuaryRun(), includeThirteenthMonth: true,
            basicEarnedEarlierInYear: 330_000m, maternity: new MaternityInput(0m, 6_000m, 4_000m));

        result.RegularPay.Should().Be(24_000m);
        result.MaternityBenefitOffset.Should().Be(6_000m);
        result.MaternityDifferential.Should().Be(4_000m);
        result.WithholdingTax.Should().Be(0m);
        result.ThirteenthMonth.Should().Be(29_500m);
        (result.SSSEmployee + result.PhilHealthEmployee + result.PagIbigEmployee).Should().Be(EmployeeShares);

        // Gross 24,000 + 29,500 = 53,500; net 53,500 - 2,450 = 51,050.
        result.GrossPay.Should().Be(53_500m);
        result.NetPay.Should().Be(51_050m);
    }

    [Fact]
    public void Compute_with_a_differential_taxes_only_the_pay_outside_the_leave()
    {
        // Offset 2,000, differential 1,000: regular pay 28,000. Base 28,000 - 1,000 - 2,450 =
        // 24,550 a month, 294,600 a year -> (294,600 - 250,000) x 15% = 6,690 -> 557.50 a month.
        // Taxing the differential too would give 25,550 -> 306,600 -> 8,490 -> 707.50.
        var result = _sut.Compute(NewEmployee(), JanuaryRun(), maternity: new MaternityInput(0m, 2_000m, 1_000m));

        result.RegularPay.Should().Be(28_000m);
        result.MaternityDifferential.Should().Be(1_000m);
        result.WithholdingTax.Should().Be(557.50m);
    }

    [Fact]
    public void Compute_caps_the_differential_at_the_regular_pay_the_offset_leaves()
    {
        // 30,000 - 20,000 offset = 10,000 left: a 15,000 differential can only be 10,000 of it.
        var result = _sut.Compute(NewEmployee(), JanuaryRun(), maternity: new MaternityInput(0m, 20_000m, 15_000m));

        result.RegularPay.Should().Be(10_000m);
        result.MaternityDifferential.Should().Be(10_000m);
    }

    [Fact]
    public void A_maternity_input_refuses_a_negative_differential()
    {
        var act = () => new MaternityInput(0m, 0m, -0.01m);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(-0.01, 0)]
    [InlineData(0, -0.01)]
    public void A_maternity_input_refuses_a_negative_figure(double advance, double offset)
    {
        var act = () => new MaternityInput((decimal)advance, (decimal)offset);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void An_entrys_gross_pay_includes_the_advance()
    {
        var entry = new PayrollRunEmployee { RegularPay = 24_000m, MaternityBenefitAdvance = 70_000.35m, SSSEmployee = 1_500m };

        entry.GrossPay.Should().Be(94_000.35m);
        entry.NetPay.Should().Be(92_500.35m);
    }

    [Fact]
    public void An_entrys_employer_cost_leaves_out_the_advance_SSS_reimburses()
    {
        var entry = new PayrollRunEmployee
        {
            RegularPay = 24_000m, MaternityBenefitAdvance = 70_000.35m,
            SSSEmployer = 3_030m, PhilHealthEmployer = 750m, PagIbigEmployer = 200m
        };

        // 24,000 + 3,030 + 750 + 200 = 27,980: gross (94,000.35) less the 70,000.35 advance.
        entry.TotalEmployerCost.Should().Be(27_980m);
    }

    private static EmployeeCompensation NewEmployee() => new()
    {
        EmployeeId = Guid.NewGuid(),
        BasicSalary = Salary,
        PayFrequency = PayFrequency.Monthly
    };

    private static PayrollRun JanuaryRun() => new()
    {
        RunNumber = "PAY-2026-001",
        PeriodStart = new DateOnly(2026, 1, 1),
        PeriodEnd = new DateOnly(2026, 1, 31),
        PayDate = new DateOnly(2026, 1, 31),
        Frequency = PayFrequency.Monthly
    };
}
