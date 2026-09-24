using PeopleCore.Domain.Entities.Leave;

namespace PeopleCore.Application.Leave.Interfaces;

public interface ILeaveDayCounter
{
    /// <summary>Leave days from start to end inclusive, keyed by calendar year (only years with days, or the start year with 0).</summary>
    Task<IReadOnlyDictionary<int, decimal>> CountByYearAsync(
        Guid employeeId, LeaveType type, DateOnly start, DateOnly end, CancellationToken ct = default);
}
