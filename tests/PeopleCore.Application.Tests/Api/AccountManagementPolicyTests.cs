using FluentAssertions;
using PeopleCore.API.Accounts;
using PeopleCore.Application.Common.Authorization;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// Who may change which account, now that roles are data. The rule: nobody grants, removes or
/// manages beyond the permissions they hold themselves, and only an Admin touches Admin. The guards
/// against locking yourself or the organisation out still apply.
/// </summary>
public class AccountManagementPolicyTests
{
    private const string AdminId = "admin-1";
    private const string HrId = "hr-1";
    private const string OtherId = "other-1";

    private static readonly string[] HrPermissions =
    [
        Permissions.EmployeesViewAll, Permissions.EmployeesManage, Permissions.OrganizationManage,
        Permissions.AttendanceManage, Permissions.AttendanceDeviceSync, Permissions.LeaveManage,
        Permissions.ApprovalsAll, Permissions.PerformanceManage, Permissions.PayrollManage,
        Permissions.RecruitmentManage, Permissions.SchedulingManage, Permissions.AnalyticsHr,
        Permissions.UsersManage,
    ];

    private static readonly IReadOnlyList<RoleGrant> Catalog =
    [
        new("Admin", Permissions.AllKeys),
        new("Employee", []),
        new("Service", [Permissions.AttendanceDeviceSync]),
        new("Auditor", [Permissions.AnalyticsExecutive]),
        new("HRManager", HrPermissions),
        new("Manager", [Permissions.ApprovalsTeam]),
        new("PayrollService", [Permissions.PayrollManage]),
    ];

    private static readonly AccountActor Admin = new(AdminId, ["Admin", "Employee"], Permissions.AllKeys);
    private static readonly AccountActor Hr = new(HrId, ["HRManager", "Employee"], HrPermissions);

    private static AccountTarget Account(params string[] roles) => new(OtherId, ["Employee", .. roles], IsActive: true);

    private static AccountTarget Self(AccountActor actor) => new(actor.UserId, actor.Roles, IsActive: true);

    // --- Effective permissions and assignable roles -------------------------------------------

    [Fact]
    public void AnAccountsPermissions_AreTheUnionOfItsRoles_AndEverythingForAdmin()
    {
        AccountManagementPolicy.PermissionsOf(["Manager", "PayrollService"], Catalog)
            .Should().BeEquivalentTo(Permissions.ApprovalsTeam, Permissions.PayrollManage);
        AccountManagementPolicy.PermissionsOf(["Employee", "Admin"], Catalog).Should().BeEquivalentTo(Permissions.AllKeys);
    }

    [Fact]
    public void AssignableRoles_ListEveryRoleButService_InCatalogueOrder_SayingWhichHrMayGrant()
    {
        var roles = AccountManagementPolicy.AssignableRoles(Hr, Catalog);

        roles.Select(r => r.Name).Should().Equal("Admin", "Employee", "Auditor", "HRManager", "Manager", "PayrollService");
        roles.Where(r => r.Grantable).Select(r => r.Name).Should().Equal("Employee", "HRManager", "Manager", "PayrollService");
        roles.Single(r => r.Name == "Admin").Reason.Should().Be("Only an administrator can grant or remove the Admin role.");
        roles.Single(r => r.Name == "Auditor").Reason.Should().Be("You can't grant or remove the Auditor role: it allows things you can't do yourself.");
    }

    [Fact]
    public void AnAdmin_MayGrantEveryAssignableRole()
    {
        AccountManagementPolicy.AssignableRoles(Admin, Catalog).Should().OnlyContain(r => r.Grantable);
    }

    [Fact]
    public void NobodyMayGrantTheServiceRole()
    {
        AccountManagementPolicy.CanGrant(Admin, "Service", Catalog).Reason
            .Should().Be("The Service role is for attendance devices and can't be assigned.");
    }

