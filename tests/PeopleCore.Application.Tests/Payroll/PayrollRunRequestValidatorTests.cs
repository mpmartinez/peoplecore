using FluentAssertions;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Validation;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class PayrollRunRequestValidatorTests
{
    private static readonly Guid AnEmployee = Guid.NewGuid();

    private static CreatePayrollRunRequest Valid(
        DateOnly? periodStart = null,
        DateOnly? periodEnd = null,
        DateOnly? payDate = null,
        PayFrequency frequency = PayFrequency.SemiMonthly,
        IReadOnlyList<PayrollRunEmployeeInput>? employees = null)
        => new(
            periodStart ?? new DateOnly(2026, 1, 1),
            periodEnd ?? new DateOnly(2026, 1, 15),
            payDate ?? new DateOnly(2026, 1, 20),
            frequency,
            employees ?? [new PayrollRunEmployeeInput(AnEmployee)]);

    private static Action Validating(CreatePayrollRunRequest request)
        => () => PayrollRunRequestValidator.Validate(request);

    [Fact]
    public void Validate_AcceptsAnOrdinaryRequest()
    {
        Validating(Valid()).Should().NotThrow();
    }

    // ── Employees ────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsARunWithNoEmployees()
    {
        Validating(Valid(employees: [])).Should()
            .Throw<DomainException>().WithMessage("*at least one employee*");
    }

    [Fact]
    public void Validate_RejectsTheSameEmployeeTwice()
    {
        // The same employee twice in one run pays them twice.
        Validating(Valid(employees: [new PayrollRunEmployeeInput(AnEmployee), new PayrollRunEmployeeInput(AnEmployee)]))
            .Should().Throw<DomainException>().WithMessage("*only once*");
    }

    [Fact]
    public void Validate_AcceptsTwoDifferentEmployees()
    {
        Validating(Valid(employees: [new PayrollRunEmployeeInput(AnEmployee), new PayrollRunEmployeeInput(Guid.NewGuid())]))
            .Should().NotThrow();
    }

    // ── Dates ────────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsAPeriodThatEndsBeforeItStarts()
    {
        Validating(Valid(periodStart: new DateOnly(2026, 1, 15), periodEnd: new DateOnly(2026, 1, 1)))
            .Should().Throw<DomainException>().WithMessage("*Period end*before period start*");
    }

    [Fact]
    public void Validate_AcceptsASingleDayPeriod()
    {
        Validating(Valid(periodStart: new DateOnly(2026, 1, 1), periodEnd: new DateOnly(2026, 1, 1)))
            .Should().NotThrow();
    }

    [Fact]
    public void Validate_RejectsAPayDateBeforeThePeriodStarts()
    {
        Validating(Valid(payDate: new DateOnly(2025, 12, 31)))
            .Should().Throw<DomainException>().WithMessage("*Pay date*before period start*");
    }

    [Fact]
    public void Validate_AcceptsAPayDateInsideThePeriod()
    {
        // Paying before the period closes is legitimate - advances are real - so the bound stops
        // at PeriodStart rather than PeriodEnd.
        Validating(Valid(payDate: new DateOnly(2026, 1, 10))).Should().NotThrow();
    }

    // ── Period length against frequency ──────────────────────────────────────

    [Fact]
    public void Validate_RejectsASemiMonthlyPeriodLongerThanSixteenDays()
    {
        // A frequency contradicting its own date range is a data-entry error, and the 2316
        // attributes income by pay date - so a 300-day "semi-monthly" run corrupts a tax year.
        Validating(Valid(
                periodStart: new DateOnly(2026, 1, 1),
                periodEnd: new DateOnly(2026, 1, 17),
                frequency: PayFrequency.SemiMonthly))
            .Should().Throw<DomainException>().WithMessage("*SemiMonthly*16 days*");
    }

    [Fact]
    public void Validate_AcceptsASemiMonthlyPeriodOfExactlySixteenDays()
    {
        Validating(Valid(
                periodStart: new DateOnly(2026, 1, 1),
                periodEnd: new DateOnly(2026, 1, 16),
                frequency: PayFrequency.SemiMonthly))
            .Should().NotThrow();
    }

    [Fact]
    public void Validate_RejectsAMonthlyPeriodLongerThanThirtyOneDays()
    {
        Validating(Valid(
                periodStart: new DateOnly(2026, 1, 1),
                periodEnd: new DateOnly(2026, 2, 1),
                frequency: PayFrequency.Monthly))
            .Should().Throw<DomainException>().WithMessage("*Monthly*31 days*");
    }

    [Fact]
    public void Validate_AcceptsAMonthlyPeriodOfExactlyThirtyOneDays()
    {
        Validating(Valid(
                periodStart: new DateOnly(2026, 1, 1),
                periodEnd: new DateOnly(2026, 1, 31),
                frequency: PayFrequency.Monthly))
            .Should().NotThrow();
    }

    [Fact]
    public void Validate_RejectsAnUndefinedFrequency()
    {
        Validating(Valid(frequency: (PayFrequency)99)).Should()
            .Throw<DomainException>().WithMessage("*Pay frequency*");
    }

    // ── Per-employee overrides ───────────────────────────────────────────────

    [Fact]
    public void Validate_AcceptsOmittedOverrides()
    {
        // Null means "use what the attendance bridge derived". Validation must not disturb that -
        // treating a missing override as zero is the exact failure the bridge exists to prevent.
        Validating(Valid(employees: [new PayrollRunEmployeeInput(AnEmployee)])).Should().NotThrow();
    }

    [Theory]
    // Every literal must be written as a double. The parameters are double?, and xUnit boxes a
    // bare integer literal as Int32, which cannot be unboxed to Nullable<Double> - the test then
    // fails on invocation rather than on the rule it is meant to exercise.
    [InlineData(-0.5, null, null, "Days worked")]
    [InlineData(32.0, null, null, "Days worked")]
    [InlineData(null, -1.0, null, "Overtime hours")]
    [InlineData(null, 745.0, null, "Overtime hours")]
    [InlineData(null, null, -1.0, "Holiday days")]
    [InlineData(null, null, 32.0, "Holiday days")]
    public void Validate_RejectsOutOfRangeOverrides(
        double? daysWorked, double? overtimeHours, double? holidayDays, string expected)
    {
        var input = new PayrollRunEmployeeInput(
            AnEmployee,
            DaysWorked: (decimal?)daysWorked,
            OvertimeHours: (decimal?)overtimeHours,
            HolidayDays: (decimal?)holidayDays);

        Validating(Valid(employees: [input])).Should()
            .Throw<DomainException>().WithMessage($"*{expected}*");
    }

    [Fact]
    public void Validate_AcceptsOverridesExactlyAtTheirBounds()
    {
        var input = new PayrollRunEmployeeInput(
            AnEmployee, DaysWorked: 31m, OvertimeHours: 744m, HolidayDays: 31m);

        Validating(Valid(
                periodStart: new DateOnly(2026, 1, 1),
                periodEnd: new DateOnly(2026, 1, 31),
                frequency: PayFrequency.Monthly,
                employees: [input]))
            .Should().NotThrow();
    }

    [Fact]
    public void Validate_AcceptsZeroOverrides()
    {
        var input = new PayrollRunEmployeeInput(
            AnEmployee, DaysWorked: 0m, OvertimeHours: 0m, HolidayDays: 0m);

        Validating(Valid(employees: [input])).Should().NotThrow();
    }

    // ── Accumulation ─────────────────────────────────────────────────────────

    [Fact]
    public void Validate_ReportsEveryProblemInOneException()
    {
        var request = new CreatePayrollRunRequest(
            new DateOnly(2026, 1, 15),
            new DateOnly(2026, 1, 1),
            new DateOnly(2025, 12, 1),
            (PayFrequency)99,
            []);

        var message = Validating(request).Should().Throw<DomainException>().Which.Message;

        message.Should().Contain("Period end");
        message.Should().Contain("Pay date");
        message.Should().Contain("Pay frequency");
        message.Should().Contain("at least one employee");
    }
}
