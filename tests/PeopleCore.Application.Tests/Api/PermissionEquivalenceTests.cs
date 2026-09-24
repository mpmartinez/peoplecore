using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using PeopleCore.API.Authorization;
using PeopleCore.API.Controllers.Auth;
using PeopleCore.Application.Common.Authorization;
using PeopleCore.Infrastructure.Identity;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// Moving from role names to permissions must not change who can reach what. LegacyRoles records,
/// endpoint by endpoint, the roles each one admitted before permissions existed. For every seeded
/// role and every endpoint, "may reach it now" - by the seeded role's permissions against the
/// endpoint's [RequirePermission] - must equal "could reach it then".
/// </summary>
public class PermissionEquivalenceTests
{
    private const string A = "Admin";
    private const string AH = "Admin,HRManager";
    private const string AHM = "Admin,HRManager,Manager";
    private const string AHP = "Admin,HRManager,PayrollService";
    private const string AHS = "Admin,HRManager,Service";

    /// <summary>Every endpoint that carried [Authorize(Roles = ...)] before permissions. All others were open to any signed-in user, or anonymous.</summary>
    private static readonly Dictionary<string, string> LegacyRoles = new()
    {
        ["UsersController.List"] = AH, ["UsersController.AssignableRoles"] = AH, ["UsersController.EmployeeLinks"] = AH,
        ["UsersController.Get"] = AH, ["UsersController.Create"] = AH, ["UsersController.SetRoles"] = AH,
        ["UsersController.LinkEmployee"] = AH, ["UsersController.Deactivate"] = AH, ["UsersController.Reactivate"] = AH,
        ["UsersController.ResetPassword"] = AH,

        ["ExecutiveAnalyticsController.GetWorkforceSummary"] = A, ["ExecutiveAnalyticsController.GetHiringTrend"] = A,
        ["ExecutiveAnalyticsController.GetAttritionRate"] = A, ["ExecutiveAnalyticsController.GetLeaveSummary"] = A,
        ["ExecutiveAnalyticsController.GetPerformanceOverview"] = A,

        ["HRAnalyticsController.GetHeadcount"] = AH, ["HRAnalyticsController.GetTurnover"] = AH,
        ["HRAnalyticsController.GetAttendance"] = AH, ["HRAnalyticsController.GetLeaveUtilization"] = AH,
        ["HRAnalyticsController.GetOvertime"] = AH, ["HRAnalyticsController.GetRecruitmentFunnel"] = AH,
        ["HRAnalyticsController.GetPerformanceDistribution"] = AH,

        ["AttendanceController.Sync"] = AHS, ["AttendanceController.Import"] = AH,
        ["HolidaysController.Create"] = AH, ["HolidaysController.Delete"] = AH,
        ["OvertimeController.Approve"] = AHM, ["OvertimeController.Reject"] = AHM,

        ["EmployeesController.Create"] = AH, ["EmployeesController.Update"] = AH,
        ["EmployeesController.Deactivate"] = AH, ["EmployeesController.DeleteDocument"] = AH,

        ["LeaveAccrualPoliciesController.GetPoliciesAsync"] = AH, ["LeaveAccrualPoliciesController.GetRules"] = AH,
        ["LeaveAccrualPoliciesController.CreatePolicyAsync"] = AH, ["LeaveAccrualPoliciesController.UpdatePolicyAsync"] = AH,
        ["LeaveAccrualPoliciesController.DeletePolicyAsync"] = AH,
        ["LeaveAccrualsController.RunAccrualsAsync"] = A,
        ["LeaveController.CreateLeaveType"] = AH, ["LeaveController.UpdateLeaveType"] = AH, ["LeaveController.DeleteLeaveType"] = AH,
        ["LeaveController.Approve"] = AHM, ["LeaveController.Reject"] = AHM,

        ["DepartmentsController.Create"] = AH, ["DepartmentsController.Update"] = AH, ["DepartmentsController.Delete"] = A,
        ["PositionsController.Create"] = AH, ["PositionsController.Update"] = AH, ["PositionsController.Delete"] = A,
        ["TeamsController.Create"] = AH, ["TeamsController.Update"] = AH, ["TeamsController.Delete"] = A,

        ["Bir2316Controller.GetYears"] = AHP, ["Bir2316Controller.GetPreview"] = AHP,
        ["Bir2316Controller.Generate"] = AHP, ["Bir2316Controller.GenerateAll"] = AHP,
        ["EmployeeCompensationController.GetByEmployee"] = AHP, ["EmployeeCompensationController.Upsert"] = AHP,
        ["PayrollRunsController.GetPaged"] = AHP, ["PayrollRunsController.GetById"] = AHP, ["PayrollRunsController.Create"] = AHP,
        ["PayrollRunsController.Compute"] = AHP, ["PayrollRunsController.Approve"] = AHP, ["PayrollRunsController.MarkPaid"] = AHP,
        ["PayrollSettingsController.GetByCompany"] = AHP, ["PayrollSettingsController.Update"] = AHP,
        ["ReportsController.GetPayslip"] = AHP, ["ReportsController.GetRunPayslips"] = AHP,
        ["PayrollExportController.GetEmployees"] = AHP, ["PayrollExportController.GetAttendanceSummary"] = AHP,
        ["PayrollExportController.GetApprovedLeaves"] = AHP, ["PayrollExportController.GetApprovedOvertime"] = AHP,
        ["PayrollExportController.GetStatusChanges"] = AHP,

        ["PerformanceController.CreateCycle"] = AH, ["PerformanceController.CloseCycle"] = AH,
        ["PerformanceController.CreateReview"] = AHM, ["PerformanceController.SubmitManagerReview"] = AHM,

        ["RecruitmentController.CreatePosting"] = AH, ["RecruitmentController.UpdatePosting"] = AH,
        ["RecruitmentController.PublishPosting"] = AH, ["RecruitmentController.ClosePosting"] = AH,
        ["RecruitmentController.CreateApplicant"] = AH, ["RecruitmentController.UpdateApplicantStatus"] = AH,
        ["RecruitmentController.ConvertToEmployee"] = AH, ["RecruitmentController.CreateInterview"] = AH,
        ["RecruitmentController.UpdateInterview"] = AH, ["RecruitmentController.DeleteInterview"] = AH,

        ["RotatingPatternsController.GetAll"] = AH, ["RotatingPatternsController.Create"] = AH, ["RotatingPatternsController.Delete"] = AH,
        ["ShiftAssignmentsController.AssignShift"] = AH, ["ShiftAssignmentsController.RemoveAssignment"] = AH,
        ["ShiftTemplatesController.GetAll"] = AH, ["ShiftTemplatesController.Create"] = AH,
        ["ShiftTemplatesController.Update"] = AH, ["ShiftTemplatesController.Delete"] = AH,
    };

