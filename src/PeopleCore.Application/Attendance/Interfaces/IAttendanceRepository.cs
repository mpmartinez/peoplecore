using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities.Attendance;

namespace PeopleCore.Application.Attendance.Interfaces;

public interface IAttendanceRepository : IRepository<AttendanceRecord>
{
    Task<AttendanceRecord?> GetByEmployeeAndDateAsync(Guid employeeId, DateOnly date, CancellationToken ct = default);
    /// <summary>
    /// A page of rows, newest first. <paramref name="reportingManagerId"/>, when given, keeps only rows
    /// of employees who report directly to that employee - a Manager's team.
    /// </summary>
    Task<(IReadOnlyList<AttendanceRecord> Items, int TotalCount)> GetPagedAsync(Guid? employeeId, Guid? reportingManagerId, DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<AttendanceRecord>> GetByEmployeeAndPeriodAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<IReadOnlyList<AttendanceRecord>> GetAllByPeriodAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}
