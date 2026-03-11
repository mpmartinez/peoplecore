using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities.Leave;

namespace PeopleCore.Application.Leave.Interfaces;

public interface ILeaveRequestRepository : IRepository<LeaveRequest>
{
    Task<(IReadOnlyList<LeaveRequest> Items, int TotalCount)> GetPagedAsync(
        Guid? employeeId, string? status, int page, int pageSize, CancellationToken ct = default);
    Task<bool> HasOverlapAsync(Guid employeeId, DateOnly startDate, DateOnly endDate, Guid? excludeId = null, CancellationToken ct = default);
    Task<IReadOnlyList<LeaveRequest>> GetApprovedByPeriodAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}
