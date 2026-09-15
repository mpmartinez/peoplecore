namespace PeopleCore.Application.Employees.Interfaces;

/// <summary>
/// Whose employee-scoped records (leave, attendance, overtime, schedules) the signed-in caller may
/// see and manage, by permission. "View all employees" sees everyone; "Approve for everyone" sees and
/// decides for everyone; "Approve for my team" sees and decides for the caller's direct reports - the
/// employees whose ReportingManagerId is the caller's own employee id. Everybody reaches themselves.
/// </summary>
public interface IEmployeeAccessService
{
    /// <summary>True when the caller may view all employees or approve for everyone.</summary>
    bool CanReachEveryone { get; }

    /// <summary>
    /// True when the caller may decide on <paramref name="employeeId"/>'s requests: an approver for
    /// everyone for anyone, a team approver for a direct report. Deliberately excludes the caller
    /// themselves - whether someone may decide their own request is a separate rule.
    /// </summary>
    Task<bool> CanManageAsync(Guid employeeId, CancellationToken ct = default);

    /// <summary>True when the caller is <paramref name="employeeId"/> or may manage them.</summary>
    Task<bool> CanViewAsync(Guid employeeId, CancellationToken ct = default);

    /// <summary>
    /// What a list asked for with no employee filter may contain: everyone for a caller who reaches
    /// everyone, a team approver's direct reports, and nothing (<see cref="EmployeeListScope.IsAllowed"/>
    /// false) for anyone else.
    /// </summary>
    EmployeeListScope GetUnfilteredListScope();
}

/// <summary>The rows an unfiltered list may return. See <see cref="IEmployeeAccessService.GetUnfilteredListScope"/>.</summary>
public sealed record EmployeeListScope(bool IsAllowed, Guid? ReportingManagerId)
{
    public static EmployeeListScope Denied { get; } = new(false, null);
    public static EmployeeListScope Everyone { get; } = new(true, null);
    public static EmployeeListScope DirectReportsOf(Guid managerId) => new(true, managerId);
}
