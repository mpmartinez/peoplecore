using Microsoft.EntityFrameworkCore;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Interfaces;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class LeaveAccrualRepository : ILeaveAccrualRepository
{
    private readonly AppDbContext _context;

    public LeaveAccrualRepository(AppDbContext context)
    {
        _context = context;
    }

    // Policies

    public async Task<IReadOnlyList<LeaveAccrualPolicy>> GetAllActivePoliciesAsync(CancellationToken ct = default)
        => await _context.LeaveAccrualPolicies
            .Include(p => p.LeaveType)
            .Where(p => p.IsActive)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<LeaveAccrualPolicy>> GetPoliciesByLeaveTypeAsync(Guid leaveTypeId, CancellationToken ct = default)
        => await _context.LeaveAccrualPolicies
            .Include(p => p.LeaveType)
            .Where(p => p.LeaveTypeId == leaveTypeId)
            .ToListAsync(ct);

    public async Task<LeaveAccrualPolicy?> GetPolicyByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.LeaveAccrualPolicies
            .Include(p => p.LeaveType)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<LeaveAccrualPolicy> AddPolicyAsync(LeaveAccrualPolicy policy, CancellationToken ct = default)
    {
        await _context.LeaveAccrualPolicies.AddAsync(policy, ct);
        await _context.SaveChangesAsync(ct);
        return policy;
    }

    public async Task UpdatePolicyAsync(LeaveAccrualPolicy policy, CancellationToken ct = default)
    {
        _context.LeaveAccrualPolicies.Update(policy);
        await _context.SaveChangesAsync(ct);
    }

    public async Task DeletePolicyAsync(LeaveAccrualPolicy policy, CancellationToken ct = default)
    {
        _context.LeaveAccrualPolicies.Remove(policy);
        await _context.SaveChangesAsync(ct);
    }

    // Transactions

    public async Task<bool> TransactionExistsAsync(Guid employeeId, Guid leaveTypeId, int year, int month, CancellationToken ct = default)
        => await _context.LeaveAccrualTransactions
            .AnyAsync(t => t.EmployeeId == employeeId
                        && t.LeaveTypeId == leaveTypeId
                        && t.PeriodYear == year
                        && t.PeriodMonth == month, ct);

    public async Task AddTransactionAsync(LeaveAccrualTransaction transaction, CancellationToken ct = default)
    {
        await _context.LeaveAccrualTransactions.AddAsync(transaction, ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<LeaveAccrualTransaction>> GetTransactionsByEmployeeAsync(Guid employeeId, CancellationToken ct = default)
        => await _context.LeaveAccrualTransactions
            .Include(t => t.LeaveType)
            .Where(t => t.EmployeeId == employeeId)
            .OrderByDescending(t => t.AccrualDate)
            .ToListAsync(ct);
}
