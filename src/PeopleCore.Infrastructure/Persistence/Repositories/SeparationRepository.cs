using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Domain.Entities.Employees;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class SeparationRepository : ISeparationRepository
{
    private readonly AppDbContext _context;

    public SeparationRepository(AppDbContext context) => _context = context;

    private IQueryable<Separation> WithDetails() =>
        _context.Separations.Include(s => s.ClearanceItems).Include(s => s.Employee).ThenInclude(e => e.Position);

    public async Task<Separation?> GetAsync(Guid id, CancellationToken ct = default)
        => await WithDetails().FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<Separation?> GetOpenForEmployeeAsync(Guid employeeId, CancellationToken ct = default)
        => await WithDetails().FirstOrDefaultAsync(s => s.EmployeeId == employeeId, ct);

    public async Task<IReadOnlyList<Separation>> ListAsync(CancellationToken ct = default)
        => await WithDetails().OrderByDescending(s => s.LastWorkingDay).AsSplitQuery().ToListAsync(ct);

    public async Task AddAsync(Separation separation, CancellationToken ct = default)
    {
        _context.Separations.Add(separation);
        await _context.SaveChangesAsync(ct);
    }

    public Task SaveAsync(CancellationToken ct = default) => _context.SaveChangesAsync(ct);

    public async Task DeleteAsync(Separation separation, CancellationToken ct = default)
    {
        _context.Separations.Remove(separation);
        await _context.SaveChangesAsync(ct);
    }
}
