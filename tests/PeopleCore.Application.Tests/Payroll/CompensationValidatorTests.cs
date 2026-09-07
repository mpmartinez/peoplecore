using FluentAssertions;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Validation;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class CompensationValidatorTests
{
    private static UpsertCompensationRequest Valid(
        decimal basicSalary = 30_000m,
        PayFrequency frequency = PayFrequency.SemiMonthly,
        string taxCode = "ME",
        int dependents = 0)
        => new(basicSalary, frequency, taxCode, dependents);

    private static Action Validating(UpsertCompensationRequest request)
        => () => CompensationValidator.Validate(request);

    [Fact]
    public void Validate_AcceptsAnOrdinaryRequest()
    {
        Validating(Valid()).Should().NotThrow();
    }

    // ── BasicSalary ──────────────────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsNegativeBasicSalary()
    {
        Validating(Valid(basicSalary: -0.01m)).Should()
            .Throw<DomainException>().WithMessage("*Basic salary*negative*");
    }

    [Fact]
    public void Validate_AcceptsZeroBasicSalary()
    {
        // An unpaid position is possible; only a negative salary is impossible.
        Validating(Valid(basicSalary: 0m)).Should().NotThrow();
    }

    [Fact]
    public void Validate_RejectsBasicSalaryAboveTheCeiling()
    {
        Validating(Valid(basicSalary: 10_000_000.01m)).Should()
            .Throw<DomainException>().WithMessage("*Basic salary*exceed*");
    }

    [Fact]
    public void Validate_AcceptsBasicSalaryExactlyAtTheCeiling()
    {
        // Pins the bound from inside. Without this, the ceiling could drift down unnoticed.
        Validating(Valid(basicSalary: 10_000_000m)).Should().NotThrow();
    }

    [Fact]
    public void Validate_RejectsBasicSalaryWithMoreThanTwoDecimalPlaces()
    {
        // The column is numeric(18,2); a third decimal is silently rounded on save today, so the
        // stored salary differs from the submitted one with no error anywhere.
        Validating(Valid(basicSalary: 30_000.123m)).Should()
            .Throw<DomainException>().WithMessage("*Basic salary*decimal places*");
    }

    [Fact]
    public void Validate_AcceptsBasicSalaryWithExactlyTwoDecimalPlaces()
    {
        Validating(Valid(basicSalary: 30_000.12m)).Should().NotThrow();
    }

    // ── PayFrequency ─────────────────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsAnUndefinedPayFrequency()
    {
        // JSON binds an out-of-range number to an enum without complaint, and
        // PayrollComputationService treats anything that is not SemiMonthly as monthly - so an
        // unrecognised value silently doubles a period's pay.
        Validating(Valid(frequency: (PayFrequency)99)).Should()
            .Throw<DomainException>().WithMessage("*Pay frequency*");
    }

    [Theory]
    [InlineData(PayFrequency.Monthly)]
    [InlineData(PayFrequency.SemiMonthly)]
    public void Validate_AcceptsEveryDefinedPayFrequency(PayFrequency frequency)
    {
        Validating(Valid(frequency: frequency)).Should().NotThrow();
    }

    // ── Dependents ───────────────────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsNegativeDependents()
    {
        Validating(Valid(dependents: -1)).Should()
            .Throw<DomainException>().WithMessage("*Dependents*negative*");
    }

    [Fact]
    public void Validate_RejectsDependentsAboveTheCeiling()
    {
        Validating(Valid(dependents: 21)).Should()
            .Throw<DomainException>().WithMessage("*Dependents*exceed*");
    }

    [Fact]
    public void Validate_AcceptsDependentsExactlyAtTheCeiling()
    {
        Validating(Valid(dependents: 20)).Should().NotThrow();
    }

    // ── TaxCode ──────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsAnEmptyTaxCode()
    {
        Validating(Valid(taxCode: "   ")).Should()
            .Throw<DomainException>().WithMessage("*Tax code is required*");
    }

    [Fact]
    public void Validate_RejectsATaxCodeLongerThanTheColumn()
    {
        // EmployeeCompensationConfiguration sets HasMaxLength(8); failing here means failing as
        // validation rather than as a database error.
        Validating(Valid(taxCode: "123456789")).Should()
            .Throw<DomainException>().WithMessage("*Tax code*8 characters*");
    }

    [Fact]
    public void Validate_AcceptsATaxCodeExactlyAtTheColumnLength()
    {
        Validating(Valid(taxCode: "12345678")).Should().NotThrow();
    }

    [Fact]
    public void Validate_AcceptsAnUnrecognisedTaxCodeString()
    {
        // Deliberately NOT checked against S/ME/S1..ME4. Nothing reads TaxCode, and TRAIN
        // (RA 10963, 2018) abolished the personal and additional exemptions those codes encoded,
        // so a whitelist would enforce an obsolete rule with no consequence - and would imply to
        // the next reader that the field still drives withholding.
        Validating(Valid(taxCode: "Z9")).Should().NotThrow();
    }

    // ── Accumulation ─────────────────────────────────────────────────────────

    [Fact]
    public void Validate_ReportsEveryProblemInOneException()
    {
        // The requirement most likely to be quietly lost to a later refactor that returns early.
        // Correcting a form one field per round trip is what makes validation resented.
        var request = new UpsertCompensationRequest(-1m, (PayFrequency)99, "", -1);

        var message = Validating(request).Should().Throw<DomainException>().Which.Message;

        message.Should().Contain("Basic salary");
        message.Should().Contain("Pay frequency");
        message.Should().Contain("Tax code");
        message.Should().Contain("Dependents");
    }
}
