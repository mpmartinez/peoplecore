using FluentAssertions;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Payroll;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class PayrollComputationServiceTests
{
    private readonly PayrollComputationService _sut = new();

    // ─── SSS ──────────────────────────────────────────────────────────────

    // Expectations below are derived from SSS Circular No. 2024-006, "Schedule of SSS
    // Contributions Effective January 2025" (repealing Circular No. 2022-033) -- they are
    // computed from the circular's rules, not copied from the production table:
    //   MSC      = compensation rounded to the NEAREST 500, floor 5,000, ceiling 35,000,
    //              so each bracket spans MSC-250 .. MSC+249.99
    //   employee = 5% of MSC
    //   employer = 10% of MSC + EC, where EC is 10.00 below MSC 15,000 and 30.00 from 15,000 up
    [Theory]
    [InlineData(4_000, 250.00, 510.00)]       // below the MSC floor -> MSC 5,000: 250 | 500 + 10 EC
    [InlineData(5_249.99, 250.00, 510.00)]    // exact upper edge of the first bracket
    [InlineData(5_250, 275.00, 560.00)]       // first centavo of the next -> MSC 5,500: 275 | 550 + 10
    [InlineData(14_749.99, 725.00, 1_460.00)] // last bracket carrying the 10.00 EC -> MSC 14,500
    [InlineData(14_750, 750.00, 1_530.00)]    // EC steps to 30.00 -> MSC 15,000: 750 | 1,500 + 30
    [InlineData(20_000, 1_000.00, 2_030.00)]  // MSC 20,000: 1,000 | 2,000 + 30
    [InlineData(20_250, 1_025.00, 2_080.00)]  // MPF begins -> MSC 20,500: 1,025 | 2,000 + 50 MPF + 30
    [InlineData(34_750, 1_750.00, 3_530.00)]  // top bracket -> MSC 35,000: 1,750 | 2,000 + 1,500 MPF + 30
    [InlineData(500_000, 1_750.00, 3_530.00)] // far above the ceiling stays capped
    public void ComputeSSS_uses_the_circular_2024_006_table(decimal salary, decimal employee, decimal employer)
    {
        var (emp, emr) = _sut.ComputeSSS(salary);

        emp.Should().Be(employee);
        emr.Should().Be(employer);
    }

    [Fact]
    public void ComputeSSS_uses_the_schedule_in_force_for_the_period()
    {
        // The 2024-006 schedule takes effect January 2025; any date from then on resolves to it.
        var (emp, emr) = _sut.ComputeSSS(20_000m, asOf: new DateOnly(2025, 1, 1));

        emp.Should().Be(1_000.00m);
        emr.Should().Be(2_030.00m);
    }

    [Fact]
    public void ComputeSSS_refuses_periods_with_no_registered_schedule()
    {
        // December 2024 fell under Circular 2022-033 (14%), which is not registered. Computing
        // it under the 2025 schedule would silently overstate both shares, so it must throw.
        var act = () => _sut.ComputeSSS(20_000m, asOf: new DateOnly(2024, 12, 31));

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*2024-12-31*")
            .WithMessage("*2025-01-01*");
    }

    [Fact]
    public void ComputeSSS_rate_override_bypasses_schedule_resolution()
    {
        var rates = new ContributionRates { SSSEmployeeRate = 0.05m, SSSEmployerRate = 0.10m };

        // Even for an unregistered period, an explicit rate override is honoured rather than throwing.
        var (emp, emr) = _sut.ComputeSSS(20_000m, rates, asOf: new DateOnly(2024, 12, 31));

        emp.Should().Be(1_000.00m);
        emr.Should().Be(2_000.00m);
    }

    [Fact]
    public void ComputeSSS_honours_explicit_rate_override()
    {
        var rates = new ContributionRates { SSSEmployeeRate = 0.05m, SSSEmployerRate = 0.10m };

        var (emp, emr) = _sut.ComputeSSS(20_000m, rates);

        emp.Should().Be(1_000.00m);
        emr.Should().Be(2_000.00m);
    }

    // ─── PhilHealth ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(5_000, 250.00)]     // below floor -> clamped up to the minimum share
    [InlineData(20_000, 500.00)]    // 5% split evenly
    [InlineData(120_000, 2_500.00)] // above ceiling -> clamped to the maximum share
    public void ComputePhilHealth_clamps_between_floor_and_ceiling(decimal salary, decimal expected)
    {
        var (emp, emr) = _sut.ComputePhilHealth(salary);

        emp.Should().Be(expected);
        emr.Should().Be(expected, "employee and employer shares are equal");
    }

    // ─── Pag-IBIG ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(3_000, 60.00)]    // 2% of actual salary
    [InlineData(10_000, 200.00)]  // exactly at the Maximum Fund Salary
    [InlineData(80_000, 200.00)]  // capped at an MFS of 10,000
    public void ComputePagIbig_caps_the_credited_salary(decimal salary, decimal expected)
    {
        var (emp, emr) = _sut.ComputePagIbig(salary);

        emp.Should().Be(expected);
        emr.Should().Be(expected);
    }

    // Pag-IBIG Fund Circular No. 460 Section C: the 1% tier applies to the employee share
    // only -- the employer contributes 2% at both tiers.
    [Theory]
    [InlineData(1_000, 10.00, 20.00)]     // 1% / 2% of 1,000
    [InlineData(1_500, 15.00, 30.00)]     // at the threshold, still the lower tier
    [InlineData(1_500.01, 30.00, 30.00)]  // first centavo over -> employee moves to 2%
    [InlineData(2_000, 40.00, 40.00)]
    public void ComputePagIbig_applies_the_lower_employee_rate_at_or_below_1500(
        decimal salary, decimal expectedEmployee, decimal expectedEmployer)
    {
        var (emp, emr) = _sut.ComputePagIbig(salary);

        emp.Should().Be(expectedEmployee);
        emr.Should().Be(expectedEmployer);
    }

    [Fact]
    public void ComputePagIbig_ignores_zeroed_settings_rather_than_zeroing_the_contribution()
    {
        // Settings rows predating the contribution-rate columns were backfilled with 0, and the
        // payroll path passes them straight through. Zero is not a valid statutory rate.
        var zeroed = new ContributionRates
        {
            PagIbigEmployeeRate = 0m, PagIbigEmployerRate = 0m, PagIbigMaxFundSalary = 0m
        };

        var (emp, emr) = _sut.ComputePagIbig(20_000m, zeroed);

        emp.Should().Be(200.00m);
        emr.Should().Be(200.00m);
    }

    [Fact]
    public void ComputePhilHealth_ignores_zeroed_settings_rather_than_zeroing_the_premium()
    {
        var zeroed = new ContributionRates
        {
            PhilHealthRate = 0m, PhilHealthMinShare = 0m, PhilHealthMaxShare = 0m
        };

        var (emp, emr) = _sut.ComputePhilHealth(20_000m, zeroed);

        emp.Should().Be(500.00m);
        emr.Should().Be(500.00m);
    }

    // ─── BIR withholding tax ──────────────────────────────────────────────

    [Fact]
    public void ComputeWithholdingTax_is_zero_below_the_250k_annual_threshold()
    {
        // 10,000 semi-monthly = 240,000 annual
        _sut.ComputeWithholdingTax(10_000m, PayFrequency.SemiMonthly).Should().Be(0m);
    }

    [Fact]
    public void ComputeWithholdingTax_taxes_only_the_excess_over_250k()
    {
        // 10,500 semi-monthly = 252,000 annual; 2,000 excess at 15% = 300 annual
        _sut.ComputeWithholdingTax(10_500m, PayFrequency.SemiMonthly).Should().Be(12.50m);
    }

    [Fact]
    public void ComputeWithholdingTax_applies_the_correct_bracket_base_and_rate()
    {
        // 20,000 semi-monthly = 480,000 annual
        // bracket 400k-800k: 22,500 base + (80,000 x 20%) = 38,500 annual / 24
        _sut.ComputeWithholdingTax(20_000m, PayFrequency.SemiMonthly).Should().Be(1_604.17m);
    }

    [Fact]
    public void ComputeWithholdingTax_annualizes_a_monthly_period_over_12()
    {
        // 40,000 monthly = 480,000 annual -> 38,500 annual / 12. Annualizing over 24 would
        // read it as 960,000 and withhold 5,937.50.
        _sut.ComputeWithholdingTax(40_000m, PayFrequency.Monthly).Should().Be(3_208.33m);
    }

    // ─── Full computation ─────────────────────────────────────────────────

    [Fact]
    public void Compute_semi_monthly_halves_the_mandatory_contributions()
    {
        var employee = NewEmployee(basicSalary: 20_000m);
        var run = NewRun();

        var result = _sut.Compute(employee, run, daysWorked: 11m);

        result.RegularPay.Should().Be(10_000.00m);
        result.SSSEmployee.Should().Be(500.00m);      // 1,000.00 / 2
        result.PhilHealthEmployee.Should().Be(250.00m); // 500 / 2
        result.PagIbigEmployee.Should().Be(100.00m);    // 200 / 2
        result.WithholdingTax.Should().Be(0m,
            "taxable income of 9,150.00 semi-monthly annualises below 250,000");
        result.GrossPay.Should().Be(10_000.00m);
        result.TotalDeductions.Should().Be(850.00m);
        result.NetPay.Should().Be(9_150.00m);
    }

    [Fact]
    public void Compute_splits_allowances_by_taxability_and_pay_period()
    {
        var employee = NewEmployee(basicSalary: 20_000m);
        employee.Allowances.Add(new EmployeeAllowance
        {
            Type = AllowanceType.Transportation, Amount = 2_000m, IsTaxable = false
        });
        employee.Allowances.Add(new EmployeeAllowance
        {
            Type = AllowanceType.Communication, Amount = 1_000m, IsTaxable = true
        });

        var result = _sut.Compute(employee, NewRun(), daysWorked: 11m);

        result.NonTaxableAllowances.Should().Be(1_000.00m, "monthly amount halved for semi-monthly");
        result.TaxableAllowances.Should().Be(500.00m);
    }

    [Fact]
    public void Compute_excludes_thirteenth_month_from_the_contribution_base()
    {
        var employee = NewEmployee(basicSalary: 20_000m);

        var result = _sut.Compute(employee, NewRun(), daysWorked: 11m, includeThirteenthMonth: true);

        result.ThirteenthMonth.Should().Be(20_000.00m);
        result.GrossPay.Should().Be(30_000.00m);
        result.SSSEmployee.Should().Be(500.00m,
            "contributions are based on basic salary, not the 13th month payout");
        result.WithholdingTax.Should().Be(0m,
            "the 13th month is excluded from the taxable base");
    }

    [Fact]
    public void Compute_pays_overtime_at_125_percent_on_an_ordinary_day()
    {
        // 22,000 x 12 / 365 = 723.29 daily, 90.41 hourly.
        var employee = NewEmployee(basicSalary: 22_000m);

        var result = _sut.Compute(employee, NewRun(), daysWorked: 10m, overtimeHours: 8m, holidayDays: 1m);

        result.DailyRate.Should().Be(723.29m);
        result.RegularPay.Should().Be(11_000.00m, "a full period pays the full salary slice");
        result.OvertimePay.Should().Be(904.10m);   // 90.41 x 1.25 x 8
        result.HolidayPay.Should().Be(723.29m,     // 723.29 x (2.00 - 1.00) x 1
            "the monthly salary already pays the holiday, so work on it adds the increment");
    }

    [Fact]
    public void Compute_prorates_active_loan_deductions_only()
    {
        var employee = NewEmployee(basicSalary: 20_000m);
        employee.Loans.Add(new EmployeeLoan
        {
            LoanType = LoanType.SSSLoan, MonthlyDeduction = 1_000m, RemainingBalance = 16_000m, IsActive = true
        });
        employee.Loans.Add(new EmployeeLoan
        {
            LoanType = LoanType.CompanyLoan, MonthlyDeduction = 5_000m, RemainingBalance = 40_000m, IsActive = false
        });

        var result = _sut.Compute(employee, NewRun(), daysWorked: 11m);

        result.LoanDeductions.Should().Be(500.00m, "only the active loan, halved for semi-monthly");
        result.LoanDeductionLines.Should().ContainSingle()
            .Which.Amount.Should().Be(500.00m);
    }

    [Fact]
    public void Compute_never_deducts_more_than_the_loan_still_owes()
    {
        // Final instalment: 1,000/month is 500 semi-monthly, but only 200 is outstanding.
        var employee = NewEmployee(basicSalary: 20_000m);
        employee.Loans.Add(new EmployeeLoan
        {
            LoanType = LoanType.SSSLoan, MonthlyDeduction = 1_000m, RemainingBalance = 200m, IsActive = true
        });

        var result = _sut.Compute(employee, NewRun(), daysWorked: 11m);

        result.LoanDeductions.Should().Be(200.00m,
            "charging the full instalment would over-collect past the payoff");
    }

    [Fact]
    public void Compute_skips_a_loan_with_nothing_left_to_pay()
    {
        var employee = NewEmployee(basicSalary: 20_000m);
        employee.Loans.Add(new EmployeeLoan
        {
            LoanType = LoanType.CompanyLoan, MonthlyDeduction = 1_000m, RemainingBalance = 0m, IsActive = true
        });

        var result = _sut.Compute(employee, NewRun(), daysWorked: 11m);

        result.LoanDeductions.Should().Be(0m);
        result.LoanDeductionLines.Should().BeEmpty();
    }

    [Fact]
    public void Compute_does_not_deduct_a_loan_that_has_not_started()
    {
        // NewRun covers 01-01 to 01-15; this loan starts after the period ends.
        var employee = NewEmployee(basicSalary: 20_000m);
        employee.Loans.Add(new EmployeeLoan
        {
            LoanType = LoanType.CompanyLoan, MonthlyDeduction = 1_000m, RemainingBalance = 10_000m,
            StartDate = new DateOnly(2026, 2, 1), IsActive = true
        });

        var result = _sut.Compute(employee, NewRun(), daysWorked: 11m);

        result.LoanDeductions.Should().Be(0m);
        result.LoanDeductionLines.Should().BeEmpty();
    }

    [Fact]
    public void Compute_deducts_a_loan_starting_within_the_period()
    {
        var employee = NewEmployee(basicSalary: 20_000m);
        employee.Loans.Add(new EmployeeLoan
        {
            LoanType = LoanType.CompanyLoan, MonthlyDeduction = 1_000m, RemainingBalance = 10_000m,
            StartDate = new DateOnly(2026, 1, 15), IsActive = true
        });

        var result = _sut.Compute(employee, NewRun(), daysWorked: 11m);

        result.LoanDeductions.Should().Be(500.00m);
    }

    [Fact]
    public void Compute_records_each_loan_separately_so_balances_can_be_retired_exactly()
    {
        var sss = new EmployeeLoan
        {
            LoanType = LoanType.SSSLoan, MonthlyDeduction = 1_000m, RemainingBalance = 16_000m, IsActive = true
        };
        var company = new EmployeeLoan
        {
            LoanType = LoanType.CompanyLoan, MonthlyDeduction = 600m, RemainingBalance = 150m, IsActive = true
        };
        var employee = NewEmployee(basicSalary: 20_000m);
        employee.Loans.Add(sss);
        employee.Loans.Add(company);

        var result = _sut.Compute(employee, NewRun(), daysWorked: 11m);

        result.LoanDeductions.Should().Be(650.00m, "500 for the SSS loan plus the 150 still owed");
        result.LoanDeductionLines.Should().HaveCount(2);
        result.LoanDeductionLines.Single(l => l.EmployeeLoanId == sss.Id).Amount.Should().Be(500.00m);
        result.LoanDeductionLines.Single(l => l.EmployeeLoanId == company.Id).Amount.Should().Be(150.00m,
            "capped at the outstanding balance, not the 300 semi-monthly instalment");
    }

    [Fact]
    public void Compute_monthly_frequency_takes_the_full_contribution()
    {
        var employee = NewEmployee(basicSalary: 20_000m);
        employee.PayFrequency = PayFrequency.Monthly;

        var result = _sut.Compute(employee, NewRun(), daysWorked: 22m);

        result.SSSEmployee.Should().Be(1_000.00m, "monthly payroll deducts the whole contribution");
        result.PhilHealthEmployee.Should().Be(500.00m);
        result.PagIbigEmployee.Should().Be(200.00m);
    }

    [Fact]
    public void Compute_monthly_frequency_withholds_tax_on_a_monthly_period()
    {
        var employee = NewEmployee(basicSalary: 40_000m);
        employee.PayFrequency = PayFrequency.Monthly;

        var result = _sut.Compute(employee, NewRun(), daysWorked: 22m);

        // Taxable = 40,000 - 1,750 SSS - 1,000 PhilHealth - 200 Pag-IBIG = 37,050 a month.
        // x 12 = 444,600 annual -> 22,500 + (44,600 x 20%) = 31,420 annual / 12.
        result.WithholdingTax.Should().Be(2_618.33m,
            "a monthly payslip is one twelfth of the year, not one twenty-fourth");
    }

    // ─── 13th month and the 90,000 exemption ──────────────────────────────

    [Fact]
    public void Compute_withholds_nothing_extra_on_a_13th_month_under_the_90k_ceiling()
    {
        var without = _sut.Compute(NewEmployee(basicSalary: 30_000m), NewRun(), daysWorked: 11m);
        var with = _sut.Compute(NewEmployee(basicSalary: 30_000m), NewRun(), daysWorked: 11m,
            includeThirteenthMonth: true);

        with.ThirteenthMonth.Should().Be(30_000m);
        with.WithholdingTax.Should().Be(without.WithholdingTax, "30,000 is inside the 90,000 exemption");
    }

    [Fact]
    public void Compute_taxes_the_13th_month_above_the_90k_ceiling_at_the_marginal_rate()
    {
        var employee = NewEmployee(basicSalary: 120_000m);
        employee.PayFrequency = PayFrequency.Monthly;

        var result = _sut.Compute(employee, NewRun(), daysWorked: 22m, includeThirteenthMonth: true);

        // Regular: 120,000 - 1,750 SSS - 2,500 PhilHealth - 200 Pag-IBIG = 115,550 a month,
        // 1,386,600 a year -> 102,500 + (586,600 x 25%) = 249,150 / 12 = 20,762.50.
        // 13th month: 120,000 - 90,000 exempt = 30,000 taxable, still inside the 25% bracket
        // on top of the annual regular pay -> 7,500, withheld in full on this payslip.
        result.WithholdingTax.Should().Be(28_262.50m);
    }

    [Fact]
    public void Compute_counts_13th_month_paid_earlier_in_the_year_against_the_ceiling()
    {
        var result = _sut.Compute(NewEmployee(basicSalary: 40_000m), NewRun(), daysWorked: 11m,
            includeThirteenthMonth: true, thirteenthMonthPaidEarlierInYear: 80_000m);

        // Regular: 20,000 - 875 SSS - 500 PhilHealth - 100 Pag-IBIG = 18,525 a half-month,
        // 444,600 a year -> 31,420 / 24 = 1,309.17.
        // Only 10,000 of the exemption is left, so 30,000 of the 40,000 is taxable, all inside
        // the 20% bracket -> 6,000.
        result.WithholdingTax.Should().Be(7_309.17m);
    }

    private static EmployeeCompensation NewEmployee(decimal basicSalary) => new()
    {
        BasicSalary = basicSalary,
        PayFrequency = PayFrequency.SemiMonthly
    };

    private static PayrollRun NewRun() => new()
    {
        RunNumber = "PR-TEST-001",
        PeriodStart = new DateOnly(2026, 1, 1),
        PeriodEnd = new DateOnly(2026, 1, 15),
        PayDate = new DateOnly(2026, 1, 20),
        Frequency = PayFrequency.SemiMonthly
    };
}
