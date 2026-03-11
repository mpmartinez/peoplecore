using Microsoft.EntityFrameworkCore;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Domain.Entities.Attendance;
using PeopleCore.Infrastructure.Persistence;

namespace PeopleCore.Infrastructure.Persistence.Repositories;

public class HolidayRepository : Repository<Holiday>, IHolidayRepository
{
    public HolidayRepository(AppDbContext context) : base(context) { }

    public async Task<IReadOnlyList<Holiday>> GetByYearAsync(int year, CancellationToken ct = default)
        => await Context.Holidays
            .Where(h => h.HolidayDate.Year == year)
            .OrderBy(h => h.HolidayDate)
            .ToListAsync(ct);

    public async Task<Holiday?> GetByDateAsync(DateOnly date, CancellationToken ct = default)
        => await Context.Holidays.FirstOrDefaultAsync(h => h.HolidayDate == date, ct);

    public async Task<IReadOnlyList<Holiday>> GetRecurringAsync(CancellationToken ct = default)
        => await Context.Holidays.Where(h => h.IsRecurring).ToListAsync(ct);
}
