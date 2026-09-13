using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities.Attendance;

namespace PeopleCore.Application.Attendance.Interfaces;

public interface IOvertimeRepository : IRepository<OvertimeRequest>
{
    /// <summary>
    /// A page of rows, newest first. <paramref name="reportingManagerId"/>, when given, keeps only rows
    /// of employees who report directly to that employee - a Manager's team.
    /// </summary>
    Task<(IReadOnlyList<OvertimeRequest> Items, int TotalCount)> GetPagedAsync(
        Guid? employeeId, Guid? reportingManagerId, string? status, int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<OvertimeRequest>> GetApprovedByPeriodAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}