    [Fact]
    public void HR_MayGrantManager_BecauseApprovingForEveryoneCoversTheTeam()
    {
        AccountManagementPolicy.CanCreate(Hr, ["Employee", "Manager"], Catalog).Allowed.Should().BeTrue();
        AccountManagementPolicy.AssignableRoles(Hr, Catalog).Single(r => r.Name == "Manager").Grantable.Should().BeTrue();
    }

    // --- Create -------------------------------------------------------------------------------

    [Fact]
    public void HR_MayCreateAnHrManager_BecauseItGrantsNothingHrLacks()
    {
        AccountManagementPolicy.CanCreate(Hr, ["Employee", "HRManager"], Catalog).Allowed.Should().BeTrue();
    }

    [Fact]
    public void HR_MayNotCreateAnAccountWithARoleThatDoesMoreThanHr()
    {
        AccountManagementPolicy.CanCreate(Hr, ["Employee", "Auditor"], Catalog).Reason
            .Should().Be("You can't grant or remove the Auditor role: it allows things you can't do yourself.");
    }

    [Fact]
    public void HR_MayNotCreateAnAdmin()
    {
        AccountManagementPolicy.CanCreate(Hr, ["Employee", "Admin"], Catalog).Reason
            .Should().Be("Only an administrator can grant or remove the Admin role.");
    }

    // --- Manage -------------------------------------------------------------------------------

    [Fact]
    public void HR_MayManageAnotherHrManager()
    {
        AccountManagementPolicy.CanManage(Hr, Account("HRManager"), Catalog).Allowed.Should().BeTrue();
    }

    [Fact]
    public void HR_MayNotManageAnAdmin()
    {
        AccountManagementPolicy.CanManage(Hr, Account("Admin"), Catalog).Reason
            .Should().Be("Only an administrator can change an administrator's account.");
    }

    [Fact]
    public void HR_MayNotManageAnAccountWhoseRolesDoMoreThanHr()
    {
        AccountManagementPolicy.CanManage(Hr, Account("Auditor"), Catalog).Reason
            .Should().Be("You can't change this account: its roles allow things you can't do yourself.");
    }

    [Fact]
    public void AnAdmin_MayManageAnyone()
    {
        AccountManagementPolicy.CanManage(Admin, Account("Admin", "Auditor"), Catalog).Allowed.Should().BeTrue();
    }

    // --- Set roles ----------------------------------------------------------------------------

    [Fact]
    public void HR_MayGrantAndRemoveRolesItCovers()
    {
        AccountManagementPolicy.CanSetRoles(Hr, Account("PayrollService"), ["Employee", "Manager", "HRManager"], Catalog, activeAdmins: 1)
            .Allowed.Should().BeTrue();
    }

    [Fact]
    public void HR_MayNotGrantARoleThatDoesMoreThanHr()
    {
        AccountManagementPolicy.CanSetRoles(Hr, Account(), ["Employee", "Auditor"], Catalog, activeAdmins: 1).Reason
            .Should().Be("You can't grant or remove the Auditor role: it allows things you can't do yourself.");
    }

    [Fact]
    public void ARoleNobodyCanAssign_ThatTheAccountAlreadyHolds_DoesNotBlockAnEdit()
    {
        var target = new AccountTarget(OtherId, ["Employee", "Service"], IsActive: true);

        AccountManagementPolicy.CanSetRoles(Admin, target, ["Employee", "Service", "Manager"], Catalog, activeAdmins: 2)
            .Allowed.Should().BeTrue();
    }

    [Fact]
    public void NobodyMayRemoveTheirOwnPermissionToManageUsers()
    {
        AccountManagementPolicy.CanSetRoles(Hr, Self(Hr), ["Employee", "Manager"], Catalog, activeAdmins: 1).Reason
            .Should().Be("You can't remove your own permission to manage users.");
    }

