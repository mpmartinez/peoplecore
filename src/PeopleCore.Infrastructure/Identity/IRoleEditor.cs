namespace PeopleCore.Infrastructure.Identity;

/// <summary>
/// Writes roles. Deliberately no rules here - who may change what is RoleManagementPolicy's call,
/// made before these are reached.
/// </summary>
public interface IRoleEditor
{
    /// <returns>The new role's id.</returns>
    Task<string> CreateAsync(string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken ct = default);

    /// <returns>
    /// How many accounts had their security stamp replaced: every holder when the permissions changed,
    /// none when only the name or description did.
    /// </returns>
    Task<int> UpdateAsync(string roleId, string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken ct = default);

    /// <summary>Deletes the role, replacing the security stamp of any account that still holds it in the same save.</summary>
    Task DeleteAsync(string roleId, CancellationToken ct = default);

    /// <summary>True when another role already has <paramref name="name"/>, ignoring case.</summary>
    Task<bool> NameTakenAsync(string name, string? exceptRoleId, CancellationToken ct = default);
}
