using PeopleCore.Application.Common.Authorization;

namespace PeopleCore.Infrastructure.Identity;

/// <summary>
/// The roles every PeopleCore database starts with, and the permissions each is seeded with. These
/// reproduce, permission for permission, what the hard-coded role checks allowed before permissions
/// existed (PermissionEquivalenceTests proves it). After seeding the database is the authority: an
/// admin may change the ordinary roles, and nothing here overwrites that.
/// </summary>
public static class SeededRoles
{
    public const string Admin = "Admin";
    public const string HRManager = "HRManager";
    public const string Manager = "Manager";
    public const string Employee = "Employee";
    public const string PayrollService = "PayrollService";
    public const string Service = "Service";

    public static readonly IReadOnlyList<string> All = [Admin, HRManager, Manager, Employee, PayrollService, Service];

    /// <summary>Roles the app relies on by name, which cannot be edited or deleted.</summary>
    public static readonly IReadOnlyList<string> System = [Admin, Employee, Service];

    public static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>
    {
        [Admin] = "Every permission, always. Cannot be edited or deleted.",
        [HRManager] = "Runs HR: employees, organization, time, approvals, performance, payroll, recruitment, schedules, analytics and user accounts.",
        [Manager] = "Approves leave, overtime and performance reviews for their direct reports.",
        [Employee] = "Self-service: their own profile, attendance, leave and payslips. Every account holds it.",
        [PayrollService] = "Runs payroll.",
        [Service] = "Attendance devices sending clock-ins. Cannot be edited or deleted.",
    };

    /// <summary>
    /// The permissions written to the database for <paramref name="role"/> when it is seeded. Admin
    /// stores none: it holds every permission by rule, so a permission added in a later release
    /// reaches it without a data change.
    /// </summary>
    public static IReadOnlyList<string> StoredPermissions(string role) => role switch
    {
        HRManager =>
        [
            Permissions.EmployeesViewAll, Permissions.EmployeesManage, Permissions.OrganizationManage,
            Permissions.AttendanceManage, Permissions.AttendanceDeviceSync, Permissions.LeaveManage,
            Permissions.ApprovalsAll, Permissions.PerformanceManage, Permissions.PayrollManage,
            Permissions.RecruitmentManage, Permissions.SchedulingManage, Permissions.AnalyticsHr,
            Permissions.UsersManage,
        ],
        Manager => [Permissions.ApprovalsTeam],
        PayrollService => [Permissions.PayrollManage],
        Service => [Permissions.AttendanceDeviceSync],
        _ => [],
    };

    /// <summary>What an account holding <paramref name="roles"/> may do under the seeded roles, in catalogue order.</summary>
    public static IReadOnlyList<string> PermissionsOf(IEnumerable<string> roles)
    {
        var held = roles.ToList();
        return held.Contains(Admin)
            ? Permissions.AllKeys
            : Permissions.AllKeys.Where(key => held.Any(role => StoredPermissions(role).Contains(key))).ToList();
    }
}
