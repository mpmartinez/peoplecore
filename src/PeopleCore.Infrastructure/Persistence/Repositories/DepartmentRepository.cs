using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Organization.Interfaces;
using PeopleCore.Domain.Entities.Organization;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class DepartmentRepository : Repository<Department>, IDepartmentRepository
{
    public DepartmentRepository(AppDbContext context) : base(context) { }

    public async Task<(IReadOnlyList<Department> Items, int TotalCount)> GetPagedAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        var query = Context.Departments
            .Include(d => d.ParentDepartment)
            .Include(d => d.SubDepartments)
            .OrderBy(d => d.Name);

        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }
}
