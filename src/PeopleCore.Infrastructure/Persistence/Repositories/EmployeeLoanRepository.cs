using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class EmployeeLoanRepository : Repository<EmployeeLoan>, IEmployeeLoanRepository
{
    public EmployeeLoanRepository(AppDbContext context) : base(context) { }

    public async Task<IReadOnlyList<EmployeeLoan>> GetByEmployeeIdsAsync(
        IEnumerable<Guid> employeeIds, CancellationToken ct = default)
        => await Context.EmployeeLoans
            .Where(l => employeeIds.Contains(l.EmployeeId))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<EmployeeLoan>> GetByIdsAsync(
        IEnumerable<Guid> ids, CancellationToken ct = default)
        => await Context.EmployeeLoans
            .Where(l => ids.Contains(l.Id))
            .ToListAsync(ct);

    public async Task UpdateRangeAsync(IEnumerable<EmployeeLoan> loans, CancellationToken ct = default)
    {
        Context.EmployeeLoans.UpdateRange(loans);
        await Context.SaveChangesAsync(ct);
    }
}
