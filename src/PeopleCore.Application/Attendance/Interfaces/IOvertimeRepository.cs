using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities.Attendance;

namespace PeopleCore.Application.Attendance.Interfaces;

public interface IOvertimeRepository : IRepository<OvertimeRequest>
{
    Task<(IReadOnlyList<OvertimeRequest> Items, int TotalCount)> GetPagedAsync(
        Guid? employeeId, string? status, int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<OvertimeRequest>> GetApprovedByPeriodAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}
