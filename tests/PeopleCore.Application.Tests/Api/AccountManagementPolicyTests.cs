using FluentAssertions;
using PeopleCore.API.Accounts;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// Who may change which account. Admins may change anything short of locking the organisation out
/// of its own administration. HR runs everyday accounts but cannot touch, or mint, Admin or HR
/// Manager accounts - otherwise any HR Manager could make themselves an administrator.
/// </summary>
public class AccountManagementPolicyTests
{
    private const string AdminId = "admin-1";
    private const string HrId = "hr-1";
    private const string OtherId = "other-1";

    private static readonly AccountActor Admin = new(AdminId, ["Admin", "Employee"]);
    private static readonly AccountActor Hr = new(HrId, ["HRManager", "Employee"]);

    private static AccountTarget Staff(params string[] roles) => new(OtherId, ["Employee", .. roles], IsActive: true);

    private static AccountTarget AdminAccount(string id = OtherId, bool active = true) => new(id, ["Admin", "Employee"], active);

    private static AccountTarget HrAccount() => new(OtherId, ["HRManager", "Employee"], IsActive: true);

    // --- Assignable roles -----------------------------------------------------------------------

    [Fact]
    public void AnAdmin_MayGrantEveryAssignableRole()
    {
        AccountManagementPolicy.AssignableRolesFor(Admin)
            .Should().Equal("Admin", "HRManager", "Manager", "Employee", "PayrollService");
    }

    [Fact]
    public void HR_MayGrantOnlyTheUnprivilegedRoles()
    {
        AccountManagementPolicy.AssignableRolesFor(Hr).Should().Equal("Manager", "Employee", "PayrollService");
    }

    // --- Create ---------------------------------------------------------------------------------

    [Fact]
    public void HR_MayCreateAManager()
    {
        AccountManagementPolicy.CanCreate(Hr, ["Employee", "Manager"]).Allowed.Should().BeTrue();
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("HRManager")]
    public void HR_MayNotCreateAPrivilegedAccount(string role)
    {
        var decision = AccountManagementPolicy.CanCreate(Hr, ["Employee", role]);

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Be($"Only an administrator can grant or remove the {role} role.");
    }

    [Fact]
    public void AnAdmin_MayCreateAnotherAdmin()
    {
        AccountManagementPolicy.CanCreate(Admin, ["Employee", "Admin"]).Allowed.Should().BeTrue();
    }

    // --- Manage ---------------------------------------------------------------------------------

    [Fact]
    public void HR_MayManageAnEverydayAccount()
    {
        AccountManagementPolicy.CanManage(Hr, Staff("Manager")).Allowed.Should().BeTrue();
    }

    [Fact]
    public void HR_MayNotManageAnAdminOrAnotherHrManager()
    {
        AccountManagementPolicy.CanManage(Hr, AdminAccount()).Reason
            .Should().Be("Only an administrator can change an Admin or HR Manager account.");
        AccountManagementPolicy.CanManage(Hr, HrAccount()).Allowed.Should().BeFalse();
    }

    [Fact]
    public void AnAdmin_MayManageAPrivilegedAccount()
    {
        AccountManagementPolicy.CanManage(Admin, HrAccount()).Allowed.Should().BeTrue();
    }

    // --- Set roles ------------------------------------------------------------------------------

    [Fact]
    public void HR_MayGrantAndRemoveEverydayRoles()
    {
        AccountManagementPolicy.CanSetRoles(Hr, Staff("PayrollService"), ["Employee", "Manager"], activeAdmins: 1)
            .Allowed.Should().BeTrue();
    }

    [Fact]
    public void HR_MayNotPromoteAnyoneToHrManager()
    {
        AccountManagementPolicy.CanSetRoles(Hr, Staff(), ["Employee", "HRManager"], activeAdmins: 1).Reason
            .Should().Be("Only an administrator can grant or remove the HRManager role.");
    }

    [Fact]
    public void HR_MayNotChangeTheRolesOfAPrivilegedAccount()
    {
        AccountManagementPolicy.CanSetRoles(Hr, HrAccount(), ["HRManager", "Employee", "Manager"], activeAdmins: 1)
            .Allowed.Should().BeFalse();
    }

