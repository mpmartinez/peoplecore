using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Infrastructure.Identity;

namespace PeopleCore.Infrastructure.Tests;

/// <summary>What a token will say an account may do: the union of its roles' stored permissions.</summary>
public class RolePermissionReaderTests : DatabaseTestBase
{
    public RolePermissionReaderTests(PostgresFixture fixture) : base(fixture) { }

    private RolePermissionReader Reader() => new(NewContext());

    [Fact]
    public async Task TheSeededRoles_ReadBackAsSeeded()
    {
        await new RoleSeeder(Context).SeedAsync();

        foreach (var role in SeededRoles.All.Where(r => r != SeededRoles.Admin))
            (await Reader().GetPermissionsAsync([role])).Should().Equal(SeededRoles.PermissionsOf([role]), role);
    }

    [Fact]
    public async Task SeveralRoles_GiveTheUnion_InCatalogueOrder_WithoutRepeats()
    {
        await new RoleSeeder(Context).SeedAsync();

        (await Reader().GetPermissionsAsync(["PayrollService", "Manager", "Employee"]))
            .Should().Equal(Permissions.ApprovalsTeam, Permissions.PayrollManage);
    }

    [Fact]
    public async Task Admin_HoldsTheWholeCatalogue_ThoughNothingIsStoredForIt()
    {
        await new RoleSeeder(Context).SeedAsync();

        (await Reader().GetPermissionsAsync(["Employee", "Admin"])).Should().Equal(Permissions.AllKeys);
    }

    [Fact]
    public async Task AStoredKeyTheCatalogueNoLongerHas_IsIgnored()
    {
        var role = new ApplicationRole { Name = "Legacy", NormalizedName = "LEGACY" };
        Context.Roles.Add(role);
        Context.RoleClaims.Add(new IdentityRoleClaim<string> { RoleId = role.Id, ClaimType = Permissions.ClaimType, ClaimValue = "reports.retired" });
        Context.RoleClaims.Add(new IdentityRoleClaim<string> { RoleId = role.Id, ClaimType = Permissions.ClaimType, ClaimValue = Permissions.AnalyticsHr });
        await Context.SaveChangesAsync();

        (await Reader().GetPermissionsAsync(["legacy"])).Should().Equal(Permissions.AnalyticsHr);
    }

    [Fact]
    public async Task NoRoles_MeansNoPermissions()
    {
        (await Reader().GetPermissionsAsync([])).Should().BeEmpty();
    }
}