    /// <summary>
    /// Endpoints added after role-name checks were removed. They had no "before" to be equivalent to,
    /// so each is pinned to exactly the permission it requires.
    /// </summary>
    private static readonly Dictionary<string, string[]> AddedEndpoints = new()
    {
        ["RolesController.List"] = [Permissions.RolesManage],
        ["RolesController.Get"] = [Permissions.RolesManage],
        ["RolesController.PermissionCatalogue"] = [Permissions.RolesManage],
        ["RolesController.Create"] = [Permissions.RolesManage],
        ["RolesController.Update"] = [Permissions.RolesManage],
        ["RolesController.Delete"] = [Permissions.RolesManage],

        ["EmailSettingsController.Get"] = [Permissions.SettingsManage],
        ["EmailSettingsController.Save"] = [Permissions.SettingsManage],
        ["EmailSettingsController.SendTest"] = [Permissions.SettingsManage],
        ["CompanyProfileController.Get"] = [Permissions.SettingsManage],
        ["CompanyProfileController.Save"] = [Permissions.SettingsManage],
        ["AttendanceController.SetBiometricId"] = [Permissions.AttendanceManage],
        ["AttendanceController.ImportTemplate"] = [Permissions.AttendanceManage],

        // Reads and requests are open to any signed-in user; the controller checks whose
        // attendance it is through IEmployeeAccessService (AttendanceCorrectionsAuthorizationTests).
        ["AttendanceCorrectionsController.GetAll"] = [],
        ["AttendanceCorrectionsController.GetHistory"] = [],
        ["AttendanceCorrectionsController.RequestCorrection"] = [],
        ["AttendanceCorrectionsController.Correct"] = [Permissions.AttendanceManage],
        ["AttendanceCorrectionsController.Approve"] = [Permissions.ApprovalsTeam, Permissions.ApprovalsAll],
        ["AttendanceCorrectionsController.Reject"] = [Permissions.ApprovalsTeam, Permissions.ApprovalsAll],

        // A leave request's document: uploading is self-service (the service refuses anyone but the
        // request's owner), and who may open it is checked inside the action - the owner,
        // approvals.all, or a team approver managing the employee on a non-confidential type
        // (LeaveControllerConfidentialTests).
        ["LeaveController.UploadDocument"] = [],
        ["LeaveController.GetDocument"] = [],

        ["GovernmentReportsController.Get"] = [Permissions.PayrollManage],

        ["Bir2316Controller.GetInputs"] = [Permissions.PayrollManage],

        ["SeparationsController.List"] = [Permissions.EmployeesManage],
        ["SeparationsController.Get"] = [Permissions.EmployeesManage],
        ["SeparationsController.Record"] = [Permissions.EmployeesManage],
        ["SeparationsController.MarkSeparated"] = [Permissions.EmployeesManage],
        ["SeparationsController.Cancel"] = [Permissions.EmployeesManage],
        ["SeparationsController.AddClearanceItem"] = [Permissions.EmployeesManage],
        ["SeparationsController.ClearItem"] = [Permissions.EmployeesManage],
        ["SeparationsController.UndoClearItem"] = [Permissions.EmployeesManage],
        ["SeparationsController.DeleteClearanceItem"] = [Permissions.EmployeesManage],

        ["EmployeesController.Coe"] = [Permissions.EmployeesManage],

        ["PayrollRunsController.RemoveEmployee"] = [Permissions.PayrollManage],
        ["FinalPayController.Get"] = [Permissions.PayrollManage],
        ["FinalPayController.Create"] = [Permissions.PayrollManage],
        ["FinalPayController.Update"] = [Permissions.PayrollManage],
    };

