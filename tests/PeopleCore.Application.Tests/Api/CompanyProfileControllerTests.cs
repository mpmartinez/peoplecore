using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PeopleCore.API.Controllers.Organization;
using PeopleCore.Application.Organization.Interfaces;
using PeopleCore.Domain.Entities.Organization;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// The employer identity on payslips and BIR Form 2316. Before this endpoint nothing could set it,
/// so the 2316 employer section always printed blank.
/// </summary>
public class CompanyProfileControllerTests
{
    private readonly Mock<ICompanyRepository> _companies = new();
    private readonly Company _company = new() { Name = "My Company" };
    private readonly CompanyProfileController _sut;

    private static readonly CompanyProfileDto Valid = new(
        "Bayanihan Trading Corp.", "123-456-789-00000", "043", "12 Rizal St.", "Makati", "1200",
        "hr@example.com", "0917 000 0000", "03-1234567-8", "12-345678901-2", "1234-5678-9012");

    public CompanyProfileControllerTests()
    {
        _companies.Setup(r => r.GetDefaultAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_company);
        _sut = new CompanyProfileController(_companies.Object);
    }

    [Fact]
    public async Task Saving_writes_every_field_onto_the_company()
    {
        var result = await _sut.Save(Valid, CancellationToken.None);

        result.Result.Should().BeOfType<OkObjectResult>();
        _company.Name.Should().Be("Bayanihan Trading Corp.");
        _company.TIN.Should().Be("123-456-789-00000");
        _company.RdoCode.Should().Be("043");
        _company.Address.Should().Be("12 Rizal St.");
        _company.ZipCode.Should().Be("1200");
        _company.SSSNumber.Should().Be("03-1234567-8");
        _companies.Verify(r => r.UpdateAsync(_company, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Getting_returns_what_is_stored()
    {
        _company.TIN = "123456789";

        var dto = (await _sut.Get(CancellationToken.None)).Result
            .Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<CompanyProfileDto>().Subject;

        dto.Name.Should().Be("My Company");
        dto.Tin.Should().Be("123456789");
    }

    [Theory]
    [InlineData("", "123456789", "1200")]          // no name
    [InlineData("Acme", "12345", "1200")]          // TIN too short
    [InlineData("Acme", "123-456-789-0", "1200")]  // odd branch code
    [InlineData("Acme", "ABC-456-789", "1200")]    // letters in TIN
    [InlineData("Acme", "123456789", "12")]        // ZIP not 4 digits
    public async Task Refuses_what_would_print_wrong_on_the_2316(string name, string tin, string zip)
    {
        var result = await _sut.Save(Valid with { Name = name, Tin = tin, ZipCode = zip }, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        _companies.Verify(r => r.UpdateAsync(It.IsAny<Company>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123456789")]
    [InlineData("123-456-789-000")]
    [InlineData("123 456 789 00000")]
    public async Task Accepts_a_blank_TIN_or_one_with_or_without_its_branch_code(string tin)
    {
        (await _sut.Save(Valid with { Tin = tin }, CancellationToken.None)).Result.Should().BeOfType<OkObjectResult>();
    }
}
