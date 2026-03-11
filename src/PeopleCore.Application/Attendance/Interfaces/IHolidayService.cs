using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Domain.Enums;

namespace PeopleCore.Application.Attendance.Interfaces;

public interface IHolidayService
{
    Task<IReadOnlyList<HolidayDto>> GetByYearAsync(int year, CancellationToken ct = default);
    Task<HolidayDto> CreateAsync(CreateHolidayDto dto, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<HolidayType?> IsHolidayAsync(DateOnly date, CancellationToken ct = default);
}
