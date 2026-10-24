namespace PeopleCore.Domain.Enums;

/// <summary>What kind of thing an audit entry records.</summary>
public enum AuditCategory
{
    /// <summary>A row was inserted, updated or deleted.</summary>
    DataChange,

    /// <summary>Sign-in, sign-out, passwords, accounts, roles, permissions, access denied.</summary>
    Security,

    /// <summary>A sensitive read or export: a payslip, a 2316, a government report.</summary>
    Access,

    /// <summary>A state-changing API call, by name, with its outcome.</summary>
    Action,
}

public enum AuditOutcome
{
    Success,
    Failure,
}
