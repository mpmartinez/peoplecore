using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace PeopleCore.Infrastructure.Tests;

public class HarnessTests : DatabaseTestBase
{
    public HarnessTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task TheMigrationChainAppliesCleanly()
    {
        // Nothing else in this repository proves the ten migrations apply to an empty database.
        // If this fails, no other test in the project is meaningful.
        var applied = await Context.Database.GetAppliedMigrationsAsync();
        var pending = await Context.Database.GetPendingMigrationsAsync();

        applied.Should().NotBeEmpty();
        pending.Should().BeEmpty("the test database's schema should be fully migrated");
    }

    [Fact]
    public async Task EachTestStartsFromAnEmptyDatabase()
    {
        Context.Employees.Add(AnEmployee());
        await Context.SaveChangesAsync();

        (await Context.Employees.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task TheDatabaseIsEmptyAgainForTheNextTest()
    {
        // Paired with the test above. Whichever runs second proves the reset actually happened -
        // without it, one of these two must fail, and a silent cross-test bleed becomes visible
        // here rather than as a mysterious count in a repository test.
        (await Context.Employees.CountAsync()).Should().Be(0);

        Context.Employees.Add(AnEmployee());
        await Context.SaveChangesAsync();

        (await Context.Employees.CountAsync()).Should().Be(1);
    }
}
