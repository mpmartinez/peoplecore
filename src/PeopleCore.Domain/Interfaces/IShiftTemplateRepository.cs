using PeopleCore.Domain.Entities.Scheduling;

namespace PeopleCore.Domain.Interfaces;

public interface IShiftTemplateRepository
{
    Task<IReadOnlyList<ShiftTemplate>> GetAllAsync(CancellationToken ct = default);
    Task<ShiftTemplate?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ShiftTemplate> AddAsync(ShiftTemplate entity, CancellationToken ct = default);
    Task UpdateAsync(ShiftTemplate entity, CancellationToken ct = default);
    Task DeleteAsync(ShiftTemplate entity, CancellationToken ct = default);
}
