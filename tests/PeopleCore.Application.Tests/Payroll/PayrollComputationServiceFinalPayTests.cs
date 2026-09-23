using FluentAssertions;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.FinalPay;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

/// <summary>
/// PayrollComputationService.Compute, fed a FinalPayExtras. All cases use a 36,500 monthly
/// salary under the 365 factor: dailyRate 1,200.00.
/// </summary>
public class PayrollComputationServiceFinalPayTests
{
    private readonly PayrollComputationService _sut = new();

    [Fact]
    public void Compute_with_final_pay_extras_prices_the_base_period_at_the_daily_rate()
    {
        var employee = NewEmployee();
        var extras = NewExtras(workingDays: 8m);

        var result = _sut.Compute(employee, NewRun(), finalPay: extras);

        result.DailyRate.Should().Be(1_200.00m);
        result.RegularPay.Should().Be(9_600.00m, "8 working days at the 1,200 daily rate");
    }

    [Fact]
    public void Compute_with_final_pay_extras_sets_the_final_pay_fields_and_rolls_them_into_gross()
    {
        var employee = NewEmployee();
        var extras = NewExtras(workingDays: 8m) with
        {
            LeaveConversionNonTaxable = 4_000m,
            LeaveConversionOtherBenefits = 1_000m,
            SeparationPay = 20_000m,
            RetirementPay = 10_000m,
            SeparationAndRetirementNonTaxable = 30_000m
        };

        var result = _sut.Compute(employee, NewRun(), finalPay: extras);

        result.LeaveConversionPay.Should().Be(5_000.00m, "the de minimis and other-benefits leave parts combined");
        result.LeaveConversionNonTaxable.Should().Be(4_000.00m);
        result.SeparationPay.Should().Be(20_000.00m);
        result.RetirementPay.Should().Be(10_000.00m);
        result.FinalPayNonTaxable.Should().Be(34_000.00m, "4,000 leave + 30,000 separation/retirement");
        result.GrossPay.Should().Be(9_600.00m + 5_000.00m + 20_000.00m + 10_000.00m);
    }

    [Fact]
    public void Compute_with_final_pay_extras_deducts_full_loan_balances_when_net_pay_covers_them()
    {
        var employee = NewEmployee();
        employee.Loans.Add(new EmployeeLoan
        {
            LoanType = LoanType.SSSLoan, MonthlyDeduction = 1_000m, RemainingBalance = 5_000m, IsActive = true
        });
        employee.Loans.Add(new EmployeeLoan
        {
            LoanType = LoanType.CompanyLoan, MonthlyDeduction = 1_000m, RemainingBalance = 3_000m, IsActive = true
        });
        var extras = NewExtras(workingDays: 30m);

        var result = _sut.Compute(employee, NewRun(), finalPay: extras);

        result.LoanDeductions.Should().Be(8_000.00m, "both balances in full, not the instalments");
        result.LoanDeductionLines.Should().HaveCount(2);
        result.LoanDeductionLines.Should().OnlyContain(l => l.Amount == 5_000.00m || l.Amount == 3_000.00m);
    }

    [Fact]
    public void Compute_with_final_pay_extras_prorates_loan_balances_when_the_final_pay_cannot_cover_them()
    {
        // regularPay 6,000 (5 days) - statutory (1,750 SSS + 912.50 PhilHealth + 200 Pag-IBIG =
        // 2,862.50, tax overridden to 0) leaves a 3,137.50 discretionary budget against an
        // 8,000 loan total, so both balances are pro-rated instead of paid in full.
        var employee = NewEmployee();
        var sss = new EmployeeLoan
        {
            LoanType = LoanType.SSSLoan, MonthlyDeduction = 1_000m, RemainingBalance = 5_000m, IsActive = true
        };
        var company = new EmployeeLoan
        {
            LoanType = LoanType.CompanyLoan, MonthlyDeduction = 1_000m, RemainingBalance = 3_000m, IsActive = true
        };
        employee.Loans.Add(sss);
        employee.Loans.Add(company);
        var extras = NewExtras(workingDays: 5m);

        var result = _sut.Compute(employee, NewRun(), finalPay: extras);

        result.RegularPay.Should().Be(6_000.00m);
        result.LoanDeductions.Should().Be(3_137.50m);
        result.LoanDeductionLines.Should().HaveCount(2);
        result.LoanDeductionLines.Single(l => l.EmployeeLoanId == sss.Id).Amount.Should().Be(1_960.94m);
        result.LoanDeductionLines.Single(l => l.EmployeeLoanId == company.Id).Amount.Should().Be(1_176.56m);
    }

