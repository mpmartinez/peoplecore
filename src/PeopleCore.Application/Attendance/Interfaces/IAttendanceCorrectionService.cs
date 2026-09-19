using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Common.DTOs;
using PeopleCore.Domain.Enums;

namespace PeopleCore.Application.Attendance.Interfaces;

/// <summary>
/// Changing a recorded time-in or time-out. Every change is kept as an <c>AttendanceCorrection</c>,
/// and no change is allowed on a day already paid in a payroll run.
/// </summary>
public interface IAttendanceCorrectionService
{
    /// <summary>Applies a change at once (an HR edit or a replacing import) and records it.</summary>
    Task<AttendanceCorrectionDto> CorrectAsync(CorrectAttendanceDto dto, string actor, AttendanceCorrectionSource source = AttendanceCorrectionSource.HrEdit, CancellationToken ct = default);

    /// <summary>Files an employee's request for their own day; nothing changes until it is approved.</summary>
    Task<AttendanceCorrectionDto> RequestAsync(Guid employeeId, RequestAttendanceCorrectionDto dto, string actor, CancellationToken ct = default);

    Task<AttendanceCorrectionDto> ApproveAsync(Guid id, Guid approverEmployeeId, string actor, CancellationToken ct = default);
    Task<AttendanceCorrectionDto> RejectAsync(Guid id, Guid rejecterEmployeeId, string actor, string reason, CancellationToken ct = default);

    Task<AttendanceCorrectionDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<AttendanceCorrectionDto>> GetPagedAsync(Guid? employeeId, Guid? reportingManagerId, AttendanceCorrectionStatus? status, int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<AttendanceCorrectionDto>> GetHistoryAsync(Guid employeeId, DateOnly date, CancellationToken ct = default);
}
