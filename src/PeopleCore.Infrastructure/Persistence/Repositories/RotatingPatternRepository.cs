using Microsoft.EntityFrameworkCore;
using PeopleCore.Domain.Entities.Scheduling;
using PeopleCore.Domain.Interfaces;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class RotatingPatternRepository : IRotatingPatternRepository
{
    private readonly AppDbContext _context;

    public RotatingPatternRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<RotatingPattern>> GetAllAsync(CancellationToken ct = default)
        => await _context.RotatingPatterns.ToListAsync(ct);

    public async Task<RotatingPattern?> GetByIdWithSlotsAsync(Guid id, CancellationToken ct = default)
        => await _context.RotatingPatterns
            .Include(p => p.Slots)
                .ThenInclude(s => s.ShiftTemplate)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<RotatingPattern> AddAsync(RotatingPattern entity, CancellationToken ct = default)
    {
        await _context.RotatingPatterns.AddAsync(entity, ct);
        await _context.SaveChangesAsync(ct);
        return entity;
    }

    public async Task UpdateAsync(RotatingPattern entity, CancellationToken ct = default)
    {
        _context.RotatingPatterns.Update(entity);
        await _context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(RotatingPattern entity, CancellationToken ct = default)
    {
        _context.RotatingPatterns.Remove(entity);
        await _context.SaveChangesAsync(ct);
    }
}
