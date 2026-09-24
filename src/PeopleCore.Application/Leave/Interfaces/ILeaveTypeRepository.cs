using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities.Leave;

namespace PeopleCore.Application.Leave.Interfaces;

public interface ILeaveTypeRepository : IRepository<LeaveType>
{
    Task<LeaveType?> GetByCodeAsync(string code, CancellationToken ct = default);

    /// <summary>Whether the type has any leave request (of any status) or any balance row - once it has, it can't be deleted.</summary>
    Task<bool> IsUsedAsync(Guid id, CancellationToken ct = default);
}
