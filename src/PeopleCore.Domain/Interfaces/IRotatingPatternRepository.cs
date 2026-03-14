using PeopleCore.Domain.Entities.Scheduling;

namespace PeopleCore.Domain.Interfaces;

public interface IRotatingPatternRepository
{
    Task<IReadOnlyList<RotatingPattern>> GetAllAsync(CancellationToken ct = default);
    Task<RotatingPattern?> GetByIdWithSlotsAsync(Guid id, CancellationToken ct = default);
    Task<RotatingPattern> AddAsync(RotatingPattern entity, CancellationToken ct = default);
    Task UpdateAsync(RotatingPattern entity, CancellationToken ct = default);
    Task DeleteAsync(RotatingPattern entity, CancellationToken ct = default);
}
