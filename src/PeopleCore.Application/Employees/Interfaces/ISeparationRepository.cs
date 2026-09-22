using PeopleCore.Domain.Entities.Employees;

namespace PeopleCore.Application.Employees.Interfaces;

public interface ISeparationRepository
{
    Task<Separation?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Separation?> GetOpenForEmployeeAsync(Guid employeeId, CancellationToken ct = default);
    Task<IReadOnlyList<Separation>> ListAsync(CancellationToken ct = default);
    Task AddAsync(Separation separation, CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default);
    Task DeleteAsync(Separation separation, CancellationToken ct = default);
}
