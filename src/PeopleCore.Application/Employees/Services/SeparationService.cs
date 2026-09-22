using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Common.Time;
using PeopleCore.Application.Employees.DTOs;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.Application.Employees.Services;

/// <summary>
/// Recording, completing and cancelling separations, and the clearance checklist that gates final
/// pay. One separation per employee, enforced by <see cref="ISeparationRepository.GetOpenForEmployeeAsync"/>
/// reading the unique index rather than a status filter - a cancelled or completed separation still
/// blocks a new one from being recorded until the old row is gone.
/// </summary>
public class SeparationService : ISeparationService
{
    private static readonly string[] DefaultClearanceItems = ["HR", "IT", "Finance", "Immediate supervisor", "Property / admin"];
    private const int MaxClearanceItemNameLength = 100;

    private readonly ISeparationRepository _separations;
    private readonly IEmployeeRepository _employees;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _clock;

    public SeparationService(ISeparationRepository separations, IEmployeeRepository employees, ICurrentUserService currentUser, TimeProvider clock)
    {
        _separations = separations;
        _employees = employees;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<SeparationDto> RecordAsync(RecordSeparationRequest request, CancellationToken ct = default)
    {
        var employee = await _employees.GetByIdAsync(request.EmployeeId, ct)
            ?? throw new KeyNotFoundException($"Employee {request.EmployeeId} not found.");
        if (!employee.IsActive)
            throw new DomainException($"{employee.FullName} is no longer active.");
        if (await _separations.GetOpenForEmployeeAsync(request.EmployeeId, ct) is not null)
            throw new DomainException($"{employee.FullName} already has a separation recorded.");

        if (request.Type == SeparationType.AuthorizedCause && request.AuthorizedCause is null)
            throw new DomainException("Choose the authorized cause.");
        if (request.Type != SeparationType.AuthorizedCause && request.AuthorizedCause is not null)
            throw new DomainException("Only an authorized-cause separation has a cause.");
        if (request.LastWorkingDay < request.NoticeDate)
            throw new DomainException("The last working day can't be before the notice date.");

        var separation = new Separation
        {
            EmployeeId = request.EmployeeId,
            Employee = employee,
            Type = request.Type,
            AuthorizedCause = request.AuthorizedCause,
            NoticeDate = request.NoticeDate,
            LastWorkingDay = request.LastWorkingDay,
            Reason = request.Reason,
            Status = SeparationStatus.NoticeGiven,
            RecordedBy = _currentUser.Email ?? "",
            ClearanceItems = DefaultClearanceItems
                .Select((name, i) => new SeparationClearanceItem { Name = name, SortOrder = i })
                .ToList()
        };

        await _separations.AddAsync(separation, ct);
        return await GetAsync(separation.Id, ct) ?? throw new InvalidOperationException("The separation was not saved.");
    }

    public async Task<SeparationDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var separation = await _separations.GetAsync(id, ct);
        return separation is null ? null : ToDto(separation, Today());
    }

    public async Task<IReadOnlyList<SeparationDto>> ListAsync(CancellationToken ct = default)
    {
        var today = Today();
        return (await _separations.ListAsync(ct)).Select(s => ToDto(s, today)).ToList();
    }

    public async Task<SeparationDto> MarkSeparatedAsync(Guid id, CancellationToken ct = default)
    {
        var separation = await RequireAsync(id, ct);
        if (separation.Status != SeparationStatus.NoticeGiven)
            throw new DomainException("This separation is already complete.");

        return await CompleteAsync(separation, ignoreDateCheck: false, ct);
    }

    public async Task CancelAsync(Guid id, CancellationToken ct = default)
    {
        var separation = await RequireAsync(id, ct);
        if (separation.Status != SeparationStatus.NoticeGiven)
            throw new DomainException("A completed separation can't be cancelled.");

        await _separations.DeleteAsync(separation, ct);
    }

