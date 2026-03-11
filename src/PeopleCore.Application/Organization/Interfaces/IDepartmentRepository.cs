using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities.Organization;

namespace PeopleCore.Application.Organization.Interfaces;

public interface IDepartmentRepository : IRepository<Department>
{
    Task<(IReadOnlyList<Department> Items, int TotalCount)> GetPagedAsync(int page, int pageSize, CancellationToken ct = default);
}
