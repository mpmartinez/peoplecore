namespace PeopleCore.Application.Common.Auditing;

/// <summary>The names of the audit entries written explicitly. Persisted, so renamed only with care.</summary>
public static class AuditActions
{
    // Sign-in and passwords
    public const string LoginSucceeded = "auth.login-succeeded";
    public const string LoginFailed = "auth.login-failed";
    public const string Logout = "auth.logout";
    public const string PasswordChanged = "auth.password-changed";
    public const string PasswordChangeFailed = "auth.password-change-failed";
    public const string PasswordResetRequested = "auth.password-reset-requested";
    public const string PasswordReset = "auth.password-reset";
    public const string PasswordResetFailed = "auth.password-reset-failed";

    // Accounts
    public const string AccountCreated = "account.created";
    public const string AccountRolesChanged = "account.roles-changed";
    public const string AccountEmployeeLinked = "account.employee-linked";
    public const string AccountDeactivated = "account.deactivated";
    public const string AccountReactivated = "account.reactivated";
    public const string AccountPasswordReset = "account.password-reset";
    public const string AccountChangeRefused = "account.change-refused";

    // Roles and settings
    public const string RoleCreated = "role.created";
    public const string RoleUpdated = "role.updated";
    public const string RoleDeleted = "role.deleted";
    public const string RoleChangeRefused = "role.change-refused";
    public const string EmailSettingsChanged = "settings.email-changed";

    public const string AccessDenied = "security.access-denied";
}
