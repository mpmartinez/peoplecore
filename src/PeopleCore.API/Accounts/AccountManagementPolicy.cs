using PeopleCore.Application.Common.Authorization;
using PeopleCore.Infrastructure.Identity;

namespace PeopleCore.API.Accounts;

/// <summary>One role and what it allows - every permission for Admin.</summary>
public sealed record RoleGrant(string Name, IReadOnlyList<string> Permissions);

/// <summary>The signed-in caller acting on an account.</summary>
/// <param name="Permissions">What the caller's token says they may do.</param>
public sealed record AccountActor(string UserId, IReadOnlyCollection<string> Roles, IReadOnlyCollection<string> Permissions)
{
    public bool IsAdmin => Roles.Contains(SeededRoles.Admin);
}

/// <summary>The account being acted on, as it stands before the change.</summary>
public sealed record AccountTarget(string UserId, IReadOnlyCollection<string> Roles, bool IsActive)
{
    public bool IsAdmin => Roles.Contains(SeededRoles.Admin);
}

public readonly record struct PolicyDecision(bool Allowed, string? Reason)
{
    public static PolicyDecision Allow => new(true, null);

    public static PolicyDecision Deny(string reason) => new(false, reason);
}

/// <summary>A role a caller may be offered, and whether - and if not, why not - they may grant it.</summary>
public sealed record AssignableRole(string Name, bool Grantable, string? Reason);

/// <summary>
/// Who may change which account. Roles are data now, so the rule is about power rather than names:
/// nobody grants, removes or manages beyond the permissions they hold themselves, and only an Admin
/// touches Admin. Plain values in - the caller, the account, the roles and what each allows - and a
/// decision with a sentence out, shown to the caller as it is.
/// </summary>
public static class AccountManagementPolicy
{
    private const string AdminTarget = "Only an administrator can change an administrator's account.";
    private const string BeyondTarget = "You can't change this account: its roles allow things you can't do yourself.";
    private const string AdminRole = "Only an administrator can grant or remove the Admin role.";
    private const string ServiceRole = "The Service role is for attendance devices and can't be assigned.";
    private const string SelfLockout = "You can't remove your own permission to manage users.";
    private const string SelfDeactivation = "You can't deactivate your own account.";
    private const string SelfReset = "Use Change Password on My Profile to change your own password.";
    private const string LastAdmin = "This is the last active administrator. Make another account an Admin first.";

    public static IReadOnlyList<string> PermissionsOf(IEnumerable<string> roles, IReadOnlyList<RoleGrant> catalog)
    {
        var held = roles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return held.Contains(SeededRoles.Admin)
            ? Permissions.AllKeys
            : catalog.Where(r => held.Contains(r.Name)).SelectMany(r => r.Permissions).Distinct().ToList();
    }

    public static PolicyDecision CanGrant(AccountActor actor, string role, IReadOnlyList<RoleGrant> catalog)
    {
        if (string.Equals(role, SeededRoles.Service, StringComparison.OrdinalIgnoreCase)) return PolicyDecision.Deny(ServiceRole);
        if (string.Equals(role, SeededRoles.Admin, StringComparison.OrdinalIgnoreCase))
            return actor.IsAdmin ? PolicyDecision.Allow : PolicyDecision.Deny(AdminRole);

        var permissions = catalog.FirstOrDefault(r => string.Equals(r.Name, role, StringComparison.OrdinalIgnoreCase))?.Permissions ?? [];
        return Covers(actor, permissions)
            ? PolicyDecision.Allow
            : PolicyDecision.Deny($"You can't grant or remove the {role} role: it allows things you can't do yourself.");
    }

    public static IReadOnlyList<AssignableRole> AssignableRoles(AccountActor actor, IReadOnlyList<RoleGrant> catalog) =>
        catalog.Where(r => r.Name != SeededRoles.Service)
            .Select(r =>
            {
                var decision = CanGrant(actor, r.Name, catalog);
                return new AssignableRole(r.Name, decision.Allowed, decision.Reason);
            })
            .ToList();

