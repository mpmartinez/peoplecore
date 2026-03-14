using Microsoft.EntityFrameworkCore;
using PeopleCore.Domain.Entities.Scheduling;
using PeopleCore.Domain.Interfaces;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class ShiftTemplateRepository : IShiftTemplateRepository
{
    private readonly AppDbContext _context;

    public ShiftTemplateRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<ShiftTemplate>> GetAllAsync(CancellationToken ct = default)
        => await _context.ShiftTemplates.ToListAsync(ct);

    public async Task<ShiftTemplate?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.ShiftTemplates.FindAsync([id], ct);

    public async Task<ShiftTemplate> AddAsync(ShiftTemplate entity, CancellationToken ct = default)
    {
        await _context.ShiftTemplates.AddAsync(entity, ct);
        await _context.SaveChangesAsync(ct);
        return entity;
    }

    public async Task UpdateAsync(ShiftTemplate entity, CancellationToken ct = default)
    {
        _context.ShiftTemplates.Update(entity);
        await _context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(ShiftTemplate entity, CancellationToken ct = default)
    {
        _context.ShiftTemplates.Remove(entity);
        await _context.SaveChangesAsync(ct);
    }
}
