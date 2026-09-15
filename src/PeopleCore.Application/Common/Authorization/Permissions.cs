namespace PeopleCore.Application.Common.Authorization;

/// <summary>One thing a role can allow, with the words the Roles page shows for it.</summary>
public sealed record PermissionDefinition(string Key, string Group, string Label, string Description);

/// <summary>
/// Everything a role can allow. Access checks name these keys, never a role, so a role an admin
/// creates at runtime means something without a code change. Keys are persisted - in
/// AspNetRoleClaims and in tokens - so a key is renamed only with a migration. The web client keeps
/// a copy (PeopleCore.Web/Auth/Permissions.cs); WebPermissionsMirrorTests keeps the two equal.
/// </summary>
public static class Permissions
{
    public const string ClaimType = "permission";

    /// <summary>Tokens without this claim at <see cref="CurrentVersion"/> predate permissions and are refused.</summary>
    public const string VersionClaimType = "perm_v";
    public const string CurrentVersion = "1";

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

    public static readonly IReadOnlyList<PermissionDefinition> All =
    [
        new(EmployeesViewAll, "People", "View all employees",
            "Any employee's full profile, government IDs, emergency contacts and documents, and everyone's attendance, leave, overtime and schedules."),
        new(EmployeesManage, "People", "Manage employees",
            "Create, edit and deactivate employees, and edit any employee's government IDs, emergency contacts and documents."),
        new(OrganizationManage, "Organization", "Manage departments, positions and teams",
            "Create and edit departments, positions and teams."),
        new(OrganizationDelete, "Organization", "Delete departments, positions and teams",
            "Delete departments, positions and teams."),
        new(AttendanceManage, "Time", "Manage attendance and holidays",
            "Import attendance, and create and delete holidays."),
        new(AttendanceDeviceSync, "Time", "Sync attendance devices",
            "Send clock-ins from attendance devices."),
        new(LeaveManage, "Time", "Manage leave types and accrual policies",
            "Create, edit and delete leave types and accrual policies."),
        new(LeaveRunAccruals, "Time", "Run leave accruals",
            "Run leave accruals by hand."),
        new(ApprovalsTeam, "Approvals", "Approve for my team",
            "Decide direct reports' leave and overtime, write their performance reviews, and see their records."),
        new(ApprovalsAll, "Approvals", "Approve for everyone",
            "Decide anyone's leave, write anyone's performance review, and see everyone's leave and overtime requests. Overtime itself is decided by each employee's own manager."),
        new(PerformanceManage, "Performance", "Manage review cycles",
            "Create and close performance review cycles."),
        new(PayrollManage, "Payroll", "Run payroll",
            "Payroll runs, settings, compensation, BIR 2316, payroll export and any employee's payslips."),
        new(RecruitmentManage, "Recruitment", "Manage recruitment",
            "Job postings, applicants and interviews."),
        new(SchedulingManage, "Scheduling", "Manage schedules",
            "Shift templates, rotating patterns and shift assignments."),
        new(AnalyticsHr, "Analytics", "HR analytics",
            "Headcount, turnover, attendance, leave, overtime, recruitment and performance analytics."),
        new(AnalyticsExecutive, "Analytics", "Executive analytics",
            "Workforce summary, hiring trend, attrition and executive leave and performance views."),
        new(UsersManage, "Administration", "Manage users",
            "Create sign-in accounts, change their roles and employee links, deactivate them and reset passwords."),
        new(RolesManage, "Administration", "Manage roles",
            "Create, rename, edit and delete roles."),
    ];

    public static readonly IReadOnlyList<string> AllKeys = All.Select(p => p.Key).ToList();
}

/// <summary>
/// Names for authorization policies that require a permission. One policy name can carry several
/// keys, satisfied by any of them, so "a team approver or an everyone approver" is one attribute.
/// </summary>
public static class PermissionPolicy
{
    public const string Prefix = "permission:";

    public static string NameFor(params string[] anyOf)
    {
        if (anyOf.Length == 0)
            throw new ArgumentException("A permission policy needs at least one permission.", nameof(anyOf));
        foreach (var key in anyOf)
            if (!Permissions.AllKeys.Contains(key))
                throw new ArgumentException($"'{key}' is not a known permission key.", nameof(anyOf));
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
