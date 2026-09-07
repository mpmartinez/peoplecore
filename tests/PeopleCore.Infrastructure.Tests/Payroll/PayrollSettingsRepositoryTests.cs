using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Payroll;

public class PayrollSettingsRepositoryTests : DatabaseTestBase
{
    public PayrollSettingsRepositoryTests(PostgresFixture fixture) : base(fixture) { }

    private PayrollSettingsRepository Sut => new(Context);

    [Fact]
    public async Task GetDefault_ReturnsNullWhenNoSettingsExist()
    {
        (await Sut.GetDefaultAsync()).Should().BeNull();
    }

    [Fact]
    public async Task GetDefault_ReturnsTheOnlyRow()
    {
        var company = ACompany();
        Context.Companies.Add(company);
        Context.PayrollSettings.Add(new PayrollSettings { CompanyId = company.Id, DailyRateFactor = 313m });
        await Context.SaveChangesAsync();

        var settings = await Sut.GetDefaultAsync();

        settings.Should().NotBeNull();
        settings!.DailyRateFactor.Should().Be(313m);
    }

    [Fact]
    public async Task GetDefault_ThrowsWhenASecondCompanyHasSettings()
    {
        // The whole multi-company safety story. PayrollRun carries no CompanyId, so rate
        // resolution would otherwise use whichever row EF returned first - and a second company's
        // DailyRateFactor or SSS overrides would wrong every computed wage with no error at all.
        // Failing loudly is the deliberate behaviour; this is the first test to prove it does.
        var first = ACompany("First Company");
        var second = ACompany("Second Company");
        Context.Companies.AddRange(first, second);
        Context.PayrollSettings.AddRange(
            new PayrollSettings { CompanyId = first.Id },
            new PayrollSettings { CompanyId = second.Id });
        await Context.SaveChangesAsync();

        var act = async () => await Sut.GetDefaultAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*PayrollRun carries no CompanyId*");
    }

    [Fact]
    public async Task TheUniqueIndexOnCompanyIdIsEnforcedByTheDatabase()
    {
        // Declared by UniquePayrollSettingsCompany. A configuration class saying IsUnique proves
        // nothing until a migration has actually built the index.
        var company = ACompany();
        Context.Companies.Add(company);
        Context.PayrollSettings.Add(new PayrollSettings { CompanyId = company.Id });
        await Context.SaveChangesAsync();

        await using var second = NewContext();
        second.PayrollSettings.Add(new PayrollSettings { CompanyId = company.Id });

        var act = async () => await second.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task GetByCompanyId_ReturnsThatCompanysSettings()
    {
        var first = ACompany("First Company");
        var second = ACompany("Second Company");
        Context.Companies.AddRange(first, second);
        Context.PayrollSettings.AddRange(
            new PayrollSettings { CompanyId = first.Id, DailyRateFactor = 313m },
            new PayrollSettings { CompanyId = second.Id, DailyRateFactor = 261m });
        await Context.SaveChangesAsync();

        var settings = await Sut.GetByCompanyIdAsync(second.Id);

        settings!.DailyRateFactor.Should().Be(261m);
    }
}
