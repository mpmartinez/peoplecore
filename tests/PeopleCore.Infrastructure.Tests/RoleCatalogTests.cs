using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Infrastructure.Identity;

namespace PeopleCore.Infrastructure.Tests;

/// <summary>Every role as the Roles page and the granting rules see it, read from Postgres.</summary>
public class RoleCatalogTests : DatabaseTestBase
{
    public RoleCatalogTests(PostgresFixture fixture) : base(fixture) { }

    private RoleCatalog Catalog() => new(NewContext());

    private async Task<ApplicationUser> AccountWithAsync(params string[] roleNames)
    {
        var email = Guid.NewGuid().ToString("N") + "@example.com";
        var user = new ApplicationUser { UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant() };
        Context.Users.Add(user);
        foreach (var name in roleNames)
        {
            var role = Context.Roles.Single(r => r.Name == name);
            Context.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = role.Id });
        }
        await Context.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task Roles_ListSystemRolesFirst_ThenTheRestByName()
    {
        await new RoleSeeder(Context, new UpperInvariantLookupNormalizer()).SeedAsync();
        Context.Roles.Add(new ApplicationRole { Name = "Auditor", NormalizedName = "AUDITOR" });
        await Context.SaveChangesAsync();

        (await Catalog().GetRolesAsync()).Select(r => r.Name)
            .Should().Equal("Admin", "Employee", "Service", "Auditor", "HRManager", "Manager", "PayrollService");
    }

    [Fact]
    public async Task EachRole_CarriesItsEffectivePermissions_WithAdminHoldingEverything()
    {
        await new RoleSeeder(Context, new UpperInvariantLookupNormalizer()).SeedAsync();

        var roles = (await Catalog().GetRolesAsync()).ToDictionary(r => r.Name);

        roles["Admin"].Permissions.Should().Equal(Permissions.AllKeys);
        roles["Manager"].Permissions.Should().Equal(Permissions.ApprovalsTeam);
        roles["Employee"].Permissions.Should().BeEmpty();
        roles["HRManager"].Permissions.Should().Equal(SeededRoles.PermissionsOf(["HRManager"]));
        roles["Admin"].IsSystem.Should().BeTrue();
        roles["HRManager"].Description.Should().Be(SeededRoles.Descriptions["HRManager"]);
    }

    [Fact]
    public async Task EachRole_CountsTheAccountsHoldingIt()
    {
        await new RoleSeeder(Context, new UpperInvariantLookupNormalizer()).SeedAsync();
        await AccountWithAsync("Employee", "Manager");
        await AccountWithAsync("Employee");

        var roles = (await Catalog().GetRolesAsync()).ToDictionary(r => r.Name);

        roles["Employee"].AccountCount.Should().Be(2);
        roles["Manager"].AccountCount.Should().Be(1);
        roles["HRManager"].AccountCount.Should().Be(0);
    }

    [Fact]
    public async Task OneRole_IsFoundById_OrNull()
    {
        await new RoleSeeder(Context, new UpperInvariantLookupNormalizer()).SeedAsync();
        var manager = Context.Roles.Single(r => r.Name == "Manager");

        (await Catalog().GetAsync(manager.Id))!.Name.Should().Be("Manager");
        (await Catalog().GetAsync("no-such-role")).Should().BeNull();
    }
}