    private static IEnumerable<(Type Controller, MethodInfo Action)> Endpoints() =>
        from controller in typeof(AuthController).Assembly.GetTypes()
        where typeof(ControllerBase).IsAssignableFrom(controller) && !controller.IsAbstract
        from action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        where action.GetCustomAttributes<HttpMethodAttribute>().Any()
        select (controller, action);

    private static string KeyOf(Type controller, MethodInfo action) => $"{controller.Name}.{action.Name}";

    /// <summary>Every [RequirePermission] that applies - the controller's and the action's must all pass.</summary>
    private static List<RequirePermissionAttribute> RequirementsOf(Type controller, MethodInfo action) =>
        controller.GetCustomAttributes<RequirePermissionAttribute>(inherit: true)
            .Concat(action.GetCustomAttributes<RequirePermissionAttribute>(inherit: true))
            .ToList();

    [Fact]
    public void NoEndpoint_AuthorizesByRoleName_AnyMore()
    {
        var byRole = Endpoints()
            .Where(e => e.Controller.GetCustomAttributes<AuthorizeAttribute>(true)
                .Concat(e.Action.GetCustomAttributes<AuthorizeAttribute>(true))
                .Any(a => !string.IsNullOrEmpty(a.Roles)))
            .Select(e => KeyOf(e.Controller, e.Action))
            .ToList();

        byRole.Should().BeEmpty();
    }

    [Fact]
    public void EveryEndpointTheOldRolesGuarded_StillExists()
    {
        var present = Endpoints().Select(e => KeyOf(e.Controller, e.Action)).ToHashSet();

        LegacyRoles.Keys.Where(k => !present.Contains(k)).Should().BeEmpty("a renamed or removed action must be reflected in LegacyRoles");
    }

    [Fact]
    public void EndpointsThatWereOpenToEveryone_RequireNoPermission()
    {
        var newlyGuarded = Endpoints()
            .Where(e => !LegacyRoles.ContainsKey(KeyOf(e.Controller, e.Action))
                        && !AddedEndpoints.ContainsKey(KeyOf(e.Controller, e.Action))
                        && RequirementsOf(e.Controller, e.Action).Count > 0)
            .Select(e => KeyOf(e.Controller, e.Action))
            .ToList();

        newlyGuarded.Should().BeEmpty();
    }

    [Fact]
    public void EndpointsAddedSincePermissions_RequireExactlyTheirPermission()
    {
        var present = Endpoints().ToDictionary(e => KeyOf(e.Controller, e.Action));

        foreach (var (key, expected) in AddedEndpoints)
        {
            present.Should().ContainKey(key);
            var (controller, action) = present[key];
            RequirementsOf(controller, action).SelectMany(r => r.AnyOf).Should().Equal(expected, key);
        }
    }

    [Fact]
    public void EverySeededRole_ReachesExactlyTheEndpointsItsOldRoleReached()
    {
        var differences = new List<string>();

        foreach (var (controller, action) in Endpoints())
        {
            if (!LegacyRoles.TryGetValue(KeyOf(controller, action), out var legacy)) continue;

            var requirements = RequirementsOf(controller, action);
            foreach (var role in SeededRoles.All)
            {
                var granted = SeededRoles.PermissionsOf([role]);
                var reachesNow = requirements.Count > 0 && requirements.All(r => r.AnyOf.Any(granted.Contains));
                var reachedThen = legacy.Split(',').Contains(role);
                if (reachesNow != reachedThen)
                    differences.Add($"{role} on {KeyOf(controller, action)}: then {reachedThen}, now {reachesNow}");
            }
        }

        differences.Should().BeEmpty();
    }
}
