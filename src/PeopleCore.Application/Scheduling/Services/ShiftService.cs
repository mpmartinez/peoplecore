using PeopleCore.Application.Scheduling.DTOs;
using PeopleCore.Application.Scheduling.Interfaces;
using PeopleCore.Domain.Entities.Scheduling;
using PeopleCore.Domain.Interfaces;

namespace PeopleCore.Application.Scheduling.Services;

public class ShiftService : IShiftService
{
    private readonly IShiftTemplateRepository _templates;
    private readonly IRotatingPatternRepository _patterns;
    private readonly IShiftAssignmentRepository _assignments;

    public ShiftService(
        IShiftTemplateRepository templates,
        IRotatingPatternRepository patterns,
        IShiftAssignmentRepository assignments)
    {
        _templates = templates;
        _patterns = patterns;
        _assignments = assignments;
    }

    // ── Shift Templates ──────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ShiftTemplateDto>> GetShiftTemplatesAsync(CancellationToken ct = default)
    {
        var entities = await _templates.GetAllAsync(ct);
        return entities.Select(ToDto).ToList();
    }

    public async Task<ShiftTemplateDto> CreateShiftTemplateAsync(CreateShiftTemplateRequest request, CancellationToken ct = default)
    {
        var entity = new ShiftTemplate
        {
            Name = request.Name,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            BreakMinutes = request.BreakMinutes,
            IsNightShift = request.IsNightShift
        };
        var created = await _templates.AddAsync(entity, ct);
        return ToDto(created);
    }

    public async Task UpdateShiftTemplateAsync(Guid id, CreateShiftTemplateRequest request, CancellationToken ct = default)
    {
        var entity = await _templates.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Shift template {id} not found.");

        entity.Name = request.Name;
        entity.StartTime = request.StartTime;
        entity.EndTime = request.EndTime;
        entity.BreakMinutes = request.BreakMinutes;
        entity.IsNightShift = request.IsNightShift;

        await _templates.UpdateAsync(entity, ct);
    }

    public async Task DeleteShiftTemplateAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _templates.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Shift template {id} not found.");
        await _templates.DeleteAsync(entity, ct);
    }

    // ── Rotating Patterns ────────────────────────────────────────────────────

    public async Task<IReadOnlyList<RotatingPatternDto>> GetRotatingPatternsAsync(CancellationToken ct = default)
    {
        var entities = await _patterns.GetAllAsync(ct);
        return entities.Select(ToPatternDto).ToList();
    }

    public async Task<RotatingPatternDto> CreateRotatingPatternAsync(CreateRotatingPatternRequest request, CancellationToken ct = default)
    {
        var entity = new RotatingPattern
        {
            Name = request.Name,
            CycleLengthDays = request.CycleLengthDays,
            Slots = request.Slots.Select(s => new RotatingPatternSlot
            {
                DayOffset = s.DayOffset,
                ShiftTemplateId = s.ShiftTemplateId
            }).ToList()
        };
        var created = await _patterns.AddAsync(entity, ct);
        return ToPatternDto(created);
    }

    public async Task DeleteRotatingPatternAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _patterns.GetByIdWithSlotsAsync(id, ct)
            ?? throw new KeyNotFoundException($"Rotating pattern {id} not found.");
        await _patterns.DeleteAsync(entity, ct);
    }

    // ── Assignments ──────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<DailyScheduleDto>> GetEmployeeScheduleAsync(
        Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var result = new List<DailyScheduleDto>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var resolved = await ResolveShiftForDayAsync(employeeId, d, ct);
            result.Add(resolved ?? new DailyScheduleDto(d, null, null, null, true, false));
        }
        return result;
    }

    public async Task AssignShiftAsync(AssignShiftRequest r, CancellationToken ct = default)
    {
        var entity = new EmployeeShiftAssignment
        {
            EmployeeId = r.EmployeeId,
            ShiftTemplateId = r.ShiftTemplateId,
            RotatingPatternId = r.RotatingPatternId,
            PatternStartDate = r.PatternStartDate,
            EffectiveFrom = r.EffectiveFrom,
            EffectiveTo = r.EffectiveTo
        };
        await _assignments.AddAsync(entity, ct);
    }

    public async Task RemoveAssignmentAsync(Guid assignmentId, CancellationToken ct = default)
    {
        var entity = await _assignments.GetByIdAsync(assignmentId, ct)
            ?? throw new KeyNotFoundException($"Assignment {assignmentId} not found.");
        await _assignments.DeleteAsync(entity, ct);
    }

    public async Task<DailyScheduleDto?> ResolveShiftForDayAsync(
        Guid employeeId, DateOnly date, CancellationToken ct = default)
    {
        var assignment = await _assignments.GetActiveAssignmentAsync(employeeId, date, ct);
        if (assignment is null) return null;

        // Fixed shift
        if (assignment.ShiftTemplateId.HasValue && assignment.ShiftTemplate is not null)
        {
            var s = assignment.ShiftTemplate;
            return new DailyScheduleDto(date, s.Name, s.StartTime, s.EndTime, false, s.IsNightShift);
        }

        // Rotating pattern
        if (assignment.RotatingPatternId.HasValue && assignment.RotatingPattern is not null)
        {
            var pattern = assignment.RotatingPattern;
            var anchorDate = assignment.PatternStartDate ?? assignment.EffectiveFrom;
            var rawOffset = (date.DayNumber - anchorDate.DayNumber) % pattern.CycleLengthDays;
            var dayOffset = rawOffset < 0 ? rawOffset + pattern.CycleLengthDays : rawOffset;
            var slot = pattern.Slots.FirstOrDefault(s => s.DayOffset == dayOffset);

            if (slot is null || slot.ShiftTemplateId is null || slot.ShiftTemplate is null)
                return new DailyScheduleDto(date, null, null, null, true, false); // rest day

            var st = slot.ShiftTemplate;
            return new DailyScheduleDto(date, st.Name, st.StartTime, st.EndTime, false, st.IsNightShift);
        }

        return null;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static ShiftTemplateDto ToDto(ShiftTemplate s) =>
        new(s.Id, s.Name, s.StartTime, s.EndTime, s.BreakMinutes, s.IsNightShift, s.IsActive);

    private static RotatingPatternDto ToPatternDto(RotatingPattern p) =>
        new(p.Id, p.Name, p.CycleLengthDays, p.IsActive,
            p.Slots.Select(s => new RotatingPatternSlotDto(s.Id, s.DayOffset, s.ShiftTemplateId, s.ShiftTemplate?.Name)).ToList());
}
