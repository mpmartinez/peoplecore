using Microsoft.EntityFrameworkCore;
using PeopleCore.Domain.Entities.Scheduling;
using PeopleCore.Domain.Interfaces;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class ShiftAssignmentRepository : IShiftAssignmentRepository
{
    private readonly AppDbContext _context;

    public ShiftAssignmentRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<EmployeeShiftAssignment?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.ShiftAssignments.FindAsync([id], ct);

    public async Task<EmployeeShiftAssignment?> GetActiveAssignmentAsync(Guid employeeId, DateOnly date, CancellationToken ct = default)
    {
        return await _context.ShiftAssignments
            .Include(a => a.ShiftTemplate)
            .Include(a => a.RotatingPattern)
                .ThenInclude(p => p!.Slots)
                    .ThenInclude(s => s.ShiftTemplate)
            .Where(a => a.EmployeeId == employeeId
                && a.EffectiveFrom <= date
                && (a.EffectiveTo == null || a.EffectiveTo >= date))
            .OrderByDescending(a => a.EffectiveFrom)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<EmployeeShiftAssignment>> GetByEmployeeAsync(Guid employeeId, CancellationToken ct = default)
        => await _context.ShiftAssignments
            .Include(a => a.ShiftTemplate)
            .Include(a => a.RotatingPattern)
                .ThenInclude(p => p!.Slots)
                    .ThenInclude(s => s.ShiftTemplate)
            .Where(a => a.EmployeeId == employeeId)
            .OrderByDescending(a => a.EffectiveFrom)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<EmployeeShiftAssignment>> GetActiveForPeriodAsync(
        IReadOnlyList<Guid> employeeIds, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        return await _context.ShiftAssignments
            .Include(a => a.ShiftTemplate)
            .Include(a => a.RotatingPattern)
                .ThenInclude(p => p!.Slots)
                    .ThenInclude(s => s.ShiftTemplate)
            .Where(a => employeeIds.Contains(a.EmployeeId)
                && a.EffectiveFrom <= to
                && (a.EffectiveTo == null || a.EffectiveTo >= from))
            .OrderByDescending(a => a.EffectiveFrom)
            .ToListAsync(ct);
    }

    public async Task<EmployeeShiftAssignment> AddAsync(EmployeeShiftAssignment entity, CancellationToken ct = default)
    {
        await _context.ShiftAssignments.AddAsync(entity, ct);
        await _context.SaveChangesAsync(ct);
        return entity;
    }

    public async Task DeleteAsync(EmployeeShiftAssignment entity, CancellationToken ct = default)
    {
        _context.ShiftAssignments.Remove(entity);
        await _context.SaveChangesAsync(ct);
    }
}
