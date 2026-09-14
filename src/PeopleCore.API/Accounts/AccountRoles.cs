namespace PeopleCore.API.Accounts;

/// <summary>
/// The roles user management can hand out. Program.cs also seeds "Service", which no endpoint
/// uses, so it is deliberately absent here and cannot be granted.
/// </summary>
public static class AccountRoles
{
    public const string Admin = "Admin";
    public const string HRManager = "HRManager";
    public const string Manager = "Manager";
    public const string Employee = "Employee";
    public const string PayrollService = "PayrollService";

    /// <summary>In display order. <see cref="Employee"/> is on every account and cannot be removed.</summary>
    public static readonly IReadOnlyList<string> Assignable = [Admin, HRManager, Manager, Employee, PayrollService];

    /// <summary>Roles that confer control over other accounts, and so only an Admin may grant.</summary>
    public static readonly IReadOnlyList<string> Privileged = [Admin, HRManager];

    public static bool IsPrivileged(IEnumerable<string> roles) => roles.Any(role => Privileged.Contains(role));
}
