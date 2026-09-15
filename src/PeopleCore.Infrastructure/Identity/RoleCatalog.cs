using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Identity;

/// <inheritdoc cref="IRoleCatalog"/>
public class RoleCatalog : IRoleCatalog
{
    private readonly AppDbContext _db;

    public RoleCatalog(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<RoleRecord>> GetRolesAsync(CancellationToken ct = default)
    {
        var roles = await _db.Roles.AsNoTracking().ToListAsync(ct);
        var records = await ToRecordsAsync(roles, ct);

        return records
            .OrderBy(r => r.IsSystem ? SeededRoles.System.ToList().IndexOf(r.Name) : int.MaxValue)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<RoleRecord?> GetAsync(string roleId, CancellationToken ct = default)
    {
        var role = await _db.Roles.AsNoTracking().SingleOrDefaultAsync(r => r.Id == roleId, ct);
        return role is null ? null : (await ToRecordsAsync([role], ct))[0];
    }

    // Two batched queries for all the roles - their permission claims and their holder counts.
    private async Task<IReadOnlyList<RoleRecord>> ToRecordsAsync(IReadOnlyList<ApplicationRole> roles, CancellationToken ct)
    {
        var roleIds = roles.Select(r => r.Id).ToList();

        var claims = await _db.RoleClaims.AsNoTracking()
            .Where(c => roleIds.Contains(c.RoleId) && c.ClaimType == Permissions.ClaimType)
            .Select(c => new { c.RoleId, c.ClaimValue })
            .ToListAsync(ct);

        var counts = await _db.UserRoles.AsNoTracking()
            .Where(ur => roleIds.Contains(ur.RoleId))
            .GroupBy(ur => ur.RoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.RoleId, g => g.Count, ct);

        return roles.Select(role =>
        {
            // Admin's permissions are a rule, not data: nothing is stored for it.
            var permissions = role.Name == SeededRoles.Admin
                ? Permissions.AllKeys
                : Permissions.AllKeys.Where(key => claims.Any(c => c.RoleId == role.Id && c.ClaimValue == key)).ToList();

            return new RoleRecord(role.Id, role.Name!, role.Description, role.IsSystem, permissions, counts.GetValueOrDefault(role.Id));
        }).ToList();
    }
}
