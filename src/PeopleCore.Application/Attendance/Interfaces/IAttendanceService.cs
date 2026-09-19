using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Common.DTOs;

namespace PeopleCore.Application.Attendance.Interfaces;

public interface IAttendanceService
{
    Task<AttendanceRecordDto> TimeInAsync(TimeInRequest request, CancellationToken ct = default);
    Task<AttendanceRecordDto> TimeOutAsync(TimeOutRequest request, CancellationToken ct = default);
    Task<PagedResult<AttendanceRecordDto>> GetAllAsync(Guid? employeeId, Guid? reportingManagerId, DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken ct = default);
    Task<AttendanceSummaryDto> GetSummaryAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<AttendanceImportResultDto> SyncPunchesAsync(IReadOnlyList<AttendancePunchDto> punches, CancellationToken ct = default);

    /// <summary>Sets a day's times outright, recomputing lateness, undertime and overtime. Both null clears the day.</summary>
    Task<AttendanceRecordDto> SetDayAsync(Guid employeeId, DateOnly date, TimeOnly? timeIn, TimeOnly? timeOut, CancellationToken ct = default);
}
