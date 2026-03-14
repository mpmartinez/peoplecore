namespace PeopleCore.Application.Scheduling.DTOs;

public record ShiftTemplateDto(
    Guid Id, string Name, TimeOnly StartTime, TimeOnly EndTime,
    int BreakMinutes, bool IsNightShift, bool IsActive);

public record CreateShiftTemplateRequest(
    string Name, TimeOnly StartTime, TimeOnly EndTime,
    int BreakMinutes, bool IsNightShift);

public record RotatingPatternSlotDto(
    Guid Id, int DayOffset, Guid? ShiftTemplateId, string? ShiftName);

public record RotatingPatternDto(
    Guid Id, string Name, int CycleLengthDays, bool IsActive,
    IReadOnlyList<RotatingPatternSlotDto> Slots);

public record CreateRotatingPatternRequest(
    string Name, int CycleLengthDays,
    IReadOnlyList<CreateSlotRequest> Slots);

public record CreateSlotRequest(int DayOffset, Guid? ShiftTemplateId);

public record AssignShiftRequest(
    Guid EmployeeId,
    Guid? ShiftTemplateId,
    Guid? RotatingPatternId,
    DateOnly? PatternStartDate,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo);

public record DailyScheduleDto(
    DateOnly Date,
    string? ShiftName,
    TimeOnly? StartTime,
    TimeOnly? EndTime,
    bool IsRestDay,
    bool IsNightShift);