    [Fact]
    public void ARoleNobodyCanAssign_ThatTheAccountAlreadyHolds_DoesNotBlockAnEdit()
    {
        // Service is seeded but unassignable. An account holding it is still HR's to edit; the
        // controller leaves the role in place.
        var target = new AccountTarget(OtherId, ["Employee", "Service"], IsActive: true);

        AccountManagementPolicy.CanSetRoles(Hr, target, ["Employee", "Manager"], activeAdmins: 1)
            .Allowed.Should().BeTrue();
    }

    [Fact]
    public void AnAdmin_MayNotRemoveTheirOwnAdminRole()
    {
        var self = AdminAccount(AdminId);

        AccountManagementPolicy.CanSetRoles(Admin, self, ["Employee"], activeAdmins: 3).Reason
            .Should().Be("You can't remove your own Admin or HR Manager role.");
    }

    [Fact]
    public void TheLastActiveAdmin_MayNotLoseTheAdminRole()
    {
        AccountManagementPolicy.CanSetRoles(Admin, AdminAccount(), ["Employee"], activeAdmins: 1).Reason
            .Should().Be("This is the last active administrator. Make another account an Admin first.");
    }

    [Fact]
    public void AnAdmin_MayLoseTheAdminRole_WhileAnotherActiveAdminRemains()
    {
        AccountManagementPolicy.CanSetRoles(Admin, AdminAccount(), ["Employee"], activeAdmins: 2)
            .Allowed.Should().BeTrue();
    }

    [Fact]
    public void ADeactivatedAdmin_IsNotTheLastActiveAdmin()
    {
        AccountManagementPolicy.CanSetRoles(Admin, AdminAccount(active: false), ["Employee"], activeAdmins: 1)
            .Allowed.Should().BeTrue();
    }

    // --- Deactivate / reactivate ----------------------------------------------------------------

    [Fact]
    public void HR_MayDeactivateAnEverydayAccount_ButNotAPrivilegedOne()
    {
        AccountManagementPolicy.CanDeactivate(Hr, Staff(), activeAdmins: 1).Allowed.Should().BeTrue();
        AccountManagementPolicy.CanDeactivate(Hr, HrAccount(), activeAdmins: 1).Allowed.Should().BeFalse();
    }

    [Fact]
    public void NobodyMayDeactivateTheirOwnAccount()
    {
        AccountManagementPolicy.CanDeactivate(Admin, AdminAccount(AdminId), activeAdmins: 3).Reason
            .Should().Be("You can't deactivate your own account.");
    }

    [Fact]
    public void TheLastActiveAdmin_MayNotBeDeactivated()
    {
        AccountManagementPolicy.CanDeactivate(Admin, AdminAccount(), activeAdmins: 1).Reason
            .Should().Be("This is the last active administrator. Make another account an Admin first.");
    }

    [Fact]
    public void HR_MayReactivateAnEverydayAccount_ButNotAPrivilegedOne()
    {
        AccountManagementPolicy.CanReactivate(Hr, Staff() with { IsActive = false }).Allowed.Should().BeTrue();
        AccountManagementPolicy.CanReactivate(Hr, AdminAccount(active: false)).Allowed.Should().BeFalse();
    }

    // --- Reset password / link employee ---------------------------------------------------------

    [Fact]
    public void NobodyMayResetTheirOwnPassword_ThatIsWhatChangePasswordIsFor()
    {
        AccountManagementPolicy.CanResetPassword(Admin, AdminAccount(AdminId)).Reason
            .Should().Be("Use Change Password on My Profile to change your own password.");
    }

    [Fact]
    public void HR_MayResetAnEverydayPassword_ButNotAPrivilegedOne()
    {
        AccountManagementPolicy.CanResetPassword(Hr, Staff()).Allowed.Should().BeTrue();
        AccountManagementPolicy.CanResetPassword(Hr, AdminAccount()).Allowed.Should().BeFalse();
    }

    [Fact]
    public void HR_MayLinkAnEverydayAccountToAnEmployee_ButNotAPrivilegedOne()
    {
        AccountManagementPolicy.CanLinkEmployee(Hr, Staff()).Allowed.Should().BeTrue();
        AccountManagementPolicy.CanLinkEmployee(Hr, HrAccount()).Allowed.Should().BeFalse();
    }
}
