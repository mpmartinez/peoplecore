using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class EmployeeAllowanceRepository : Repository<EmployeeAllowance>, IEmployeeAllowanceRepository
{
    public EmployeeAllowanceRepository(AppDbContext context) : base(context) { }

    public async Task<IReadOnlyList<EmployeeAllowance>> GetByEmployeeIdsAsync(
        IEnumerable<Guid> employeeIds, CancellationToken ct = default)
        => await Context.EmployeeAllowances
            .Where(a => employeeIds.Contains(a.EmployeeId))
            .ToListAsync(ct);
}
