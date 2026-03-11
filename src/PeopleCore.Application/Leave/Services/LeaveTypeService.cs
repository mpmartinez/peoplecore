using PeopleCore.Application.Leave.DTOs;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Domain.Entities.Leave;

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
        var lt = new LeaveType
        {
            Name = dto.Name,
            Code = dto.Code,
            MaxDaysPerYear = dto.MaxDaysPerYear,
            IsPaid = dto.IsPaid,
            IsCarryOver = dto.IsCarryOver,
            CarryOverMaxDays = dto.CarryOverMaxDays,
            GenderRestriction = dto.GenderRestriction,
            RequiresDocument = dto.RequiresDocument,
            IsActive = true
        };
        var created = await _repo.AddAsync(lt, ct);
        return ToDto(created);
    }

    public async Task<LeaveTypeDto> UpdateAsync(Guid id, CreateLeaveTypeDto dto, CancellationToken ct = default)
    {
        var lt = await _repo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Leave type {id} not found.");
        lt.Name = dto.Name;
        lt.Code = dto.Code;
        lt.MaxDaysPerYear = dto.MaxDaysPerYear;
        lt.IsPaid = dto.IsPaid;
        lt.IsCarryOver = dto.IsCarryOver;
        lt.CarryOverMaxDays = dto.CarryOverMaxDays;
        lt.GenderRestriction = dto.GenderRestriction;
        lt.RequiresDocument = dto.RequiresDocument;
        lt.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(lt, ct);
        return ToDto(lt);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var lt = await _repo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Leave type {id} not found.");
        await _repo.DeleteAsync(lt, ct);
    }

    private static LeaveTypeDto ToDto(LeaveType lt) => new(
        lt.Id, lt.Name, lt.Code, lt.MaxDaysPerYear, lt.IsPaid,
        lt.IsCarryOver, lt.CarryOverMaxDays, lt.GenderRestriction,
        lt.RequiresDocument, lt.IsActive);
}
