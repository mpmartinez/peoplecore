using FluentAssertions;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Validation;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Payroll;

public class Bir2316ManualInputsValidatorTests
{
    private static Action Validating(Bir2316ManualInputs manual)
        => () => Bir2316ManualInputsValidator.Validate(manual);

    [Fact]
    public void Validate_AcceptsTheEmptyOverlayThatPreviewUses()
    {
        // Bir2316Service.GetPreviewAsync calls BuildAsync with new Bir2316ManualInputs() - all
        // zeros and nulls. If validation rejected that, preview would break for every employee.
        Validating(new Bir2316ManualInputs()).Should().NotThrow();
    }

    [Fact]
    public void Validate_AcceptsAFullyPopulatedOverlay()
    {
        var manual = new Bir2316ManualInputs
        {
            PrevEmployerTin = "123-456-789",
            PrevEmployerName = "Previous Employer Inc.",
            PrevEmployerAddress = "123 Ayala Avenue, Makati City",
            PrevEmployerZipCode = "1200",
            Item22_PrevTaxableCompensation = 250_000m,
            Item25B_PrevTaxWithheld = 12_500m,
            Item27_PeraTaxCredit = 5_000m,
            Item33_HazardPayMwe = 10_000m,
            Item35_DeMinimis = 15_000m,
            StatutoryMinWagePerDay = 610m,
            StatutoryMinWagePerMonth = 18_500m,
        };

        Validating(manual).Should().NotThrow();
    }

    // ── Money bounds ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Item22")]
    [InlineData("Item25B")]
    [InlineData("Item27")]
    [InlineData("Item33")]
    [InlineData("Item35")]
    [InlineData("MinWageDay")]
    [InlineData("MinWageMonth")]
    public void Validate_RejectsNegativeMoneyOnEveryField(string field)
    {
        // Negative money on a tax certificate is indefensible on any of these boxes.
        Validating(WithField(field, -0.01m)).Should()
            .Throw<DomainException>().WithMessage("*negative*");
    }

    [Theory]
    [InlineData("Item22")]
    [InlineData("Item25B")]
    [InlineData("Item27")]
    [InlineData("Item33")]
    [InlineData("Item35")]
    [InlineData("MinWageDay")]
    [InlineData("MinWageMonth")]
    public void Validate_RejectsMoneyAboveTheAnnualCeiling(string field)
    {
        Validating(WithField(field, 120_000_000.01m)).Should()
            .Throw<DomainException>().WithMessage("*exceed*");
    }

    [Theory]
    [InlineData("Item22")]
    [InlineData("Item27")]
    [InlineData("Item33")]
    [InlineData("Item35")]
    [InlineData("MinWageDay")]
    [InlineData("MinWageMonth")]
    public void Validate_AcceptsMoneyExactlyAtTheAnnualCeiling(string field)
    {
        // Item25B is excluded: at the ceiling it would exceed Item22, which the cross-field rule
        // below rejects for its own reason. It is covered at the ceiling in that test instead.
        Validating(WithField(field, 120_000_000m)).Should().NotThrow();
    }

    [Fact]
    public void Validate_RejectsMoneyWithMoreThanTwoDecimalPlaces()
    {
        Validating(WithField("Item35", 1_000.005m)).Should()
            .Throw<DomainException>().WithMessage("*decimal places*");
    }

    [Fact]
    public void Validate_AcceptsMoneyWithExactlyTwoDecimalPlaces()
    {
        Validating(WithField("Item35", 1_000.05m)).Should().NotThrow();
    }

    // ── Cross-field ──────────────────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsTaxWithheldExceedingTheCompensationItCameFrom()
    {
        // The only rule here that catches a PLAUSIBLE mistake: two figures each individually
        // reasonable and jointly impossible. You cannot withhold more tax than the pay it was
        // withheld from.
        var manual = new Bir2316ManualInputs
        {
            Item22_PrevTaxableCompensation = 100_000m,
            Item25B_PrevTaxWithheld = 100_000.01m,
        };

        Validating(manual).Should()
            .Throw<DomainException>().WithMessage("*Item 25B*cannot exceed*Item 22*");
    }

    [Fact]
    public void Validate_AcceptsTaxWithheldEqualToTheCompensation()
    {
        // Absurd as a tax rate, but not impossible, and the bound is what is being pinned.
        var manual = new Bir2316ManualInputs
        {
            Item22_PrevTaxableCompensation = 120_000_000m,
            Item25B_PrevTaxWithheld = 120_000_000m,
        };

        Validating(manual).Should().NotThrow();
    }

