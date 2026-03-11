using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Employees.DTOs;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Domain.Entities.Employees;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class EmployeeRepository : Repository<Employee>, IEmployeeRepository
{
    public EmployeeRepository(AppDbContext context) : base(context) { }

    public async Task<(IReadOnlyList<Employee> Items, int TotalCount)> GetPagedAsync(
        EmployeeFilterDto filter, CancellationToken ct = default)
    {
        var query = Context.Employees
            .Include(e => e.Department)
            .Include(e => e.Position)
            .Include(e => e.ReportingManager)
            .Include(e => e.GovernmentIds)
            .Include(e => e.EmergencyContacts)
            .Include(e => e.Documents)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(e =>
                e.FirstName.Contains(filter.Search) ||
                e.LastName.Contains(filter.Search) ||
                e.EmployeeNumber.Contains(filter.Search) ||
                e.WorkEmail.Contains(filter.Search));

        if (filter.DepartmentId.HasValue)
            query = query.Where(e => e.DepartmentId == filter.DepartmentId);

        if (filter.Status.HasValue)
            query = query.Where(e => e.EmploymentStatus == filter.Status);

        if (filter.IsActive.HasValue)
            query = query.Where(e => e.IsActive == filter.IsActive);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(e => e.LastName).ThenBy(e => e.FirstName)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<bool> EmployeeNumberExistsAsync(string employeeNumber, CancellationToken ct = default)
        => await Context.Employees.AnyAsync(e => e.EmployeeNumber == employeeNumber, ct);
}
