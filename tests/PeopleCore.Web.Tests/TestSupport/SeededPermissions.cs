using System.Security.Claims;
using PeopleCore.Web.Auth;

namespace PeopleCore.Web.Tests.TestSupport;

/// <summary>
/// The permissions each seeded role starts with, so page and menu tests can keep speaking in roles.
/// A copy of SeededRoles in PeopleCore.Infrastructure, which the web tests cannot reference;
/// PermissionEquivalenceTests there proves those seeds reproduce the old role checks.
/// </summary>
public static class SeededPermissions
{
    private static readonly Dictionary<string, string[]> Stored = new()
    {
        ["HRManager"] =
        [
            Permissions.EmployeesViewAll, Permissions.EmployeesManage, Permissions.OrganizationManage,
            Permissions.AttendanceManage, Permissions.AttendanceDeviceSync, Permissions.LeaveManage,
            Permissions.ApprovalsAll, Permissions.PerformanceManage, Permissions.PayrollManage,
            Permissions.RecruitmentManage, Permissions.SchedulingManage, Permissions.AnalyticsHr,
            Permissions.UsersManage,
        ],
        ["Manager"] = [Permissions.ApprovalsTeam],
        ["PayrollService"] = [Permissions.PayrollManage],
        ["Service"] = [Permissions.AttendanceDeviceSync],
    };

    public static IReadOnlyList<string> Of(params string[] roles) =>
        roles.Contains("Admin")
            ? Permissions.AllKeys
            : Permissions.AllKeys.Where(k => roles.Any(r => Stored.TryGetValue(r, out var keys) && keys.Contains(k))).ToList();

    public static Claim[] ClaimsFor(params string[] roles) =>
        Of(roles).Select(p => new Claim(Permissions.ClaimType, p)).ToArray();
}
