using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Infrastructure.Identity;
using PeopleCore.Infrastructure.Persistence.Migrations;

namespace PeopleCore.Infrastructure.Tests;

/// <summary>
/// Existing roles get their permissions from the migration; roles created on a fresh database get
/// them from RoleSeeder at the moment of creation. Both must hand out exactly what SeededRoles says,
/// and neither may ever touch a role that already exists.
/// </summary>
public class RoleSeedingTests : DatabaseTestBase
{
    public RoleSeedingTests(PostgresFixture fixture) : base(fixture) { }

    private async Task<Dictionary<string, List<string>>> StoredPermissionsByRoleAsync()
    {
        await using var read = NewContext();
        var rows = await (from claim in read.RoleClaims
                          join role in read.Roles on claim.RoleId equals role.Id
                          where claim.ClaimType == Permissions.ClaimType
                          select new { role.Name, claim.ClaimValue }).ToListAsync();
        return rows.GroupBy(r => r.Name!).ToDictionary(g => g.Key, g => g.Select(r => r.ClaimValue!).Order().ToList());
    }

    private static Dictionary<string, List<string>> Expected() =>
        SeededRoles.All
            .Where(r => SeededRoles.StoredPermissions(r).Count > 0)
            .ToDictionary(r => r, r => SeededRoles.StoredPermissions(r).Order().ToList());

    private async Task<Dictionary<string, string?>> StoredDescriptionsByRoleAsync()
    {
        await using var read = NewContext();
        var roles = await read.Roles.ToListAsync();
        return roles.ToDictionary(r => r.Name!, r => r.Description);
    }

    [Fact]
    public async Task OnAFreshDatabase_TheSeederCreatesEveryRole_WithItsPermissions_AndMarksTheSystemRoles()
    {
        await new RoleSeeder(Context, new UpperInvariantLookupNormalizer()).SeedAsync();

        await using var read = NewContext();
        var roles = await read.Roles.ToListAsync();
        roles.Select(r => r.Name).Should().BeEquivalentTo(SeededRoles.All);
        roles.Where(r => r.IsSystem).Select(r => r.Name).Should().BeEquivalentTo("Admin", "Employee", "Service");
        roles.Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r.Description));
        (await StoredPermissionsByRoleAsync()).Should().BeEquivalentTo(Expected());
        foreach (var role in roles)
            role.Description.Should().Be(SeededRoles.Descriptions[role.Name!], $"{role.Name}'s description must match SeededRoles");
    }

    [Fact]
    public async Task TheSeeder_NeverTouchesARoleThatAlreadyExists()
    {
        // An admin who has emptied Manager's permissions must find it still empty after a restart.
        Context.Roles.Add(new ApplicationRole { Name = "Manager", NormalizedName = "MANAGER" });
        await Context.SaveChangesAsync();

        await new RoleSeeder(Context, new UpperInvariantLookupNormalizer()).SeedAsync();

        (await StoredPermissionsByRoleAsync()).Should().NotContainKey("Manager");
    }

    [Fact]
    public async Task ADeletedOrdinaryRole_IsNotRecreated_OnTheNextStartup()
    {
        // Once roles can be deleted, recreating HRManager on restart would hand its 13 permissions -
        // users.manage among them - back to a role an admin deliberately removed.
        await new RoleSeeder(Context, new UpperInvariantLookupNormalizer()).SeedAsync();
        await using (var delete = NewContext())
        {
            var hr = await delete.Roles.SingleAsync(r => r.NormalizedName == "HRMANAGER");
            delete.RoleClaims.RemoveRange(delete.RoleClaims.Where(c => c.RoleId == hr.Id));
            delete.Roles.Remove(hr);
            await delete.SaveChangesAsync();
        }

        await new RoleSeeder(NewContext(), new UpperInvariantLookupNormalizer()).SeedAsync();

        await using var read = NewContext();
        (await read.Roles.AnyAsync(r => r.NormalizedName == "HRMANAGER")).Should().BeFalse();
    }

    [Fact]
    public async Task AMissingSystemRole_IsAlwaysRestored()
    {
        // The app relies on Admin, Employee and Service by name; they are not an admin's to remove.
        Context.Roles.Add(new ApplicationRole { Name = "Recruiter", NormalizedName = "RECRUITER" });
        await Context.SaveChangesAsync();

        await new RoleSeeder(Context, new UpperInvariantLookupNormalizer()).SeedAsync();

        await using var read = NewContext();
        var names = await read.Roles.Select(r => r.Name).ToListAsync();
        names.Should().Contain(["Admin", "Employee", "Service", "Recruiter"]);
        names.Should().NotContain(["HRManager", "Manager", "PayrollService"], "ordinary roles are seeded only into an empty table");
        (await read.Roles.Where(r => r.IsSystem).Select(r => r.Name).ToListAsync()).Should().BeEquivalentTo("Admin", "Employee", "Service");
    }

    [Fact]
    public async Task OnAnExistingDatabase_TheMigrationSql_GivesTheExistingRolesTheirPermissions()
    {
        foreach (var name in SeededRoles.All)
            Context.Roles.Add(new ApplicationRole { Name = name, NormalizedName = name.ToUpperInvariant() });
        await Context.SaveChangesAsync();

        await Context.Database.ExecuteSqlRawAsync(AddRolePermissions.SeedExistingRolesSql);
        await Context.Database.ExecuteSqlRawAsync(AddRolePermissions.SeedExistingRolesSql);

        (await StoredPermissionsByRoleAsync()).Should().BeEquivalentTo(Expected(), "the migration and SeededRoles must agree, and running it twice adds nothing");
        await using var read = NewContext();
        (await read.Roles.Where(r => r.IsSystem).Select(r => r.Name).ToListAsync())
            .Should().BeEquivalentTo("Admin", "Employee", "Service");

        var descriptions = await StoredDescriptionsByRoleAsync();
        foreach (var name in SeededRoles.All)
            descriptions[name].Should().Be(SeededRoles.Descriptions[name], $"{name}'s description must match SeededRoles");
    }
}
