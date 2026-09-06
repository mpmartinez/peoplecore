using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class EmployeeCompensationRepository : Repository<EmployeeCompensation>, IEmployeeCompensationRepository
{
    public EmployeeCompensationRepository(AppDbContext context) : base(context) { }

    public async Task<EmployeeCompensation?> GetByEmployeeIdAsync(Guid employeeId, CancellationToken ct = default)
        => await Context.EmployeeCompensations.FirstOrDefaultAsync(c => c.EmployeeId == employeeId, ct);

    public async Task<IReadOnlyList<EmployeeCompensation>> GetByEmployeeIdsAsync(
        IEnumerable<Guid> employeeIds, CancellationToken ct = default)
        => await Context.EmployeeCompensations
            .Where(c => employeeIds.Contains(c.EmployeeId))
            .ToListAsync(ct);
}
