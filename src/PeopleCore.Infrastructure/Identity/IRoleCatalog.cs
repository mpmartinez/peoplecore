namespace PeopleCore.Infrastructure.Identity;

/// <summary>A role as the Roles page and the granting rules see it.</summary>
/// <param name="Permissions">What the role allows, in catalogue order - every permission for Admin.</param>
/// <param name="AccountCount">How many accounts hold the role.</param>
public sealed record RoleRecord(
    string Id,
    string Name,
    string? Description,
    bool IsSystem,
    IReadOnlyList<string> Permissions,
    int AccountCount);

public interface IRoleCatalog
{
    /// <summary>Every role: system roles first (Admin, Employee, Service), then the rest by name.</summary>
    Task<IReadOnlyList<RoleRecord>> GetRolesAsync(CancellationToken ct = default);

    Task<RoleRecord?> GetAsync(string roleId, CancellationToken ct = default);
}
