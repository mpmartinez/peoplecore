using FluentAssertions;
using PeopleCore.API.Accounts;
using PeopleCore.Application.Common.Authorization;
using Xunit;

namespace PeopleCore.Application.Tests.Api;

/// <summary>
/// Who may change which role. The same principle as accounts: nobody puts on a role, or changes a
/// role that already has, a permission they don't hold. System roles are the app's, not an admin's.
/// </summary>
public class RoleManagementPolicyTests
{
    private static readonly AccountActor Admin = new("admin-1", ["Admin", "Employee"], Permissions.AllKeys);

    // Holds roles.manage and users.manage through a custom role, plus recruitment.
    private static readonly AccountActor Delegate = new("delegate-1", ["Delegates", "Employee"],
        [Permissions.RolesManage, Permissions.UsersManage, Permissions.RecruitmentManage]);

    private static RoleSnapshot Role(params string[] permissions) => new("Recruiter", IsSystem: false, permissions);

    [Fact]
    public void SystemRoles_CannotBeChanged_EvenByAnAdmin()
    {
        var admin = new RoleSnapshot("Admin", IsSystem: true, Permissions.AllKeys);

        RoleManagementPolicy.CanEdit(Admin, admin).Reason.Should().Be("System roles can't be changed.");
        RoleManagementPolicy.CanDelete(Admin, admin).Allowed.Should().BeFalse();
    }

    [Fact]
    public void ARoleThatAllowsMoreThanTheCaller_CannotBeChangedOrDeleted_ByThem()
    {
        var beyond = Role(Permissions.PayrollManage);

        RoleManagementPolicy.CanEdit(Delegate, beyond).Reason.Should().Be("You can't change a role that allows things you can't do yourself.");
        RoleManagementPolicy.CanDelete(Delegate, beyond).Allowed.Should().BeFalse();
    }

    [Fact]
    public void ARoleWithinTheCallersPermissions_MayBeChangedAndDeleted()
    {
        RoleManagementPolicy.CanEdit(Delegate, Role(Permissions.RecruitmentManage)).Allowed.Should().BeTrue();
        RoleManagementPolicy.CanDelete(Delegate, Role(Permissions.RecruitmentManage)).Allowed.Should().BeTrue();
    }

    [Fact]
    public void NobodyMayGiveARolePermissionsTheyLack()
    {
        RoleManagementPolicy.CanCreate(Delegate, [Permissions.RecruitmentManage]).Allowed.Should().BeTrue();
        RoleManagementPolicy.CanCreate(Delegate, [Permissions.PayrollManage]).Reason
            .Should().Be("You can't give a role permissions you don't have yourself.");
        RoleManagementPolicy.CanUpdate(Delegate, Role(Permissions.RecruitmentManage), [Permissions.PayrollManage],
            actorPermissionsAfter: Delegate.Permissions).Reason
            .Should().Be("You can't give a role permissions you don't have yourself.");
    }

    [Fact]
    public void NobodyMayRemoveTheirOwnPermissionToManageRoles()
    {
        var theirOwn = new RoleSnapshot("Delegates", IsSystem: false, [Permissions.RolesManage, Permissions.UsersManage]);

        RoleManagementPolicy.CanUpdate(Delegate, theirOwn, [Permissions.UsersManage],
            actorPermissionsAfter: [Permissions.UsersManage, Permissions.RecruitmentManage]).Reason
            .Should().Be("You can't remove your own permission to manage roles.");
    }

    [Fact]
    public void NobodyMayRemoveTheirOwnPermissionToManageUsers()
    {
        var theirOwn = new RoleSnapshot("Delegates", IsSystem: false, [Permissions.RolesManage, Permissions.UsersManage]);

        RoleManagementPolicy.CanUpdate(Delegate, theirOwn, [Permissions.RolesManage],
            actorPermissionsAfter: [Permissions.RolesManage, Permissions.RecruitmentManage]).Reason
            .Should().Be("You can't remove your own permission to manage users.");
    }

    [Fact]
    public void AnAdmin_KeepsEveryPermission_SoTheLockoutGuardNeverTripsForThem()
    {
        RoleManagementPolicy.CanUpdate(Admin, Role(Permissions.RolesManage), [], actorPermissionsAfter: Permissions.AllKeys)
            .Allowed.Should().BeTrue();
    }
}
