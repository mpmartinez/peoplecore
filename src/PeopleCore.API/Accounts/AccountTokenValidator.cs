using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using PeopleCore.Infrastructure.Identity;

namespace PeopleCore.API.Accounts;

/// <summary>
/// Checks, on every request, that the account a token names still exists, is active, and has not
/// changed since the token was issued. Without it a deactivated account, or one whose roles were
/// just removed, would keep working until its token expired - up to eight hours.
/// </summary>
public class AccountTokenValidator
{
    private readonly UserManager<ApplicationUser> _users;

    public AccountTokenValidator(UserManager<ApplicationUser> users) => _users = users;

    /// <returns>Why the token must be refused, or null when it may be used.</returns>
    public async Task<string?> FindRejectionAsync(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = principal.FindFirstValue(AccountClaims.SecurityStamp);
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(stamp))
            return "The token was issued before revocation checks existed.";

        var user = await _users.FindByIdAsync(userId);
        if (user is null) return "The account no longer exists.";
        if (!user.IsActive) return "The account has been deactivated.";

        return user.SecurityStamp == stamp ? null : "The account has changed since the token was issued.";
    }
}