    [Fact]
    public void RemovingOneOfYourOwnRoles_IsFine_WhileAnotherStillLetsYouManageUsers()
    {
        var both = new AccountActor(HrId, ["HRManager", "Manager", "Employee"], [.. HrPermissions, Permissions.ApprovalsTeam]);

        AccountManagementPolicy.CanSetRoles(both, Self(both), ["HRManager", "Employee"], Catalog, activeAdmins: 1)
            .Allowed.Should().BeTrue();
    }

    [Fact]
    public void AnAdmin_MayDropAdmin_OnlyWhileAnotherRoleStillLetsThemManageUsers_AndAnotherAdminRemains()
    {
        var adminAndHr = new AccountActor(AdminId, ["Admin", "HRManager", "Employee"], Permissions.AllKeys);

        AccountManagementPolicy.CanSetRoles(adminAndHr, Self(adminAndHr), ["HRManager", "Employee"], Catalog, activeAdmins: 2)
            .Allowed.Should().BeTrue();
        AccountManagementPolicy.CanSetRoles(Admin, Self(Admin), ["Employee"], Catalog, activeAdmins: 2).Reason
            .Should().Be("You can't remove your own permission to manage users.");
    }

    [Fact]
    public void TheLastActiveAdmin_MayNotLoseTheAdminRole()
    {
        AccountManagementPolicy.CanSetRoles(Admin, Account("Admin"), ["Employee"], Catalog, activeAdmins: 1).Reason
            .Should().Be("This is the last active administrator. Make another account an Admin first.");
    }

    [Fact]
    public void ADeactivatedAdmin_IsNotTheLastActiveAdmin()
    {
        var inactiveAdmin = new AccountTarget(OtherId, ["Admin", "Employee"], IsActive: false);

        AccountManagementPolicy.CanSetRoles(Admin, inactiveAdmin, ["Employee"], Catalog, activeAdmins: 1).Allowed.Should().BeTrue();
    }

    // --- Deactivate, reactivate, reset, link --------------------------------------------------

    [Fact]
    public void NobodyMayDeactivateTheirOwnAccount()
    {
        AccountManagementPolicy.CanDeactivate(Admin, Self(Admin), Catalog, activeAdmins: 3).Reason
            .Should().Be("You can't deactivate your own account.");
    }

    [Fact]
    public void TheLastActiveAdmin_MayNotBeDeactivated()
    {
        AccountManagementPolicy.CanDeactivate(Admin, Account("Admin"), Catalog, activeAdmins: 1).Reason
            .Should().Be("This is the last active administrator. Make another account an Admin first.");
    }

    [Fact]
    public void HR_MayDeactivateReactivateResetAndLink_AnAccountItCovers_ButNotOneItDoesNot()
    {
        var covered = Account("Manager");
        var beyond = Account("Auditor");

        AccountManagementPolicy.CanDeactivate(Hr, covered, Catalog, activeAdmins: 1).Allowed.Should().BeTrue();
        AccountManagementPolicy.CanReactivate(Hr, covered, Catalog).Allowed.Should().BeTrue();
        AccountManagementPolicy.CanResetPassword(Hr, covered, Catalog).Allowed.Should().BeTrue();
        AccountManagementPolicy.CanLinkEmployee(Hr, covered, Catalog).Allowed.Should().BeTrue();

        AccountManagementPolicy.CanDeactivate(Hr, beyond, Catalog, activeAdmins: 1).Allowed.Should().BeFalse();
        AccountManagementPolicy.CanReactivate(Hr, beyond, Catalog).Allowed.Should().BeFalse();
        AccountManagementPolicy.CanResetPassword(Hr, beyond, Catalog).Allowed.Should().BeFalse();
        AccountManagementPolicy.CanLinkEmployee(Hr, beyond, Catalog).Allowed.Should().BeFalse();
    }

    [Fact]
    public void NobodyMayResetTheirOwnPassword_ThatIsWhatChangePasswordIsFor()
    {
        AccountManagementPolicy.CanResetPassword(Admin, Self(Admin), Catalog).Reason
            .Should().Be("Use Change Password on My Profile to change your own password.");
    }
}
