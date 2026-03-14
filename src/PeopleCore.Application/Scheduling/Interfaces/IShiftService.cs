using PeopleCore.Application.Scheduling.DTOs;

namespace PeopleCore.Application.Scheduling.Interfaces;

public interface IShiftService
{
    // Shift Templates
    Task<IReadOnlyList<ShiftTemplateDto>> GetShiftTemplatesAsync(CancellationToken ct = default);
    Task<ShiftTemplateDto> CreateShiftTemplateAsync(CreateShiftTemplateRequest request, CancellationToken ct = default);
    Task UpdateShiftTemplateAsync(Guid id, CreateShiftTemplateRequest request, CancellationToken ct = default);
    Task DeleteShiftTemplateAsync(Guid id, CancellationToken ct = default);

    // Rotating Patterns
    Task<IReadOnlyList<RotatingPatternDto>> GetRotatingPatternsAsync(CancellationToken ct = default);
    Task<RotatingPatternDto> CreateRotatingPatternAsync(CreateRotatingPatternRequest request, CancellationToken ct = default);
    Task DeleteRotatingPatternAsync(Guid id, CancellationToken ct = default);

    // Assignments
    Task<IReadOnlyList<DailyScheduleDto>> GetEmployeeScheduleAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task AssignShiftAsync(AssignShiftRequest request, CancellationToken ct = default);
    Task RemoveAssignmentAsync(Guid assignmentId, CancellationToken ct = default);

    // Internal use by AttendanceService
    Task<DailyScheduleDto?> ResolveShiftForDayAsync(Guid employeeId, DateOnly date, CancellationToken ct = default);
}
