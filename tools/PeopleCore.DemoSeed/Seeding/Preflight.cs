using System.Text.Json.Nodes;
using PeopleCore.DemoSeed.Api;
using PeopleCore.DemoSeed.Plan;

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

    /// <summary>
    /// An existing VL or SL type is reused as it is. LeaveRequestService refuses a request from anyone
    /// whose gender differs from a non-null GenderRestriction (an empty string included), and the
    /// payroll bridge counts a day of unpaid leave as an absence, so either would break the demo.
    /// </summary>
    public static string? LeaveTypeProblem(string code, JsonNode type)
    {
        if (type["genderRestriction"] is JsonValue restriction)
            return $"The existing {code} leave type is limited to one gender ({restriction}), so some of the demo's leave would be refused.";
        if (type["isPaid"] is JsonValue paid && paid.TryGetValue<bool>(out var isPaid) && !isPaid)
            return $"The existing {code} leave type is unpaid, so the demo's leave days would be paid as absences.";
        return null;
    }

    /// <summary>The demo's departments hang off the site's one company; with several, it can't tell which is meant.</summary>
    public static string? CompanyProblem(int companies) => companies switch
    {
        0 => "The site has no company record.",
        1 => null,
        _ => $"The site has {companies} companies. The demo needs a single-company site, so it knows where its departments belong.",
    };

    /// <summary>An account on the demo's own email domain.</summary>
    public static bool IsDemoEmail(string email) =>
        email.EndsWith("@" + PeopleBuilder.EmailDomain, StringComparison.OrdinalIgnoreCase);

    private static string Describe(IReadOnlyList<string> roles) => roles.Count == 0 ? "(none)" : string.Join(", ", roles);
}
