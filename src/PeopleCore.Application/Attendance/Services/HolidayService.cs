using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Domain.Entities.Attendance;
using PeopleCore.Domain.Enums;

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

    public async Task<HolidayType?> IsHolidayAsync(DateOnly date, CancellationToken ct = default)
    {
        // Check exact date match first
        var holiday = await _repo.GetByDateAsync(date, ct);
        if (holiday is not null) return holiday.HolidayType;

        // Check recurring holidays (year-agnostic)
        var recurring = await _repo.GetRecurringAsync(ct);
        var match = recurring.FirstOrDefault(h => h.HolidayDate.Month == date.Month && h.HolidayDate.Day == date.Day);
        return match?.HolidayType;
    }

    private static HolidayDto ToDto(Holiday h) => new(h.Id, h.Name, h.HolidayDate, h.HolidayType, h.IsRecurring);
}
