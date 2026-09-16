using PeopleCore.Application.Common.Authorization;

namespace PeopleCore.API.Accounts;

/// <summary>A role as it stands, for deciding whether a caller may change it.</summary>
public sealed record RoleSnapshot(string Name, bool IsSystem, IReadOnlyList<string> Permissions);

/// <summary>
/// Who may change which role. Nobody puts a permission on a role, or changes a role that already has
/// one, unless they hold that permission themselves - otherwise "manage roles" would be a way to give
/// yourself anything. System roles are the app's to define. And nobody may edit away their own power
/// to manage roles or users, which would lock them out mid-task.
/// </summary>
public static class RoleManagementPolicy
{
    private const string SystemRole = "System roles can't be changed.";
    private const string BeyondRole = "You can't change a role that allows things you can't do yourself.";
    private const string BeyondGrant = "You can't give a role permissions you don't have yourself.";
    private const string OwnRolesManage = "You can't remove your own permission to manage roles.";
    private const string OwnUsersManage = "You can't remove your own permission to manage users.";

    public static PolicyDecision CanEdit(AccountActor actor, RoleSnapshot role)
    {
        if (role.IsSystem) return PolicyDecision.Deny(SystemRole);
        return ActorCoverage.Covers(actor, role.Permissions) ? PolicyDecision.Allow : PolicyDecision.Deny(BeyondRole);
    }

    public static PolicyDecision CanCreate(AccountActor actor, IReadOnlyCollection<string> permissions) =>
        ActorCoverage.Covers(actor, permissions) ? PolicyDecision.Allow : PolicyDecision.Deny(BeyondGrant);

    /// <param name="actorPermissionsAfter">What the caller would be able to do once the change is saved.</param>
    public static PolicyDecision CanUpdate(
        AccountActor actor, RoleSnapshot role, IReadOnlyCollection<string> newPermissions, IReadOnlyCollection<string> actorPermissionsAfter)
    {
        var edit = CanEdit(actor, role);
        if (!edit.Allowed) return edit;
        if (!ActorCoverage.Covers(actor, newPermissions)) return PolicyDecision.Deny(BeyondGrant);

        if (!actor.IsAdmin)
        {
            if (actor.Permissions.Contains(Permissions.RolesManage) && !actorPermissionsAfter.Contains(Permissions.RolesManage))
                return PolicyDecision.Deny(OwnRolesManage);
            if (actor.Permissions.Contains(Permissions.UsersManage) && !actorPermissionsAfter.Contains(Permissions.UsersManage))
                return PolicyDecision.Deny(OwnUsersManage);
        }

        return PolicyDecision.Allow;
    }

    /// <summary>Whether the role may be deleted as far as the caller goes; the controller separately refuses roles still held.</summary>
    public static PolicyDecision CanDelete(AccountActor actor, RoleSnapshot role) => CanEdit(actor, role);
}
