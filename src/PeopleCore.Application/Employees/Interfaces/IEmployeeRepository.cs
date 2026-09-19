using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Employees.DTOs;
using PeopleCore.Domain.Entities.Employees;

namespace PeopleCore.Application.Employees.Interfaces;

public interface IEmployeeRepository : IRepository<Employee>
{
    Task<(IReadOnlyList<Employee> Items, int TotalCount)> GetPagedAsync(EmployeeFilterDto filter, CancellationToken ct = default);
    Task<bool> EmployeeNumberExistsAsync(string employeeNumber, CancellationToken ct = default);
    Task<Employee?> GetByNumberAsync(string employeeNumber, CancellationToken ct = default);

    /// <summary>The employee enrolled on the time clock under <paramref name="biometricId"/>, if any.</summary>
    Task<Employee?> GetByBiometricIdAsync(string biometricId, CancellationToken ct = default);

    /// <summary>Every employee's number, name and biometric id, with nothing else loaded - what an attendance import matches against.</summary>
    Task<IReadOnlyList<AttendanceImportEmployeeDto>> GetAttendanceImportKeysAsync(CancellationToken ct = default);

    /// <summary>
    /// Every employee whose id is in <paramref name="ids"/>, each loaded with only its
    /// <see cref="Employee.GovernmentIds"/> - what <c>Bir2316Service</c> needs for the TIN lookup.
    /// Deliberately lighter than <see cref="IRepository{T}.GetByIdAsync"/>'s six includes
    /// (Department, Position, ReportingManager, GovernmentIds, EmergencyContacts, Documents),
    /// none of which a 2316 certificate reads, and batched so a bulk build does not repeat that
    /// load once per employee.
    /// </summary>
    Task<IReadOnlyList<Employee>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);

    /// <summary>True when <paramref name="employeeId"/>'s reporting manager is <paramref name="managerId"/>.</summary>
    Task<bool> IsDirectReportAsync(Guid employeeId, Guid managerId, CancellationToken ct = default);

    /// <summary>
    /// Every employee, loaded with <see cref="Employee.Department"/>, <see cref="Employee.Position"/>
    /// and <see cref="Employee.GovernmentIds"/> - what the payroll master-data export reads.
    /// Deliberately heavier than <see cref="IRepository{T}.GetAllAsync"/>'s Department-only load: the
    /// other <c>GetAllAsync</c> callers (HR analytics, leave accrual) don't read Position or
    /// GovernmentIds, and GovernmentIds is a collection Include that multiplies rows those callers
    /// don't need.
    /// </summary>
    Task<IReadOnlyList<Employee>> GetAllForExportAsync(CancellationToken ct = default);
}