    [Fact]
    public void Compute_with_final_pay_extras_puts_hr_deductions_in_other_deductions_after_loans()
    {
        var employee = NewEmployee();
        employee.Loans.Add(new EmployeeLoan
        {
            LoanType = LoanType.SSSLoan, MonthlyDeduction = 1_000m, RemainingBalance = 5_000m, IsActive = true
        });
        var extras = NewExtras(workingDays: 30m) with
        {
            Deductions = [("Unreturned equipment", 1_500m)]
        };

        var result = _sut.Compute(employee, NewRun(), finalPay: extras);

        result.LoanDeductions.Should().Be(5_000.00m);
        result.OtherDeductions.Should().Be(1_500.00m, "the discretionary budget covers the loan and the HR deduction");
    }

    [Fact]
    public void Compute_with_final_pay_extras_caps_hr_deductions_by_what_is_left_after_loans()
    {
        // regularPay 6,000 (5 days) - 2,862.50 statutory leaves 3,137.50; the 5,000 loan balance
        // alone exceeds it, so nothing is left for the 1,500 HR deduction.
        var employee = NewEmployee();
        employee.Loans.Add(new EmployeeLoan
        {
            LoanType = LoanType.SSSLoan, MonthlyDeduction = 1_000m, RemainingBalance = 5_000m, IsActive = true
        });
        var extras = NewExtras(workingDays: 5m) with
        {
            Deductions = [("Unreturned equipment", 1_500m)]
        };

        var result = _sut.Compute(employee, NewRun(), finalPay: extras);

        result.LoanDeductions.Should().Be(3_137.50m);
        result.OtherDeductions.Should().Be(0m, "the loan already exhausted the discretionary budget");
    }

    [Fact]
    public void Compute_with_final_pay_extras_uses_the_override_as_the_withholding_tax()
    {
        var employee = NewEmployee();
        var zero = NewExtras(workingDays: 22m);
        var negative = zero with { WithholdingTaxOverride = -2_000m };

        var withZero = _sut.Compute(employee, NewRun(), finalPay: zero);
        var withNegative = _sut.Compute(employee, NewRun(), finalPay: negative);

        withNegative.WithholdingTax.Should().Be(-2_000.00m);
        withNegative.NetPay.Should().Be(withZero.NetPay + 2_000.00m,
            "a negative override reduces total deductions by the same 2,000");
    }

    [Fact]
    public void Compute_with_final_pay_extras_leave_beyond_de_minimis_is_other_benefits_untaxed_within_the_90000()
    {
        // Leave beyond the de minimis days is "other benefits" (RR 5-2011 as amended by RR
        // 11-2018): it shares the 90,000 exemption with the 13th month, so 50,000 of it - with
        // no 13th month here - is untaxed, and stays out of the withholding base.
        var employee = NewEmployee(basicSalary: 120_000m);
        var without = NewExtras(workingDays: 22m, withholdingTaxOverride: null);
        var withLeave = without with { LeaveConversionOtherBenefits = 50_000m };

        var baseline = _sut.Compute(employee, NewRun(), finalPay: without);
        var result = _sut.Compute(employee, NewRun(), finalPay: withLeave);

        result.WithholdingTax.Should().Be(baseline.WithholdingTax);
        result.FinalPayTaxable.Should().Be(0m, "none of it is taxable outright");
        result.ThirteenthMonthAndOtherBenefits.Should().Be(50_000m);
    }

