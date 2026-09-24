using PeopleCore.Application.Leave.DTOs;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Domain.Entities.Leave;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.Application.Leave.Services;

public class LeaveTypeService : ILeaveTypeService
{
    private readonly ILeaveTypeRepository _repo;

    public LeaveTypeService(ILeaveTypeRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<LeaveTypeDto>> GetAllAsync(CancellationToken ct = default)
    {
        var types = await _repo.GetAllAsync(ct);
        return types.Select(ToDto).ToList();
    }

    public async Task<LeaveTypeDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var lt = await _repo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Leave type {id} not found.");
        return ToDto(lt);
    }

    public async Task<LeaveTypeDto> CreateAsync(CreateLeaveTypeDto dto, CancellationToken ct = default)
    {
        Validate(dto);
        var lt = new LeaveType();
        Apply(dto, lt);
        var created = await _repo.AddAsync(lt, ct);
        return ToDto(created);
    }

    public async Task<LeaveTypeDto> UpdateAsync(Guid id, CreateLeaveTypeDto dto, CancellationToken ct = default)
    {
        var lt = await _repo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Leave type {id} not found.");
        Validate(dto);
        Apply(dto, lt);
        lt.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(lt, ct);
        return ToDto(lt);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var lt = await _repo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Leave type {id} not found.");
        if (await _repo.IsUsedAsync(id, ct))
            throw new DomainException($"{lt.Name} has been used; deactivate it instead.");

        // The policies' foreign key restricts deletes. An unused type's policies have accrued
        // nothing (no transaction references it), so they go with it, in the same save.
        await _repo.DeleteWithPoliciesAsync(lt, ct);
    }

    public Task<StatutoryLeaveResultDto> AddStatutoryAsync(CancellationToken ct = default)
        => StatutoryLeaveSet.ApplyAsync(_repo, ct);

    private static void Validate(CreateLeaveTypeDto dto)
    {
        if (dto.EntitlementKind == LeaveEntitlementKind.PerEvent && !(dto.DaysPerEvent > 0))
            throw new DomainException("Set the days per event.");
        if (dto.EntitlementKind == LeaveEntitlementKind.YearlyAllowance && !(dto.MaxDaysPerYear > 0))
            throw new DomainException("Set the days per year.");
        if (dto.IsMaternity && dto.EntitlementKind != LeaveEntitlementKind.PerEvent)
            throw new DomainException("A maternity type must be per event.");
        // Only a per-event type keeps MaxEvents; on any other kind it is cleared, not checked.
        if (dto.EntitlementKind == LeaveEntitlementKind.PerEvent && dto.MaxEvents < 1)
            throw new DomainException("Set at least 1 for the most times allowed.");
        if (dto.MinServiceMonths < 0)
            throw new DomainException("Service months can't be negative.");
    }

    /// <summary>Writes every setting. Create and update are both full replacements.</summary>
    private static void Apply(CreateLeaveTypeDto dto, LeaveType lt)
    {
        lt.Name = dto.Name;
        lt.Code = dto.Code;
        lt.MaxDaysPerYear = dto.MaxDaysPerYear;
        lt.IsPaid = dto.IsPaid;
        lt.IsCarryOver = dto.IsCarryOver;
        lt.CarryOverMaxDays = dto.CarryOverMaxDays;
        // LeaveRules compares this to employee.Gender.ToString(); a blank would shut everyone out.
        lt.GenderRestriction = string.IsNullOrWhiteSpace(dto.GenderRestriction) ? null : dto.GenderRestriction.Trim();
        lt.RequiresDocument = dto.RequiresDocument;
        lt.IsConvertibleToCash = dto.IsConvertibleToCash;
        lt.CountsAsVacationForDeMinimis = dto.CountsAsVacationForDeMinimis;
        lt.IsActive = dto.IsActive;
        lt.EntitlementKind = dto.EntitlementKind;
        lt.CountsCalendarDays = dto.CountsCalendarDays;
        lt.DaysPerEvent = dto.DaysPerEvent;
        lt.MinServiceMonths = dto.MinServiceMonths;
        lt.RequiresMarried = dto.RequiresMarried;
        lt.RequiresSoloParentId = dto.RequiresSoloParentId;
        lt.MaxEvents = dto.EntitlementKind == LeaveEntitlementKind.PerEvent ? dto.MaxEvents : null;
        lt.IsConfidential = dto.IsConfidential;
        lt.IsMaternity = dto.IsMaternity;
    }

    private static LeaveTypeDto ToDto(LeaveType lt) => new(
        lt.Id, lt.Name, lt.Code, lt.MaxDaysPerYear, lt.IsPaid,
        lt.IsCarryOver, lt.CarryOverMaxDays, lt.GenderRestriction,
        lt.RequiresDocument, lt.IsActive,
        lt.IsConvertibleToCash, lt.CountsAsVacationForDeMinimis,
        lt.EntitlementKind, lt.CountsCalendarDays,
        lt.DaysPerEvent, lt.MinServiceMonths,
        lt.RequiresMarried, lt.RequiresSoloParentId,
        lt.MaxEvents, lt.IsConfidential, lt.IsMaternity);
}
