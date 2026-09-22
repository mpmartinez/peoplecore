using FluentAssertions;
using PeopleCore.Application.Employees.Coe;
using Xunit;

namespace PeopleCore.Application.Tests.Employees;

public class CoeContentTests
{
    private static CoeFacts Facts(DateOnly? lastDay = null, decimal? salary = 35_000m) => new(
        "Juan Santos Cruz", "Payroll Officer", new DateOnly(2021, 3, 1), lastDay,
        "Acme Inc.", "123 Ayala Ave, Makati", null, salary, null, "Maria Reyes");

    private static readonly DateOnly Today = new(2026, 9, 22);

    [Fact]
    public void ACurrentEmployee_IsEmployedToPresent()
    {
        var c = CoeContent.Build(Facts(), new CoeRequest(null, null, null, false), Today);

        c.Title.Should().Be("CERTIFICATE OF EMPLOYMENT");
        c.Paragraphs[0].Should().Be(
            "This is to certify that Juan Santos Cruz has been employed by Acme Inc. as Payroll Officer from March 1, 2021 to present.");
        c.Paragraphs.Last().Should().Be(
            "This certification is issued upon the request of the employee for whatever legal purpose it may serve.");
        c.DateLine.Should().Be("Issued on September 22, 2026.");
        c.SignatoryName.Should().Be("Maria Reyes");
        c.SignatoryTitle.Should().Be("HR Manager");
    }

    [Fact]
    public void AFormerEmployee_WasEmployedToTheirLastWorkingDay()
    {
        var c = CoeContent.Build(Facts(lastDay: new DateOnly(2026, 8, 31)), new CoeRequest(null, null, null, false), Today);

        c.Paragraphs[0].Should().Be(
            "This is to certify that Juan Santos Cruz was employed by Acme Inc. as Payroll Officer from March 1, 2021 to August 31, 2026.");
    }

    [Fact]
    public void TheSalarySentence_IsAddedOnlyWhenAskedFor()
    {
        CoeContent.Build(Facts(), new CoeRequest(null, null, null, false), Today).Paragraphs.Should().HaveCount(2);

        var withSalary = CoeContent.Build(Facts(), new CoeRequest(null, null, null, true), Today);

        withSalary.Paragraphs.Should().HaveCount(3);
        withSalary.Paragraphs[1].Should().Be("They received a monthly basic salary of ₱35,000.00.");
    }

    [Fact]
    public void PurposeAndSignatory_CanBeChanged()
    {
        var c = CoeContent.Build(Facts(), new CoeRequest("  For a bank loan application.  ", "Ana Lim", "HR Director", false), Today);

        c.Paragraphs.Last().Should().Be("For a bank loan application.");
        c.SignatoryName.Should().Be("Ana Lim");
        c.SignatoryTitle.Should().Be("HR Director");
    }

    [Fact]
    public void WithNoPosition_TheSentenceOmitsIt()
    {
        var c = CoeContent.Build(Facts() with { Position = null }, new CoeRequest(null, null, null, false), Today);

        c.Paragraphs[0].Should().Be("This is to certify that Juan Santos Cruz has been employed by Acme Inc. from March 1, 2021 to present.");
    }

    [Fact]
    public void AskingForSalary_WithNoneOnRecord_IsRefused()
    {
        var act = () => CoeContent.Build(Facts(salary: null), new CoeRequest(null, null, null, true), Today);

        act.Should().Throw<PeopleCore.Domain.Exceptions.DomainException>().WithMessage("*no salary on record*");
    }
}
