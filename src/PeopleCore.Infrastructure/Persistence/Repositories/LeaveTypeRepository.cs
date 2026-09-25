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
           || await Context.LeaveBalances.AnyAsync(b => b.LeaveTypeId == id, ct)
           || await Context.LeaveAccrualTransactions.AnyAsync(t => t.LeaveTypeId == id, ct);

    public async Task<LeaveType> AddWithPoliciesAsync(
        LeaveType type, IReadOnlyList<LeaveAccrualPolicy> policies, CancellationToken ct = default)
    {
        // Every row is added explicitly: a new entity already carries its Guid, so one reached only
        // through a navigation would be taken for an existing row.
        Context.LeaveTypes.Add(type);
        Context.LeaveAccrualPolicies.AddRange(policies);
        await Context.SaveChangesAsync(ct);
        return type;
    }

    public async Task DeleteWithPoliciesAsync(LeaveType type, CancellationToken ct = default)
    {
        var policies = await Context.LeaveAccrualPolicies.Where(p => p.LeaveTypeId == type.Id).ToListAsync(ct);
        Context.LeaveAccrualPolicies.RemoveRange(policies);
        Context.LeaveTypes.Remove(type);
        await Context.SaveChangesAsync(ct);
    }
}