    [Fact]
    public void Compute_with_final_pay_extras_leave_beyond_the_90000_is_taxed_at_the_margin_like_the_13th_month()
    {
        // 120,000 a month: daily rate 120,000 x 12 / 365 = 3,945.205... -> 3,945.21; 22 days =
        // 86,794.62. Contributions: SSS 1,750 (top bracket), PhilHealth 120,000 x 5% / 2 = 3,000
        // -> the 2,500 ceiling, Pag-IBIG 200. Withholding base 86,794.62 - 4,450 = 82,344.62 a
        // month, 988,135.44 a year - in the 800,000-2,000,000 bracket (25%).
        // 100,000 of leave beyond de minimis: 10,000 over the 90,000 exemption, taxed at the
        // margin: 10,000 x 25% = 2,500.00 on top of the period's own withholding.
        var employee = NewEmployee(basicSalary: 120_000m);
        var without = NewExtras(workingDays: 22m, withholdingTaxOverride: null);
        var withLeave = without with { LeaveConversionOtherBenefits = 100_000m };

        var baseline = _sut.Compute(employee, NewRun(), finalPay: without);
        var result = _sut.Compute(employee, NewRun(), finalPay: withLeave);

        result.RegularPay.Should().Be(86_794.62m);
        result.WithholdingTax.Should().Be(baseline.WithholdingTax + 2_500.00m);
    }

    [Theory]
    [InlineData(PayFrequency.Monthly)]
    [InlineData(PayFrequency.SemiMonthly)]
    public void Compute_with_final_pay_extras_prorates_allowances_over_the_salary_days(PayFrequency frequency)
    {
        // Monthly x 12 / 365 x 13 salary days, whatever the frequency - not a period's share:
        //   taxable 3,650 x 12 / 365 = 120 a day x 13 = 1,560.00
        //   non-taxable 1,825 x 12 / 365 = 60 a day x 13 = 780.00
        var employee = NewEmployee();
        employee.PayFrequency = frequency;
        employee.Allowances.Add(new EmployeeAllowance { Type = AllowanceType.Transportation, Amount = 3_650m, IsTaxable = true });
        employee.Allowances.Add(new EmployeeAllowance { Type = AllowanceType.Meal, Amount = 1_825m, IsTaxable = false });

        var result = _sut.Compute(employee, NewRun(), finalPay: NewExtras(workingDays: 13m));

        result.TaxableAllowances.Should().Be(1_560m);
        result.NonTaxableAllowances.Should().Be(780m);
    }

    [Fact]
    public void Compute_with_final_pay_extras_prorates_allowances_at_the_factors_daily_rate()
    {
        // Under 313: 3,650 x 12 x 11 / 313 = 481,800 / 313 = 1,539.297... -> 1,539.30;
        //            1,825 x 12 x 11 / 313 = 240,900 / 313 = 769.648... -> 769.65.
        var employee = NewEmployee();
        employee.Allowances.Add(new EmployeeAllowance { Type = AllowanceType.Transportation, Amount = 3_650m, IsTaxable = true });
        employee.Allowances.Add(new EmployeeAllowance { Type = AllowanceType.Meal, Amount = 1_825m, IsTaxable = false });

        var result = _sut.Compute(employee, NewRun(), dailyRateFactor: 313m, finalPay: NewExtras(workingDays: 11m));

        result.TaxableAllowances.Should().Be(1_539.30m);
        result.NonTaxableAllowances.Should().Be(769.65m);
    }

    [Fact]
    public void Compute_with_final_pay_extras_pays_no_allowances_for_no_salary_days()
    {
        var employee = NewEmployee();
        employee.Allowances.Add(new EmployeeAllowance { Type = AllowanceType.Transportation, Amount = 3_650m, IsTaxable = true });
        employee.Allowances.Add(new EmployeeAllowance { Type = AllowanceType.Meal, Amount = 1_825m, IsTaxable = false });

        var result = _sut.Compute(employee, NewRun(), finalPay: NewExtras(workingDays: 0m));

        result.TaxableAllowances.Should().Be(0m);
        result.NonTaxableAllowances.Should().Be(0m);
    }

