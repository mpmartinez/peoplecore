using FluentAssertions;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Infrastructure.Persistence.Repositories;

namespace PeopleCore.Infrastructure.Tests.Payroll;

public class Bir2316InputsRepositoryTests : DatabaseTestBase
{
    public Bir2316InputsRepositoryTests(PostgresFixture fixture) : base(fixture) { }

    private async Task<Guid> AnEmployeeIdAsync()
    {
        var employee = AnEmployee();
        Context.Employees.Add(employee);
        await Context.SaveChangesAsync();
        return employee.Id;
    }

    private static Bir2316ManualInputs Inputs(decimal prevTaxable) => new()
    {
        PrevEmployerTin = "111-222-333-000", PrevEmployerName = "Old Co.",
        Item22_PrevTaxableCompensation = prevTaxable, Item25B_PrevTaxWithheld = 1_200m
    };

    [Fact]
    public async Task Save_ThenGet_ReturnsTheInputs()
    {
        var id = await AnEmployeeIdAsync();

        await new Bir2316InputsRepository(NewContext()).SaveAsync(id, 2026, Inputs(50_000m));

        var saved = await new Bir2316InputsRepository(NewContext()).GetAsync(id, 2026);
        saved!.PrevEmployerName.Should().Be("Old Co.");
        saved.Item22_PrevTaxableCompensation.Should().Be(50_000m);
        saved.Item25B_PrevTaxWithheld.Should().Be(1_200m);
    }

    [Fact]
    public async Task Save_Twice_ReplacesRatherThanDuplicates()
    {
        var id = await AnEmployeeIdAsync();
        await new Bir2316InputsRepository(NewContext()).SaveAsync(id, 2026, Inputs(50_000m));

        await new Bir2316InputsRepository(NewContext()).SaveAsync(id, 2026, Inputs(60_000m));

        await using var reader = NewContext();
        reader.Set<PeopleCore.Domain.Entities.Payroll.Bir2316Inputs>().Count().Should().Be(1);
        (await new Bir2316InputsRepository(reader).GetAsync(id, 2026))!.Item22_PrevTaxableCompensation.Should().Be(60_000m);
    }

    [Fact]
    public async Task GetForYear_ReturnsOnlyThatYear_KeyedByEmployee()
    {
        var id = await AnEmployeeIdAsync();
        await new Bir2316InputsRepository(NewContext()).SaveAsync(id, 2025, Inputs(10_000m));
        await new Bir2316InputsRepository(NewContext()).SaveAsync(id, 2026, Inputs(20_000m));

        var year = await new Bir2316InputsRepository(NewContext()).GetForYearAsync(2026);

        year.Should().ContainSingle();
        year[id].Item22_PrevTaxableCompensation.Should().Be(20_000m);
    }

    [Fact]
    public async Task Get_WithNothingSaved_IsNull()
    {
        var id = await AnEmployeeIdAsync();

        (await new Bir2316InputsRepository(NewContext()).GetAsync(id, 2026)).Should().BeNull();
    }
}
