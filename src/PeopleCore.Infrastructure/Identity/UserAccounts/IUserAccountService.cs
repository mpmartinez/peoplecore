using PeopleCore.Application.Common.DTOs;

namespace PeopleCore.Infrastructure.Identity.UserAccounts;

/// <summary>
/// Administration of other people's sign-in accounts. Lives beside Identity rather than in the
/// Application layer because <see cref="ApplicationUser"/> does.
/// </summary>
public interface IUserAccountService
{
    Task<PagedResult<UserAccountDto>> ListAsync(string? search, int page, int pageSize, CancellationToken ct = default);

    Task<UserAccountDto?> GetAsync(string id, CancellationToken ct = default);

    /// <summary>Every role an account may be given, by name.</summary>
    Task<IReadOnlyList<string>> GetRoleNamesAsync(CancellationToken ct = default);

    Task<UserAccountResult> CreateAsync(CreateUserAccountRequest request, CancellationToken ct = default);

    /// <param name="callerId">The account making the change, which may not strip its own Admin role.</param>
    Task<UserAccountResult> UpdateAsync(string id, UpdateUserAccountRequest request, string callerId, CancellationToken ct = default);

    /// <param name="callerId">The account making the change, which may not delete itself.</param>
    Task<UserAccountResult> DeleteAsync(string id, string callerId, CancellationToken ct = default);
}
