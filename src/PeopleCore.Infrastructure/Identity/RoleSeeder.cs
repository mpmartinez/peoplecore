using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Identity;

/// <summary>
/// Makes sure the roles PeopleCore relies on exist. The system roles - Admin, Employee, Service - are
/// restored whenever they are missing, because the app refers to them by name. The ordinary seeded
/// roles (HRManager, Manager, PayrollService) are created only into an empty roles table, i.e. on a
/// brand-new database: once roles are editable, a missing ordinary role is one an admin deleted, and
/// bringing it back on restart would undo that. A role that exists is never touched. Existing
/// databases got their permissions once, from the AddRolePermissions migration.
/// </summary>
public class RoleSeeder
{
    private readonly AppDbContext _db;
    private readonly ILookupNormalizer _normalizer;

    public RoleSeeder(AppDbContext db, ILookupNormalizer normalizer)
    {
        _db = db;
        _normalizer = normalizer;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var freshDatabase = !await _db.Roles.AnyAsync(ct);
        var toEnsure = freshDatabase ? SeededRoles.All : SeededRoles.System;

        foreach (var name in toEnsure)
        {
            var normalized = _normalizer.NormalizeName(name);
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
