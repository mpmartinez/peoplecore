namespace PeopleCore.Web.Auth;

/// <summary>
/// Client copy of the API's permission keys (PeopleCore.Application.Common.Authorization.Permissions).
/// Menus and page guards name these; the signed-in token carries the ones the account holds as
/// "permission" claims. WebPermissionsMirrorTests fails if this list drifts from the API's.
/// </summary>
public static class Permissions
{
    public const string ClaimType = "permission";

    public const string EmployeesViewAll = "employees.view-all";
    public const string EmployeesManage = "employees.manage";
    public const string OrganizationManage = "organization.manage";
    public const string OrganizationDelete = "organization.delete";
    public const string AttendanceManage = "attendance.manage";
    public const string AttendanceDeviceSync = "attendance.device-sync";
    public const string LeaveManage = "leave.manage";
    public const string LeaveRunAccruals = "leave.run-accruals";
    public const string ApprovalsTeam = "approvals.team";
    public const string ApprovalsAll = "approvals.all";
    public const string PerformanceManage = "performance.manage";
    public const string PayrollManage = "payroll.manage";
    public const string RecruitmentManage = "recruitment.manage";
    public const string SchedulingManage = "scheduling.manage";
    public const string AnalyticsHr = "analytics.hr";
    public const string AnalyticsExecutive = "analytics.executive";
    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";

    public static readonly IReadOnlyList<string> AllKeys =
    [
        EmployeesViewAll, EmployeesManage, OrganizationManage, OrganizationDelete, AttendanceManage,
        AttendanceDeviceSync, LeaveManage, LeaveRunAccruals, ApprovalsTeam, ApprovalsAll,
        PerformanceManage, PayrollManage, RecruitmentManage, SchedulingManage, AnalyticsHr,
        AnalyticsExecutive, UsersManage, RolesManage,
    ];
}

/// <summary>Policy names in the API's format: "permission:" and the keys, any of which will do.</summary>
public static class PermissionPolicy
{
    public const string Prefix = "permission:";

    public static string NameFor(params string[] anyOf)
    {
        if (anyOf.Length == 0)
            throw new ArgumentException("A permission policy needs at least one permission.", nameof(anyOf));
        return Prefix + string.Join('|', anyOf);
    }

    public static bool TryParse(string policyName, out IReadOnlyList<string> anyOf)
    {
        anyOf = [];
        if (!policyName.StartsWith(Prefix, StringComparison.Ordinal)) return false;

        var keys = policyName[Prefix.Length..].Split('|', StringSplitOptions.RemoveEmptyEntries);
        if (keys.Length == 0 || keys.Any(k => !Permissions.AllKeys.Contains(k))) return false;

        anyOf = keys;
        return true;
    }
}