    [Theory]
    [InlineData(PayFrequency.Monthly)]
    [InlineData(PayFrequency.SemiMonthly)]
    public void Compute_with_final_pay_extras_takes_the_months_full_contributions_when_nothing_was_deducted(PayFrequency frequency)
    {
        // A whole month on 36,500 whatever the frequency - never a semi-monthly half:
        // employee SSS 1,750, PhilHealth 912.50, Pag-IBIG 200; employer 3,530, 912.50, 200.
        var employee = NewEmployee();
        employee.PayFrequency = frequency;

        var result = _sut.Compute(employee, NewRun(), finalPay: NewExtras(workingDays: 5m));

        (result.SSSEmployee, result.PhilHealthEmployee, result.PagIbigEmployee).Should().Be((1_750m, 912.50m, 200m));
        (result.SSSEmployer, result.PhilHealthEmployer, result.PagIbigEmployer).Should().Be((3_530m, 912.50m, 200m));
    }

    [Fact]
    public void Compute_with_final_pay_extras_tops_the_month_up_and_never_past_it()
    {
        // Already deducted: employee SSS 875, PhilHealth 456.25, Pag-IBIG 250 (more than the 200
        // month); employer 1,765, 1,000 (more than 912.50), 100. Each share is topped up to the
        // month and no further: 875, 456.25, 0; 1,765, 0, 100.
        var extras = NewExtras(workingDays: 5m) with
        {
            ContributionsDeductedInMonth = new ContributionShares(
                SssEmployee: 875m, SssEmployer: 1_765m, PhilHealthEmployee: 456.25m, PhilHealthEmployer: 1_000m,
                PagIbigEmployee: 250m, PagIbigEmployer: 100m)
        };

        var result = _sut.Compute(NewEmployee(), NewRun(), finalPay: extras);

        (result.SSSEmployee, result.PhilHealthEmployee, result.PagIbigEmployee).Should().Be((875m, 456.25m, 0m));
        (result.SSSEmployer, result.PhilHealthEmployer, result.PagIbigEmployer).Should().Be((1_765m, 0m, 100m));
    }

    private static PayrollRun StraddlingNewYear() => new()
    {
        RunNumber = "FP-2025-001",
        PeriodStart = new DateOnly(2024, 12, 16),
        PeriodEnd = new DateOnly(2025, 1, 10),
        PayDate = new DateOnly(2025, 1, 31),
        Frequency = PayFrequency.Monthly
    };

    [Fact]
    public void Compute_with_final_pay_extras_takes_the_sss_schedule_of_the_contribution_month()
    {
        // The final period runs Dec 16, 2024 to a Jan 10, 2025 last working day; its contributions
        // are January 2025's, so the schedule is the one in force on Jan 1, 2025 (Circular
        // 2024-006): 36,500 is in the top bracket, MSC 35,000 - employee 1,750, employer 3,530.
        // Looked up at the period start (Dec 16, 2024) no schedule is registered at all.
        var result = _sut.Compute(NewEmployee(), StraddlingNewYear(), finalPay: NewExtras(workingDays: 26m));

        result.SSSEmployee.Should().Be(1_750m);
        result.SSSEmployer.Should().Be(3_530m);
    }

    [Fact]
    public void Compute_on_a_regular_run_still_takes_the_sss_schedule_at_the_period_start()
    {
        var act = () => _sut.Compute(NewEmployee(), StraddlingNewYear());

        act.Should().Throw<NotSupportedException>();
    }

    private static EmployeeCompensation NewEmployee(decimal basicSalary = 36_500m) => new()
    {
        BasicSalary = basicSalary,
        PayFrequency = PayFrequency.Monthly
    };

    private static PayrollRun NewRun() => new()
    {
        RunNumber = "PR-FINAL-001",
        PeriodStart = new DateOnly(2026, 1, 1),
        PeriodEnd = new DateOnly(2026, 1, 31),
        PayDate = new DateOnly(2026, 2, 5),
        Frequency = PayFrequency.Monthly
    };

    private static FinalPayExtras NewExtras(decimal workingDays, decimal? withholdingTaxOverride = 0m) => new(
        WorkingDays: workingDays,
        LeaveConversionNonTaxable: 0m,
        LeaveConversionOtherBenefits: 0m,
        SeparationPay: 0m,
        RetirementPay: 0m,
        SeparationAndRetirementNonTaxable: 0m,
        Deductions: [],
        WithholdingTaxOverride: withholdingTaxOverride);
}