    public async Task<SeparationDto> AddClearanceItemAsync(Guid id, string name, CancellationToken ct = default)
    {
        var separation = await RequireAsync(id, ct);
        EnsureClearanceEditable(separation);

        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0)
            throw new DomainException("Give a name for the clearance item.");
        if (trimmed.Length > MaxClearanceItemNameLength)
            throw new DomainException($"Keep the clearance item name to {MaxClearanceItemNameLength} characters.");
        if (separation.ClearanceItems.Any(i => string.Equals(i.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
            throw new DomainException($"There's already a {trimmed} item.");

        separation.ClearanceItems.Add(new SeparationClearanceItem
        {
            SeparationId = separation.Id,
            Name = trimmed,
            SortOrder = separation.ClearanceItems.Count
        });
        await _separations.SaveAsync(ct);
        return ToDto(separation, Today());
    }

    public async Task<SeparationDto> ClearItemAsync(Guid id, Guid itemId, string? note, CancellationToken ct = default)
    {
        var separation = await RequireAsync(id, ct);
        EnsureClearanceEditable(separation);
        var item = RequireItem(separation, itemId);
        if (item.ClearedAt is not null)
            throw new DomainException($"{item.Name} is already cleared.");

        item.ClearedBy = _currentUser.Email;
        item.ClearedAt = PhilippineTime.Now(_clock);
        item.Note = note;
        await _separations.SaveAsync(ct);
        return ToDto(separation, Today());
    }

    public async Task<SeparationDto> UndoClearItemAsync(Guid id, Guid itemId, CancellationToken ct = default)
    {
        var separation = await RequireAsync(id, ct);
        EnsureClearanceEditable(separation);
        var item = RequireItem(separation, itemId);
        if (item.ClearedAt is null)
            throw new DomainException($"{item.Name} isn't cleared.");

        item.ClearedBy = null;
        item.ClearedAt = null;
        item.Note = null;
        await _separations.SaveAsync(ct);
        return ToDto(separation, Today());
    }

    public async Task<SeparationDto> DeleteClearanceItemAsync(Guid id, Guid itemId, CancellationToken ct = default)
    {
        var separation = await RequireAsync(id, ct);
        EnsureClearanceEditable(separation);
        var item = RequireItem(separation, itemId);
        if (item.ClearedAt is not null)
            throw new DomainException($"Undo {item.Name}'s clearance before removing it.");

        separation.ClearanceItems.Remove(item);
        await _separations.SaveAsync(ct);
        return ToDto(separation, Today());
    }

    public async Task<SeparationDto> SeparateNowAsync(Guid employeeId, DateOnly lastWorkingDay, SeparationType type, CancellationToken ct = default)
    {
        var existing = await _separations.GetOpenForEmployeeAsync(employeeId, ct);

        Separation separation;
        if (existing is { Status: SeparationStatus.NoticeGiven })
        {
            separation = existing;
            separation.LastWorkingDay = lastWorkingDay;
            separation.Type = type;
        }
        else
        {
            var recorded = await RecordAsync(
                new RecordSeparationRequest(employeeId, type, null, lastWorkingDay, lastWorkingDay, null), ct);
            separation = await _separations.GetAsync(recorded.Id, ct)
                ?? throw new InvalidOperationException("The separation was not saved.");
        }

        return await CompleteAsync(separation, ignoreDateCheck: true, ct);
    }

    // ── Rules ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Plan 2 fills this in: every clearance change is refused once the separation's final pay is Paid.</summary>
    private void EnsureClearanceEditable(Separation separation) { }

    private async Task<SeparationDto> CompleteAsync(Separation separation, bool ignoreDateCheck, CancellationToken ct)
    {
        var today = Today();
        if (!ignoreDateCheck && today < separation.LastWorkingDay)
            throw new DomainException(
                $"{separation.Employee.FullName}'s last working day is {separation.LastWorkingDay:MMM d, yyyy}; mark them separated on or after it.");

        separation.Status = SeparationStatus.Separated;
        separation.SeparatedBy = _currentUser.Email;
        separation.SeparatedAt = PhilippineTime.Now(_clock);

        var employee = separation.Employee ?? await _employees.GetByIdAsync(separation.EmployeeId, ct)
            ?? throw new KeyNotFoundException($"Employee {separation.EmployeeId} not found.");
        employee.IsActive = false;
        employee.SeparationDate = separation.LastWorkingDay;
        await _employees.UpdateAsync(employee, ct);
        await _separations.SaveAsync(ct);

        return ToDto(separation, today);
    }

    private async Task<Separation> RequireAsync(Guid id, CancellationToken ct)
        => await _separations.GetAsync(id, ct) ?? throw new KeyNotFoundException($"Separation {id} not found.");

    private static SeparationClearanceItem RequireItem(Separation separation, Guid itemId)
        => separation.ClearanceItems.FirstOrDefault(i => i.Id == itemId)
            ?? throw new KeyNotFoundException($"Clearance item {itemId} not found.");

    private DateOnly Today() => DateOnly.FromDateTime(PhilippineTime.Now(_clock));

    private static SeparationDto ToDto(Separation s, DateOnly today) => new(
        s.Id,
        s.EmployeeId,
        s.Employee.FullName,
        s.Employee.EmployeeNumber,
        s.Employee.Position?.Title,
        s.Type,
        s.AuthorizedCause,
        s.NoticeDate,
        s.LastWorkingDay,
        s.Reason,
        s.Status,
        s.RecordedBy,
        s.SeparatedBy,
        s.SeparatedAt,
        s.FinalPayDueBy,
        s.Status == SeparationStatus.Separated && s.FinalPayDueBy < today,
        s.ClearanceItems.Count(i => i.ClearedAt is not null),
        s.ClearanceItems.Count,
        s.ClearanceItems.OrderBy(i => i.SortOrder)
            .Select(i => new ClearanceItemDto(i.Id, i.Name, i.ClearedBy, i.ClearedAt, i.Note))
            .ToList());
}
