using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Identity;

/// <inheritdoc cref="IRoleEditor"/>
public class RoleEditor : IRoleEditor
{
    private readonly AppDbContext _db;

    public RoleEditor(AppDbContext db) => _db = db;

    public async Task<string> CreateAsync(string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken ct = default)
    {
        var role = new ApplicationRole { Name = name, NormalizedName = name.ToUpperInvariant(), Description = description };
        _db.Roles.Add(role);
        AddPermissions(role.Id, permissions);
        await _db.SaveChangesAsync(ct);
        return role.Id;
    }

    public async Task<int> UpdateAsync(string roleId, string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken ct = default)
    {
        var role = await _db.Roles.SingleAsync(r => r.Id == roleId, ct);
        role.Name = name;
        role.NormalizedName = name.ToUpperInvariant();
        role.Description = description;

        var stored = await _db.RoleClaims.Where(c => c.RoleId == roleId && c.ClaimType == Permissions.ClaimType).ToListAsync(ct);
        var unchanged = stored.Select(c => c.ClaimValue!).ToHashSet().SetEquals(permissions);

        var signedOut = 0;
        if (!unchanged)
        {
            _db.RoleClaims.RemoveRange(stored);
            AddPermissions(roleId, permissions);

            // Every holder's token still lists the old permissions. Replacing the stamps in this same
            // save means the permission change and the revocation land together or not at all.
            var holderIds = _db.UserRoles.Where(ur => ur.RoleId == roleId).Select(ur => ur.UserId);
            var holders = await _db.Users.Where(u => holderIds.Contains(u.Id)).ToListAsync(ct);
            foreach (var holder in holders)
            {
                holder.SecurityStamp = Guid.NewGuid().ToString("N");
                holder.ConcurrencyStamp = Guid.NewGuid().ToString();
            }
            signedOut = holders.Count;
        }

        await _db.SaveChangesAsync(ct);
        return signedOut;
    }

    public async Task DeleteAsync(string roleId, CancellationToken ct = default)
    {
        var role = await _db.Roles.SingleAsync(r => r.Id == roleId, ct);
        _db.RoleClaims.RemoveRange(await _db.RoleClaims.Where(c => c.RoleId == roleId).ToListAsync(ct));
        _db.Roles.Remove(role);
        await _db.SaveChangesAsync(ct);
    }

    public Task<bool> NameTakenAsync(string name, string? exceptRoleId, CancellationToken ct = default)
    {
        var normalized = name.ToUpperInvariant();
        return _db.Roles.AnyAsync(r => r.NormalizedName == normalized && r.Id != exceptRoleId, ct);
    }

    private void AddPermissions(string roleId, IEnumerable<string> permissions)
    {
        foreach (var key in permissions.Distinct())
            _db.RoleClaims.Add(new IdentityRoleClaim<string> { RoleId = roleId, ClaimType = Permissions.ClaimType, ClaimValue = key });
    }
}
