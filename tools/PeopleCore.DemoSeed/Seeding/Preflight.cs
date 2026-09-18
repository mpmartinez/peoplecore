using PeopleCore.DemoSeed.Api;

namespace PeopleCore.DemoSeed.Seeding;

/// <summary>
/// The decisions behind the preflight, kept free of HTTP so they can be tested. Each returns why the
/// run must not go ahead, or null when it may.
/// </summary>
internal static class Preflight
{
    private const string AdminRequirement = "PEOPLECORE_ADMIN_EMAIL must be an Admin account with its own password";

    /// <summary>
    /// Only the Admin role holds everything the seeder needs (HRManager lacks leave.run-accruals, so
    /// an HR login would create the company and then fail at the first accrual). An account still on
    /// a temporary password can do nothing but change it.
    /// </summary>
    public static string? AdminProblem(SignIn signIn)
    {
        if (signIn.MustChangePassword)
            return $"{AdminRequirement}; this one is still on a temporary password. Sign in to the site once, choose a password, and use that.";
        if (!signIn.Roles.Contains("Admin", StringComparer.Ordinal))
            return $"{AdminRequirement}; this one has the roles {Describe(signIn.Roles)}, not Admin.";
        return null;
    }

    private static string Describe(IReadOnlyList<string> roles) => roles.Count == 0 ? "(none)" : string.Join(", ", roles);
}
