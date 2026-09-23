using System.Globalization;
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
    private const int MaxReasonLength = 1000;
    private const int MaxClearanceNoteLength = 500;

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

        // Someone deactivated before separations were tracked can still have one recorded, so
        // their final pay can be made - but only as what already happened: they are separated,
        // and on the date they were deactivated with.
        bool alreadyLeft = !employee.IsActive;
        if (alreadyLeft && employee.SeparationDate is null)
            throw new DomainException($"{employee.FullName} is no longer active.");
        if (await _separations.GetOpenForEmployeeAsync(request.EmployeeId, ct) is not null)
            throw new DomainException($"{employee.FullName} already has a separation recorded.");
        if (alreadyLeft && request.LastWorkingDay != employee.SeparationDate)
            throw new DomainException(
                $"{employee.FullName} left on " +
                $"{employee.SeparationDate!.Value.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}; " +
                "record the separation with that last working day.");

        if (request.Type == SeparationType.AuthorizedCause && request.AuthorizedCause is null)
            throw new DomainException("Choose the authorized cause.");
        if (request.Type != SeparationType.AuthorizedCause && request.AuthorizedCause is not null)
            throw new DomainException("Only an authorized-cause separation has a cause.");
        if (request.LastWorkingDay < request.NoticeDate)
            throw new DomainException("The last working day can't be before the notice date.");
        if (request.Reason is { Length: > MaxReasonLength })
            throw new DomainException($"Keep the reason to {MaxReasonLength} characters.");

        var separation = new Separation
        {
            EmployeeId = request.EmployeeId,
            Employee = employee,
            Type = request.Type,
            AuthorizedCause = request.AuthorizedCause,
            NoticeDate = request.NoticeDate,
            LastWorkingDay = request.LastWorkingDay,
            Reason = request.Reason,
            Status = alreadyLeft ? SeparationStatus.Separated : SeparationStatus.NoticeGiven,
            RecordedBy = _currentUser.Email ?? "",
            SeparatedBy = alreadyLeft ? _currentUser.Email : null,
            SeparatedAt = alreadyLeft ? PhilippineTime.Now(_clock) : null,
            ClearanceItems = DefaultClearanceItems
                .Select((name, i) => new SeparationClearanceItem { Name = name, SortOrder = i })
                .ToList()
        };

        // AddAsync converts the unique-index violation from two HR users racing to record the same
        // employee's separation into the same DomainException the check above throws - see
        // SeparationRepository.AddAsync.
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
        if (separation.FinalPayRunId is not null)
            throw new DomainException("Final pay has already been started for this separation.");

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
        var duplicate = separation.ClearanceItems.FirstOrDefault(i => string.Equals(i.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null)
            throw new DomainException($"There's already a {duplicate.Name} item.");

        var item = new SeparationClearanceItem
        {
            SeparationId = separation.Id,
            Name = trimmed,
            SortOrder = separation.ClearanceItems.Count
        };

        // The dedicated insert, not separation.ClearanceItems.Add(item) + SaveAsync: see
        // ISeparationRepository.AddClearanceItemAsync's doc for the concurrency trap that avoids.
        await _separations.AddClearanceItemAsync(item, ct);

        // Against a real DbContext, EF's relationship fix-up already added `item` to
        // separation.ClearanceItems itself the moment it was tracked (its SeparationId matches this
        // tracked separation). A mocked repository does no such thing, so the Application-layer
        // tests still need this - guard instead of dropping it, so the item lands in the DTO
        // exactly once either way.
        if (!separation.ClearanceItems.Contains(item))
            separation.ClearanceItems.Add(item);
        return ToDto(separation, Today());
    }

    public async Task<SeparationDto> ClearItemAsync(Guid id, Guid itemId, string? note, CancellationToken ct = default)
    {
        var separation = await RequireAsync(id, ct);
        EnsureClearanceEditable(separation);
        var item = RequireItem(separation, itemId);
        if (item.ClearedAt is not null)
            throw new DomainException($"{item.Name} is already cleared.");
        if (note is { Length: > MaxClearanceNoteLength })
            throw new DomainException($"Keep the note to {MaxClearanceNoteLength} characters.");

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

        // The undo is kept on the item, with the note it throws away, so a clearance that was given
        // and then taken back still leaves a trace.
        item.LastUndoneBy = _currentUser.Email;
        item.LastUndoneAt = PhilippineTime.Now(_clock);
        item.LastUndoneNote = item.Note;
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

    public async Task<SeparationDto> SeparateNowAsync(
        Guid employeeId, DateOnly lastWorkingDay, SeparationType? type = null, AuthorizedCause? authorizedCause = null, CancellationToken ct = default)
    {
        var existing = await _separations.GetOpenForEmployeeAsync(employeeId, ct);

        Separation separation;
        if (existing is { Status: SeparationStatus.NoticeGiven })
        {
            separation = existing;

            if (type is { } explicitType)
            {
                if (explicitType == SeparationType.AuthorizedCause && authorizedCause is null)
                    throw new DomainException("Choose the authorized cause.");
                if (explicitType != SeparationType.AuthorizedCause && authorizedCause is not null)
                    throw new DomainException("Only an authorized-cause separation has a cause.");

                separation.Type = explicitType;
                separation.AuthorizedCause = authorizedCause;
            }
            else if (authorizedCause is not null)
            {
                // A cause given without a type: it only makes sense if the separation is already
                // AuthorizedCause (its type isn't changing), the same rule RecordAsync applies to a
                // freshly given type. Silently dropping the cause here would hide a caller's mistake.
                if (separation.Type != SeparationType.AuthorizedCause)
                    throw new DomainException("Only an authorized-cause separation has a cause.");

                separation.AuthorizedCause = authorizedCause;
            }
            // Neither given: keep whatever this separation was already recorded with - Deactivate
            // is just asserting the date here, not re-classifying why the employee left.

            // HR is asserting the actual last working day directly (it can be earlier than the
            // notice originally given, e.g. an immediate termination that supersedes a resignation
            // notice). Pulling the notice date back to match, rather than refusing, keeps
            // "notice date <= last working day" true without treating HR's own assertion as invalid.
            if (lastWorkingDay < separation.NoticeDate)
                separation.NoticeDate = lastWorkingDay;

            separation.LastWorkingDay = lastWorkingDay;
        }
        else
        {
            var recordType = type ?? SeparationType.Resignation;
            var recorded = await RecordAsync(
                new RecordSeparationRequest(employeeId, recordType, authorizedCause, lastWorkingDay, lastWorkingDay, null), ct);
            separation = await _separations.GetAsync(recorded.Id, ct)
                ?? throw new InvalidOperationException("The separation was not saved.");
        }

        return await CompleteAsync(separation, ignoreDateCheck: true, ct);
    }

    // ── Rules ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every clearance change is refused once the separation's final pay is Paid: paying it required
    /// clearance to be complete, and it has to stay the clearance the payment was released on.
    /// </summary>
    private static void EnsureClearanceEditable(Separation separation)
    {
        if (separation.FinalPayRun?.Status == PayrollRunStatus.Paid)
            throw new DomainException("Final pay has been paid; clearance can't change now.");
    }

    private async Task<SeparationDto> CompleteAsync(Separation separation, bool ignoreDateCheck, CancellationToken ct)
    {
        var today = Today();
        if (!ignoreDateCheck && today < separation.LastWorkingDay)
            throw new DomainException(
                $"{separation.Employee.FullName}'s last working day is " +
                $"{separation.LastWorkingDay.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}; mark them separated on or after it.");

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
        s.Status == SeparationStatus.Separated && s.FinalPayDueBy < today && s.FinalPayRun?.Status != PayrollRunStatus.Paid,
        s.ClearanceItems.Count(i => i.ClearedAt is not null),
        s.ClearanceItems.Count,
        s.ClearanceItems.OrderBy(i => i.SortOrder)
            .Select(i => new ClearanceItemDto(i.Id, i.Name, i.ClearedBy, i.ClearedAt, i.Note, i.LastUndoneBy, i.LastUndoneAt))
            .ToList(),
        s.FinalPayRunId,
        s.FinalPayRun?.RunNumber,
        s.FinalPayRun?.Status);
}
