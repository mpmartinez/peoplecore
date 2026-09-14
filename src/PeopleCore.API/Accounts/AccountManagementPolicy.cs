namespace PeopleCore.API.Accounts;

/// <summary>The signed-in caller acting on an account.</summary>
public sealed record AccountActor(string UserId, IReadOnlyCollection<string> Roles)
{
    public bool IsAdmin => Roles.Contains(AccountRoles.Admin);
}

/// <summary>The account being acted on, as it stands before the change.</summary>
public sealed record AccountTarget(string UserId, IReadOnlyCollection<string> Roles, bool IsActive);

public readonly record struct PolicyDecision(bool Allowed, string? Reason)
{
    public static PolicyDecision Allow => new(true, null);

    public static PolicyDecision Deny(string reason) => new(false, reason);
}

/// <summary>
/// Who may change which account. Plain values in, a decision and a sentence out - no Identity, no
/// database - so every combination is unit tested. The sentence is shown to the caller as it is.
/// </summary>
public static class AccountManagementPolicy
{
    private const string PrivilegedTarget = "Only an administrator can change an Admin or HR Manager account.";
    private const string SelfDemotion = "You can't remove your own Admin or HR Manager role.";
    private const string SelfDeactivation = "You can't deactivate your own account.";
    private const string SelfReset = "Use Change Password on My Profile to change your own password.";
    private const string LastAdmin = "This is the last active administrator. Make another account an Admin first.";

    public static IReadOnlyList<string> AssignableRolesFor(AccountActor actor) =>
        actor.IsAdmin
            ? AccountRoles.Assignable
            : AccountRoles.Assignable.Where(role => !AccountRoles.Privileged.Contains(role)).ToList();

    /// <summary>Whether the actor may change the target at all. Every other check starts here.</summary>
    public static PolicyDecision CanManage(AccountActor actor, AccountTarget target) =>
        actor.IsAdmin || !AccountRoles.IsPrivileged(target.Roles)
            ? PolicyDecision.Allow
            : PolicyDecision.Deny(PrivilegedTarget);

    public static PolicyDecision CanCreate(AccountActor actor, IReadOnlyCollection<string> roles) =>
        FirstUngrantable(actor, roles) is { } role ? Ungrantable(role) : PolicyDecision.Allow;

    public static PolicyDecision CanSetRoles(
        AccountActor actor, AccountTarget target, IReadOnlyCollection<string> roles, int activeAdmins)
    {
        var manage = CanManage(actor, target);
        if (!manage.Allowed) return manage;

        // Only assignable roles are compared: one nobody can assign (Service) stays where it is.
        var held = target.Roles.Where(role => AccountRoles.Assignable.Contains(role)).ToHashSet();
        var changed = held.Except(roles).Concat(roles.Except(held)).ToList();
        if (FirstUngrantable(actor, changed) is { } forbidden) return Ungrantable(forbidden);

        var losesPrivilege = AccountRoles.Privileged.Any(role => held.Contains(role) && !roles.Contains(role));
        if (losesPrivilege && actor.UserId == target.UserId) return PolicyDecision.Deny(SelfDemotion);

        if (IsLastActiveAdmin(target, activeAdmins) && !roles.Contains(AccountRoles.Admin))
            return PolicyDecision.Deny(LastAdmin);

        return PolicyDecision.Allow;
    }

    public static PolicyDecision CanLinkEmployee(AccountActor actor, AccountTarget target) => CanManage(actor, target);

    public static PolicyDecision CanDeactivate(AccountActor actor, AccountTarget target, int activeAdmins)
    {
        var manage = CanManage(actor, target);
        if (!manage.Allowed) return manage;
        if (actor.UserId == target.UserId) return PolicyDecision.Deny(SelfDeactivation);
        return IsLastActiveAdmin(target, activeAdmins) ? PolicyDecision.Deny(LastAdmin) : PolicyDecision.Allow;
    }

    public static PolicyDecision CanReactivate(AccountActor actor, AccountTarget target) => CanManage(actor, target);

    public static PolicyDecision CanResetPassword(AccountActor actor, AccountTarget target) =>
        actor.UserId == target.UserId ? PolicyDecision.Deny(SelfReset) : CanManage(actor, target);

    private static string? FirstUngrantable(AccountActor actor, IEnumerable<string> roles)
    {
        var grantable = AssignableRolesFor(actor);
        return roles.FirstOrDefault(role => !grantable.Contains(role));
    }

    private static PolicyDecision Ungrantable(string role) =>
        PolicyDecision.Deny($"Only an administrator can grant or remove the {role} role.");

    private static bool IsLastActiveAdmin(AccountTarget target, int activeAdmins) =>
        target.IsActive && target.Roles.Contains(AccountRoles.Admin) && activeAdmins <= 1;
}
