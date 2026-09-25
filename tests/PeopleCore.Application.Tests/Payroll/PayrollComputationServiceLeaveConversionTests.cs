using FluentAssertions;
using PeopleCore.Application.Payroll.FinalPay;
using PeopleCore.Application.Payroll.Services;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

/// <summary>
/// PayrollComputationService.Compute, fed a LeaveConversionInput on a regular December run.
/// <para>
/// All cases use an 88,000 monthly salary paid monthly, so a full month's regular pay is 88,000.
/// Contributions: SSS 1,750 (top bracket), PhilHealth 88,000 x 5% / 2 = 2,200 (under the 2,500
/// ceiling), Pag-IBIG 200. Withholding base 88,000 - 4,150 = 83,850 a month, 1,006,200 a year -
/// in the 800,000-2,000,000 bracket: 102,500 + 25% x 206,200 = 154,050 a year, 12,837.50 a month.
/// Every taxable excess below stays inside that bracket, so it is taxed at the 25% margin.
/// </para>
/// </summary>
public class PayrollComputationServiceLeaveConversionTests
{
    private const decimal RegularWithholding = 12_837.50m;

    private readonly PayrollComputationService _sut = new();

    [Fact]
    public void Compute_with_a_leave_conversion_records_it_as_a_final_pay_does()
    {
        var result = _sut.Compute(NewEmployee(), DecemberRun(),
            leaveConversion: new LeaveConversionInput(DeMinimis: 12_000m, OtherBenefits: 6_000m));

        result.LeaveConversionPay.Should().Be(18_000m, "the de minimis and other-benefits parts combined");
        result.LeaveConversionNonTaxable.Should().Be(12_000m);
        result.LeaveConversionOtherBenefits.Should().Be(6_000m);
        result.FinalPayNonTaxable.Should().Be(12_000m, "the de minimis part is the only outright non-taxable part");
        result.FinalPayTaxable.Should().Be(0m);
        result.ThirteenthMonthAndOtherBenefits.Should().Be(6_000m);
        result.GrossPay.Should().Be(88_000m + 18_000m);
    }

    [Fact]
    public void Compute_with_a_leave_conversion_keeps_it_out_of_the_withholding_base()
    {
        // No 13th month and nothing used earlier: the 6,000 of other benefits sits well inside the
        // 90,000 exemption, and the 12,000 de minimis is non-taxable outright - so the withholding
        // is the regular month's alone.
        var baseline = _sut.Compute(NewEmployee(), DecemberRun());
        var result = _sut.Compute(NewEmployee(), DecemberRun(),
            leaveConversion: new LeaveConversionInput(12_000m, 6_000m));

        baseline.WithholdingTax.Should().Be(RegularWithholding);
        result.WithholdingTax.Should().Be(RegularWithholding);
    }

    [Fact]
    public void Compute_with_a_leave_conversion_taxes_the_other_benefits_past_the_exemption_left_by_the_13th_month()
    {
        // 13th month: (968,000 earned earlier + 88,000 this month) / 12 = 88,000.
        // 13th month and other benefits: 88,000 + 6,000 = 94,000; 94,000 - 90,000 = 4,000 taxable.
        // 4,000 x 25% = 1,000.00 on top of the regular 12,837.50 = 13,837.50.
        var result = _sut.Compute(NewEmployee(), DecemberRun(),
            includeThirteenthMonth: true, basicEarnedEarlierInYear: 968_000m,
            leaveConversion: new LeaveConversionInput(12_000m, 6_000m));

        result.ThirteenthMonth.Should().Be(88_000m);
        result.WithholdingTax.Should().Be(13_837.50m);
    }

    [Fact]
    public void Compute_with_the_exemption_already_used_taxes_all_the_other_benefits()
    {
        // 90,000 of the exemption used earlier in the year leaves none: all 6,000 is taxable.
        // 6,000 x 25% = 1,500.00 on top of the regular 12,837.50 = 14,337.50.
        var result = _sut.Compute(NewEmployee(), DecemberRun(),
            leaveConversion: new LeaveConversionInput(12_000m, 6_000m),
            otherBenefitsExemptUsedEarlierInYear: 90_000m);

        result.WithholdingTax.Should().Be(14_337.50m);
    }

    [Fact]
    public void Compute_without_the_exemption_used_falls_back_to_the_13th_month_paid_earlier()
    {
        // Today's behaviour for a caller that doesn't pass the new figure: the 13th month paid
        // earlier (90,000) is what used the exemption, so again all 6,000 is taxable - 1,500.00.
        var result = _sut.Compute(NewEmployee(), DecemberRun(),
            thirteenthMonthPaidEarlierInYear: 90_000m,
            leaveConversion: new LeaveConversionInput(12_000m, 6_000m));

        result.WithholdingTax.Should().Be(14_337.50m);
    }

    [Fact]
    public void Compute_takes_the_13th_month_due_from_the_13th_month_paid_and_the_tax_from_the_exemption_used()
    {
        // Earlier in the year: a 44,000 13th month advance and 46,000 of other benefits - 90,000 of
        // the exemption used, but only 44,000 of the 13th month paid.
        // 13th month due: (968,000 + 88,000) / 12 = 88,000 - 44,000 paid = 44,000.
        // Taxable: nothing of the exemption left, so 44,000 + 6,000 = 50,000 x 25% = 12,500.00,
        // on top of the regular 12,837.50 = 25,337.50.
        // (Falling back to the 13th month alone would leave 46,000 exempt and tax only 4,000.)
        var result = _sut.Compute(NewEmployee(), DecemberRun(),
            includeThirteenthMonth: true, basicEarnedEarlierInYear: 968_000m,
            thirteenthMonthPaidEarlierInYear: 44_000m,
            leaveConversion: new LeaveConversionInput(12_000m, 6_000m),
            otherBenefitsExemptUsedEarlierInYear: 90_000m);

        result.ThirteenthMonth.Should().Be(44_000m);
        result.WithholdingTax.Should().Be(25_337.50m);
    }

    [Fact]
    public void Compute_refuses_a_final_pay_and_a_leave_conversion_together()
    {
        var finalPay = new FinalPayExtras(
            WorkingDays: 10m,
            LeaveConversionNonTaxable: 0m,
            LeaveConversionOtherBenefits: 0m,
            SeparationPay: 0m,
            RetirementPay: 0m,
            SeparationAndRetirementNonTaxable: 0m,
            Deductions: [],
            WithholdingTaxOverride: 0m);

        var act = () => _sut.Compute(NewEmployee(), DecemberRun(), finalPay: finalPay,
            leaveConversion: new LeaveConversionInput(12_000m, 6_000m));

        act.Should().Throw<ArgumentException>();
    }

    private static EmployeeCompensation NewEmployee() => new()
    {
        BasicSalary = 88_000m,
        PayFrequency = PayFrequency.Monthly
    };

    private static PayrollRun DecemberRun() => new()
    {
        RunNumber = "PAY-2026-024",
        PeriodStart = new DateOnly(2026, 12, 1),
        PeriodEnd = new DateOnly(2026, 12, 31),
        PayDate = new DateOnly(2026, 12, 20),
        Frequency = PayFrequency.Monthly
    };
}
