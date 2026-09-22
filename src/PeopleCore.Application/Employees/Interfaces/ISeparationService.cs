using PeopleCore.Application.Employees.DTOs;
using PeopleCore.Domain.Enums;

namespace PeopleCore.Application.Employees.Interfaces;

/// <summary>Recording, completing and cancelling separations, and the clearance checklist that gates final pay.</summary>
public interface ISeparationService
{
    Task<SeparationDto> RecordAsync(RecordSeparationRequest request, CancellationToken ct = default);
    Task<SeparationDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SeparationDto>> ListAsync(CancellationToken ct = default);
    Task<SeparationDto> MarkSeparatedAsync(Guid id, CancellationToken ct = default);
    Task CancelAsync(Guid id, CancellationToken ct = default);
    Task<SeparationDto> AddClearanceItemAsync(Guid id, string name, CancellationToken ct = default);
    Task<SeparationDto> ClearItemAsync(Guid id, Guid itemId, string? note, CancellationToken ct = default);
    Task<SeparationDto> UndoClearItemAsync(Guid id, Guid itemId, CancellationToken ct = default);
    Task<SeparationDto> DeleteClearanceItemAsync(Guid id, Guid itemId, CancellationToken ct = default);

    /// <summary>
    /// Records and marks separated in one step - what Deactivate calls. When the employee already
    /// has a <see cref="Domain.Enums.SeparationStatus.NoticeGiven"/> separation, that record is
    /// reused: a null <paramref name="type"/> keeps its existing type and cause, a given one is
    /// validated and applied the same way <see cref="RecordAsync"/> validates a new one. Otherwise a
    /// new record is created with <paramref name="type"/> defaulting to
    /// <see cref="SeparationType.Resignation"/>.
    /// </summary>
    Task<SeparationDto> SeparateNowAsync(
        Guid employeeId, DateOnly lastWorkingDay, SeparationType? type = null, AuthorizedCause? authorizedCause = null, CancellationToken ct = default);
}
