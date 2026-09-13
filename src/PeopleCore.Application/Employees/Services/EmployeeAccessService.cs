using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Employees.Interfaces;

namespace PeopleCore.Application.Employees.Services;

/// <inheritdoc cref="IEmployeeAccessService"/>
public class EmployeeAccessService : IEmployeeAccessService
{
    private static readonly string[] HrRoles = ["Admin", "HRManager"];
    private const string ManagerRole = "Manager";

    private readonly ICurrentUserService _currentUser;
    private readonly IEmployeeRepository _employees;

    public EmployeeAccessService(ICurrentUserService currentUser, IEmployeeRepository employees)
    {
        _currentUser = currentUser;
        _employees = employees;
    }

    public bool IsHrStaff => HrRoles.Any(_currentUser.IsInRole);

    public async Task<bool> CanManageAsync(Guid employeeId, CancellationToken ct = default)
    {
        if (IsHrStaff)
            return true;

        return TeamManagerId() is { } managerId
               && await _employees.IsDirectReportAsync(employeeId, managerId, ct);
    }

    public async Task<bool> CanViewAsync(Guid employeeId, CancellationToken ct = default)
        => _currentUser.EmployeeId == employeeId || await CanManageAsync(employeeId, ct);

    public EmployeeListScope GetUnfilteredListScope()
    {
        if (IsHrStaff)
            return EmployeeListScope.Everyone;

        return TeamManagerId() is { } managerId
            ? EmployeeListScope.DirectReportsOf(managerId)
            : EmployeeListScope.Denied;
    }

    /// <summary>
    /// The caller's employee id when they hold the Manager role, otherwise null. Null for a Manager
    /// with no employee_id claim too: nobody can report to an account that is not an employee.
    /// </summary>
    private Guid? TeamManagerId()
        => _currentUser.IsInRole(ManagerRole) ? _currentUser.EmployeeId : null;
}
