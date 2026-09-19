using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Domain.Entities.Attendance;
using PeopleCore.Domain.Enums;

namespace PeopleCore.Application.Attendance.Interfaces;

public interface IAttendanceCorrectionRepository : IRepository<AttendanceCorrection>
{
    /// <summary>Newest first, with each correction's employee loaded.</summary>
    Task<(IReadOnlyList<AttendanceCorrection> Items, int TotalCount)> GetPagedAsync(
        Guid? employeeId, Guid? reportingManagerId, AttendanceCorrectionStatus? status, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Every correction to one employee's day, oldest first.</summary>
    Task<IReadOnlyList<AttendanceCorrection>> GetForDayAsync(Guid employeeId, DateOnly date, CancellationToken ct = default);

    Task<bool> HasPendingForDayAsync(Guid employeeId, DateOnly date, CancellationToken ct = default);

    Task<AttendanceCorrection?> GetWithEmployeeAsync(Guid id, CancellationToken ct = default);
}
