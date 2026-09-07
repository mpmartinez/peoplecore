using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Employees.DTOs;
using PeopleCore.Domain.Entities.Employees;

namespace PeopleCore.Application.Employees.Interfaces;

public interface IEmployeeRepository : IRepository<Employee>
{
    Task<(IReadOnlyList<Employee> Items, int TotalCount)> GetPagedAsync(EmployeeFilterDto filter, CancellationToken ct = default);
    Task<bool> EmployeeNumberExistsAsync(string employeeNumber, CancellationToken ct = default);
    Task<Employee?> GetByNumberAsync(string employeeNumber, CancellationToken ct = default);

    /// <summary>
    /// Every employee whose id is in <paramref name="ids"/>, each loaded with only its
    /// <see cref="Employee.GovernmentIds"/> - what <c>Bir2316Service</c> needs for the TIN lookup.
    /// Deliberately lighter than <see cref="IRepository{T}.GetByIdAsync"/>'s six includes
    /// (Department, Position, ReportingManager, GovernmentIds, EmergencyContacts, Documents),
    /// none of which a 2316 certificate reads, and batched so a bulk build does not repeat that
    /// load once per employee.
    /// </summary>
    Task<IReadOnlyList<Employee>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
}