    // ── TIN ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("123456789")]
    [InlineData("123-456-789")]
    [InlineData("123 456 789")]
    [InlineData("123456789012")]
    [InlineData("123-456-789-000")]
    public void Validate_AcceptsAWellFormedTin(string tin)
    {
        Validating(new Bir2316ManualInputs { PrevEmployerTin = tin }).Should().NotThrow();
    }

    [Theory]
    [InlineData("12345678")]       // 8 digits
    [InlineData("1234567890")]     // 10 digits
    [InlineData("12345678901234")] // 14 digits
    [InlineData("123-456-78X")]    // a letter
    public void Validate_RejectsAMalformedTin(string tin)
    {
        Validating(new Bir2316ManualInputs { PrevEmployerTin = tin }).Should()
            .Throw<DomainException>().WithMessage("*TIN*");
    }

    // ── ZIP ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_AcceptsAFourDigitZipCode()
    {
        Validating(new Bir2316ManualInputs { PrevEmployerZipCode = "1200" }).Should().NotThrow();
    }

    [Theory]
    [InlineData("120")]
    [InlineData("12000")]
    [InlineData("12A0")]
    public void Validate_RejectsAZipCodeThatIsNotFourDigits(string zip)
    {
        // Bir2316FieldMap draws this into exactly 4 digit cells.
        Validating(new Bir2316ManualInputs { PrevEmployerZipCode = zip }).Should()
            .Throw<DomainException>().WithMessage("*ZIP*4 digits*");
    }

    // ── String lengths ───────────────────────────────────────────────────────

    [Fact]
    public void Validate_RejectsAnAbsurdlyLongEmployerName()
    {
        Validating(new Bir2316ManualInputs { PrevEmployerName = new string('x', 201) }).Should()
            .Throw<DomainException>().WithMessage("*name*200 characters*");
    }

    [Fact]
    public void Validate_AcceptsAnEmployerNameExactlyAtTheLimit()
    {
        Validating(new Bir2316ManualInputs { PrevEmployerName = new string('x', 200) })
            .Should().NotThrow();
    }

    [Fact]
    public void Validate_RejectsAnAbsurdlyLongEmployerAddress()
    {
        Validating(new Bir2316ManualInputs { PrevEmployerAddress = new string('x', 201) }).Should()
            .Throw<DomainException>().WithMessage("*address*200 characters*");
    }

    [Fact]
    public void Validate_AcceptsAnEmployerAddressExactlyAtTheLimit()
    {
        Validating(new Bir2316ManualInputs { PrevEmployerAddress = new string('x', 200) })
            .Should().NotThrow();
    }

    // ── Accumulation ─────────────────────────────────────────────────────────

    [Fact]
    public void Validate_ReportsEveryProblemInOneException()
    {
        var manual = new Bir2316ManualInputs
        {
            Item22_PrevTaxableCompensation = -1m,
            Item35_DeMinimis = -1m,
            PrevEmployerTin = "nope",
            PrevEmployerZipCode = "12",
        };

        var message = Validating(manual).Should().Throw<DomainException>().Which.Message;

        message.Should().Contain("Item 22");
        message.Should().Contain("Item 35");
        message.Should().Contain("TIN");
        message.Should().Contain("ZIP");
    }

    private static Bir2316ManualInputs WithField(string field, decimal value) => field switch
    {
        "Item22" => new Bir2316ManualInputs { Item22_PrevTaxableCompensation = value },
        // Item22 is raised alongside Item25B so the cross-field rule does not fire and mask the
        // bound this test is actually pinning.
        "Item25B" => new Bir2316ManualInputs
        {
            Item22_PrevTaxableCompensation = value < 0m ? 0m : value,
            Item25B_PrevTaxWithheld = value,
        },
        "Item27" => new Bir2316ManualInputs { Item27_PeraTaxCredit = value },
        "Item33" => new Bir2316ManualInputs { Item33_HazardPayMwe = value },
        "Item35" => new Bir2316ManualInputs { Item35_DeMinimis = value },
        "MinWageDay" => new Bir2316ManualInputs { StatutoryMinWagePerDay = value },
        "MinWageMonth" => new Bir2316ManualInputs { StatutoryMinWagePerMonth = value },
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown field key."),
    };
}
