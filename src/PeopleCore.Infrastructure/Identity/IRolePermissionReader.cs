namespace PeopleCore.Infrastructure.Identity;

/// <summary>What an account holding a set of roles may do, as the token issued at sign-in will say.</summary>
public interface IRolePermissionReader
{
    /// <summary>
    /// The union of the roles' permissions, in catalogue order. Every permission when the roles
    /// include Admin. Role names match case-insensitively, as Identity's do.
    /// </summary>
    Task<IReadOnlyList<string>> GetPermissionsAsync(IEnumerable<string> roleNames, CancellationToken ct = default);
}
