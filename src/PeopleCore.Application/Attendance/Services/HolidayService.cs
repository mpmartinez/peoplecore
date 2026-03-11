using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Domain.Entities.Attendance;

namespace PeopleCore.Application.Attendance.Services;

public class HolidayService : IHolidayService
{
    private readonly IHolidayRepository _repo;

    public HolidayService(IHolidayRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<HolidayDto>> GetByYearAsync(int year, CancellationToken ct = default)
    {
        var holidays = await _repo.GetByYearAsync(year, ct);
        return holidays.Select(ToDto).ToList();
    }

    public async Task<HolidayDto> CreateAsync(CreateHolidayDto dto, CancellationToken ct = default)
    {
        var holiday = new Holiday
        {
            Name = dto.Name,
            HolidayDate = dto.HolidayDate,
            HolidayType = dto.HolidayType,
            IsRecurring = dto.IsRecurring
        };
        var created = await _repo.AddAsync(holiday, ct);
        return ToDto(created);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var holiday = await _repo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Holiday {id} not found.");
        await _repo.DeleteAsync(holiday, ct);
    }

    public async Task<bool> IsHolidayAsync(DateOnly date, CancellationToken ct = default)
    {
        var holiday = await _repo.GetByDateAsync(date, ct);
        if (holiday is not null) return true;
        // Check recurring (same month+day regardless of year)
        var all = await _repo.GetByYearAsync(date.Year, ct);
        return all.Any(h => h.IsRecurring && h.HolidayDate.Month == date.Month && h.HolidayDate.Day == date.Day);
    }

    private static HolidayDto ToDto(Holiday h) => new(h.Id, h.Name, h.HolidayDate, h.HolidayType, h.IsRecurring);
}
