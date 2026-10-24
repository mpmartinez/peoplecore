namespace PeopleCore.API.Auditing;

/// <summary>
/// Names an action the audit trail records, in place of the default <c>Controller.Action</c>.
/// <see cref="IdRoute"/> names the route or query value holding the record's id, and
/// <see cref="EmployeeRoute"/> the one holding the employee it concerns.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AuditAsAttribute : Attribute
{
    public AuditAsAttribute(string action, string summary)
    {
        Action = action;
        Summary = summary;
    }

    public string Action { get; }
    public string Summary { get; }
    public string? EntityType { get; init; }
    public string? IdRoute { get; init; }
    public string? EmployeeRoute { get; init; }
}

/// <summary>
/// A sensitive read or export - a payslip, a 2316, a government report. Each successful call is
/// recorded in the audit trail as an access entry, with the arguments it was called with.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AuditAccessAttribute : Attribute
{
    public AuditAccessAttribute(string action, string summary)
    {
        Action = action;
        Summary = summary;
    }

    public string Action { get; }
    public string Summary { get; }
    public string? EntityType { get; init; }
    public string? IdRoute { get; init; }
    public string? EmployeeRoute { get; init; }

    /// <summary>The caller's own record - My Payslips - so the employee is the caller's.</summary>
    public bool SubjectIsCaller { get; init; }
}

/// <summary>
/// Keeps the action filter from recording this controller's calls, because the controller records
/// richer security events of its own. AuditCoverageTests pins which controllers may carry it.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class SkipAuditActionAttribute : Attribute { }
