using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities.Leave;

namespace PeopleCore.Application.Leave.Interfaces;

public interface ILeaveRequestRepository : IRepository<LeaveRequest>
{
    /// <summary>
    /// A page of rows, newest first. <paramref name="reportingManagerId"/>, when given, keeps only rows
    /// of employees who report directly to that employee - a Manager's team.
    /// </summary>
    Task<(IReadOnlyList<LeaveRequest> Items, int TotalCount)> GetPagedAsync(
        Guid? employeeId, Guid? reportingManagerId, string? status, int page, int pageSize, CancellationToken ct = default);
    Task<bool> HasOverlapAsync(Guid employeeId, DateOnly startDate, DateOnly endDate, Guid? excludeId = null, CancellationToken ct = default);
    Task<IReadOnlyList<LeaveRequest>> GetApprovedByPeriodAsync(DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>
    /// The employee's Pending requests of one type - the days they hold against the balance.
    /// <paramref name="excludeId"/>, when given, leaves out that request (the one being approved).
    /// </summary>
    Task<IReadOnlyList<LeaveRequest>> GetPendingAsync(Guid employeeId, Guid leaveTypeId, Guid? excludeId, CancellationToken ct = default);

    /// <summary>How many of the employee's requests of one type are Approved - the events used.</summary>
    Task<int> CountApprovedAsync(Guid employeeId, Guid leaveTypeId, CancellationToken ct = default);
}