    /// <summary>Whether the actor may change the target at all. Every other check starts here.</summary>
    public static PolicyDecision CanManage(AccountActor actor, AccountTarget target, IReadOnlyList<RoleGrant> catalog)
    {
        if (target.IsAdmin && !actor.IsAdmin) return PolicyDecision.Deny(AdminTarget);
        return Covers(actor, PermissionsOf(target.Roles, catalog)) ? PolicyDecision.Allow : PolicyDecision.Deny(BeyondTarget);
    }

    public static PolicyDecision CanCreate(AccountActor actor, IReadOnlyCollection<string> roles, IReadOnlyList<RoleGrant> catalog) =>
        FirstRefusal(actor, roles, catalog);

    public static PolicyDecision CanSetRoles(
        AccountActor actor, AccountTarget target, IReadOnlyCollection<string> rolesAfter, IReadOnlyList<RoleGrant> catalog, int activeAdmins)
    {
        var manage = CanManage(actor, target, catalog);
        if (!manage.Allowed) return manage;

        var before = target.Roles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var after = rolesAfter.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // A role nobody can assign (Service) that the account already holds and keeps is not a change.
        var changed = before.Except(after, StringComparer.OrdinalIgnoreCase)
            .Concat(after.Except(before, StringComparer.OrdinalIgnoreCase))
            .ToList();
        var refusal = FirstRefusal(actor, changed, catalog);
        if (!refusal.Allowed) return refusal;

        if (actor.UserId == target.UserId
            && PermissionsOf(before, catalog).Contains(Permissions.UsersManage)
            && !PermissionsOf(after, catalog).Contains(Permissions.UsersManage))
            return PolicyDecision.Deny(SelfLockout);

        if (IsLastActiveAdmin(target, activeAdmins) && !after.Contains(SeededRoles.Admin))
            return PolicyDecision.Deny(LastAdmin);

        return PolicyDecision.Allow;
    }

    public static PolicyDecision CanLinkEmployee(AccountActor actor, AccountTarget target, IReadOnlyList<RoleGrant> catalog) =>
        CanManage(actor, target, catalog);

    public static PolicyDecision CanDeactivate(AccountActor actor, AccountTarget target, IReadOnlyList<RoleGrant> catalog, int activeAdmins)
    {
        var manage = CanManage(actor, target, catalog);
        if (!manage.Allowed) return manage;
        if (actor.UserId == target.UserId) return PolicyDecision.Deny(SelfDeactivation);
        return IsLastActiveAdmin(target, activeAdmins) ? PolicyDecision.Deny(LastAdmin) : PolicyDecision.Allow;
    }

    public static PolicyDecision CanReactivate(AccountActor actor, AccountTarget target, IReadOnlyList<RoleGrant> catalog) =>
        CanManage(actor, target, catalog);

    public static PolicyDecision CanResetPassword(AccountActor actor, AccountTarget target, IReadOnlyList<RoleGrant> catalog) =>
        actor.UserId == target.UserId ? PolicyDecision.Deny(SelfReset) : CanManage(actor, target, catalog);

    // "Approve for everyone" covers "Approve for my team" (Permissions.WithImplied), so HR - which
    // approves for everyone - may still grant Manager, as it could before permissions existed.
    private static bool Covers(AccountActor actor, IEnumerable<string> permissions)
    {
        if (actor.IsAdmin) return true;
        var held = Permissions.WithImplied(actor.Permissions);
        return permissions.All(held.Contains);
    }

    private static PolicyDecision FirstRefusal(AccountActor actor, IEnumerable<string> roles, IReadOnlyList<RoleGrant> catalog) =>
        roles.Select(role => CanGrant(actor, role, catalog)).FirstOrDefault(d => !d.Allowed, PolicyDecision.Allow);

    private static bool IsLastActiveAdmin(AccountTarget target, int activeAdmins) =>
        target.IsActive && target.IsAdmin && activeAdmins <= 1;
}
