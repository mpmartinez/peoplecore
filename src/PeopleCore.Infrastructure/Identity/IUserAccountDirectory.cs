namespace PeopleCore.Infrastructure.Identity;

/// <summary>An account as the user management screens list it.</summary>
public sealed record UserAccountRow(
    string Id,
    string Email,
    string? FirstName,
    string? LastName,
    IReadOnlyList<string> Roles,
    bool IsActive,
    bool MustChangePassword,
    Guid? EmployeeId,
    string? EmployeeName);

/// <summary>Which employee a login belongs to.</summary>
public sealed record EmployeeLinkRow(Guid EmployeeId, string UserId, bool IsActive);

/// <summary>
/// Reads across accounts, their roles and their employees. Changes to an account go through
/// UserManager; these reads need joins UserManager does not offer and, unlike UserManager.Users,
/// can be stubbed in a controller test.
/// </summary>
public interface IUserAccountDirectory
{
    /// <summary>Accounts whose email, first or last name contains <paramref name="search"/>, ignoring case, ordered by email.</summary>
    Task<(IReadOnlyList<UserAccountRow> Items, int TotalCount)> SearchAsync(
        string? search, int page, int pageSize, CancellationToken ct = default);

    Task<UserAccountRow?> GetAsync(string userId, CancellationToken ct = default);

    Task<IReadOnlyList<EmployeeLinkRow>> GetEmployeeLinksAsync(CancellationToken ct = default);

    Task<int> CountActiveInRoleAsync(string role, CancellationToken ct = default);

    Task<string?> FindUserLinkedToEmployeeAsync(Guid employeeId, CancellationToken ct = default);
}
