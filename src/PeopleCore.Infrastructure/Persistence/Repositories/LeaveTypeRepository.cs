using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class LeaveTypeRepository : Repository<LeaveType>, ILeaveTypeRepository
{
    public LeaveTypeRepository(AppDbContext context) : base(context) { }

    public async Task<LeaveType?> GetByCodeAsync(string code, CancellationToken ct = default)
        => await Context.LeaveTypes.FirstOrDefaultAsync(lt => lt.Code == code, ct);

    public async Task<bool> IsUsedAsync(Guid id, CancellationToken ct = default)
        => await Context.LeaveRequests.AnyAsync(r => r.LeaveTypeId == id, ct)
           || await Context.LeaveBalances.AnyAsync(b => b.LeaveTypeId == id, ct);
}
