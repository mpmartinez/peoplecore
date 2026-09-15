using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Identity;

/// <summary>
/// Creates any seeded role that does not exist yet, with its description, system flag and
/// permissions. A role that already exists is never touched - not even to add a permission it lacks
/// - because once roles are editable its contents are an admin's decision. Existing databases got
/// their permissions once, from the AddRolePermissions migration.
/// </summary>
public class RoleSeeder
{
    private readonly AppDbContext _db;

    public RoleSeeder(AppDbContext db) => _db = db;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        foreach (var name in SeededRoles.All)
        {
            var normalized = name.ToUpperInvariant();
            if (await _db.Roles.AnyAsync(r => r.NormalizedName == normalized, ct)) continue;

            var role = new ApplicationRole
            {
                Name = name,
                NormalizedName = normalized,
                Description = SeededRoles.Descriptions[name],
                IsSystem = SeededRoles.System.Contains(name),
            };
            _db.Roles.Add(role);

            foreach (var key in SeededRoles.StoredPermissions(name))
                _db.RoleClaims.Add(new IdentityRoleClaim<string> { RoleId = role.Id, ClaimType = Permissions.ClaimType, ClaimValue = key });
        }

        await _db.SaveChangesAsync(ct);
    }
}
