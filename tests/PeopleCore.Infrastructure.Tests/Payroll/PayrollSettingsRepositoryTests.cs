using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Application.Payroll.Services;
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

    [Fact]
    public async Task TheDefaultSettings_AreReadAndChanged_WithoutNamingTheirCompany()
    {
        // api/payroll-settings/default, through the service and the real repository: the row
        // payroll computes from changes, and nothing is added beside it.
        var company = ACompany("Zamboanga Branch");
        Context.Companies.Add(company);
        Context.PayrollSettings.Add(new PayrollSettings { CompanyId = company.Id, DailyRateFactor = 313m });
        await Context.SaveChangesAsync();

        var service = new PayrollSettingsService(Sut);
        var read = await service.GetDefaultAsync();
        read.CompanyId.Should().Be(company.Id);
        await service.UpdateDefaultAsync(read with { CompanyId = Guid.Empty, ExemptFromMaternityDifferential = true });

        await using var fresh = NewContext();
        var stored = await fresh.PayrollSettings.SingleAsync();
        stored.CompanyId.Should().Be(company.Id);
        stored.DailyRateFactor.Should().Be(313m);
        stored.ExemptFromMaternityDifferential.Should().BeTrue();
    }
}
