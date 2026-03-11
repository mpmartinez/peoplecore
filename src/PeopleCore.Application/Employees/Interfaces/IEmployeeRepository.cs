using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Employees.DTOs;
using PeopleCore.Domain.Entities.Employees;

namespace PeopleCore.Application.Employees.Interfaces;

public interface IEmployeeRepository : IRepository<Employee>
{
    Task<(IReadOnlyList<Employee> Items, int TotalCount)> GetPagedAsync(EmployeeFilterDto filter, CancellationToken ct = default);
    Task<bool> EmployeeNumberExistsAsync(string employeeNumber, CancellationToken ct = default);
}
