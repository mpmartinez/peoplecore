using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities.Attendance;

namespace PeopleCore.Application.Attendance.Interfaces;

public interface IHolidayRepository : IRepository<Holiday>
{
    Task<IReadOnlyList<Holiday>> GetByYearAsync(int year, CancellationToken ct = default);
    Task<Holiday?> GetByDateAsync(DateOnly date, CancellationToken ct = default);
    Task<IReadOnlyList<Holiday>> GetRecurringAsync(CancellationToken ct = default);
}
