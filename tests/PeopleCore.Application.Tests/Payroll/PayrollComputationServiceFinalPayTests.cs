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
            LeaveConversionTaxable = 1_000m,
            SeparationPay = 20_000m,
            RetirementPay = 10_000m,
            SeparationAndRetirementNonTaxable = 30_000m
        };

        var result = _sut.Compute(employee, NewRun(), finalPay: extras);

        result.LeaveConversionPay.Should().Be(5_000.00m, "the non-taxable and taxable leave parts combined");
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
    public void Compute_with_final_pay_extras_taxable_leave_conversion_raises_the_withholding_base()
    {
        // Higher salary so the withholding base clears the 250,000-a-year threshold and a
        // change in the base actually moves the tax.
        var employee = NewEmployee(basicSalary: 120_000m);
        var without = NewExtras(workingDays: 22m, withholdingTaxOverride: null);
        var withTaxableLeave = without with { LeaveConversionTaxable = 50_000m };

        var baseline = _sut.Compute(employee, NewRun(), finalPay: without);
        var result = _sut.Compute(employee, NewRun(), finalPay: withTaxableLeave);

        result.WithholdingTax.Should().BeGreaterThan(baseline.WithholdingTax,
            "the taxable leave conversion joins the withholding base like a taxable allowance");
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
        LeaveConversionTaxable: 0m,
        SeparationPay: 0m,
        RetirementPay: 0m,
        SeparationAndRetirementNonTaxable: 0m,
        Deductions: [],
        WithholdingTaxOverride: withholdingTaxOverride);
}
