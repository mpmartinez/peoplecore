using PeopleCore.Application.Attendance.DTOs;

namespace PeopleCore.Application.Attendance.Interfaces;

public interface IHolidayService
{
    Task<IReadOnlyList<HolidayDto>> GetByYearAsync(int year, CancellationToken ct = default);
    Task<HolidayDto> CreateAsync(CreateHolidayDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<bool> IsHolidayAsync(DateOnly date, CancellationToken ct = default);
}
