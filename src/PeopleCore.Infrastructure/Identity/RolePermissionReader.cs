using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Identity;

/// <inheritdoc cref="IRolePermissionReader"/>
public class RolePermissionReader : IRolePermissionReader
{
    private readonly AppDbContext _db;

    public RolePermissionReader(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<string>> GetPermissionsAsync(IEnumerable<string> roleNames, CancellationToken ct = default)
    {
        var normalized = roleNames.Select(name => name.ToUpperInvariant()).ToList();
        if (normalized.Count == 0) return [];
        if (normalized.Contains(SeededRoles.Admin.ToUpperInvariant())) return Permissions.AllKeys;

        var stored = await (from claim in _db.RoleClaims
                            join role in _db.Roles on claim.RoleId equals role.Id
                            where normalized.Contains(role.NormalizedName!) && claim.ClaimType == Permissions.ClaimType
                            select claim.ClaimValue!)
                           .Distinct()
                           .ToListAsync(ct);

        // Filtering through the catalogue fixes the order and drops any key a later release retired.
        return Permissions.AllKeys.Where(stored.Contains).ToList();
    }
}
