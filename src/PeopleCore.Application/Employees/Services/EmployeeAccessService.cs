using PeopleCore.Application.Common.Authorization;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Employees.Interfaces;

namespace PeopleCore.Application.Employees.Services;

/// <inheritdoc cref="IEmployeeAccessService"/>
public class EmployeeAccessService : IEmployeeAccessService
{
    private readonly ICurrentUserService _currentUser;
    private readonly IEmployeeRepository _employees;

    public EmployeeAccessService(ICurrentUserService currentUser, IEmployeeRepository employees)
    {
        _currentUser = currentUser;
        _employees = employees;
    }

    public bool CanReachEveryone =>
        _currentUser.HasPermission(Permissions.EmployeesViewAll) || _currentUser.HasPermission(Permissions.ApprovalsAll);

    public async Task<bool> CanManageAsync(Guid employeeId, CancellationToken ct = default)
    {
        if (_currentUser.HasPermission(Permissions.ApprovalsAll))
            return true;

        return TeamManagerId() is { } managerId
               && await _employees.IsDirectReportAsync(employeeId, managerId, ct);
    }

    public async Task<bool> CanViewAsync(Guid employeeId, CancellationToken ct = default)
        => _currentUser.EmployeeId == employeeId
           || _currentUser.HasPermission(Permissions.EmployeesViewAll)
           || await CanManageAsync(employeeId, ct);

    public EmployeeListScope GetUnfilteredListScope()
    {
        if (CanReachEveryone)
            return EmployeeListScope.Everyone;

        return TeamManagerId() is { } managerId
            ? EmployeeListScope.DirectReportsOf(managerId)
            : EmployeeListScope.Denied;
    }

    /// <summary>
    /// The caller's employee id when they may approve for their team, otherwise null. Null too for a
    /// team approver with no employee_id claim: nobody can report to an account that is not an employee.
    /// </summary>
    private Guid? TeamManagerId()
        => _currentUser.HasPermission(Permissions.ApprovalsTeam) ? _currentUser.EmployeeId : null;
}
