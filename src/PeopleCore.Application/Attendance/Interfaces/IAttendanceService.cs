using PeopleCore.Application.Attendance.DTOs;
using PeopleCore.Application.Common.DTOs;

namespace PeopleCore.Application.Attendance.Interfaces;

public interface IAttendanceService
{
    Task<AttendanceRecordDto> TimeInAsync(TimeInRequest request, CancellationToken ct = default);
    Task<AttendanceRecordDto> TimeOutAsync(TimeOutRequest request, CancellationToken ct = default);
    Task<PagedResult<AttendanceRecordDto>> GetAllAsync(Guid? employeeId, DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken ct = default);
    Task<AttendanceSummaryDto> GetSummaryAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<AttendanceImportResultDto> SyncPunchesAsync(IReadOnlyList<AttendancePunchDto> punches, CancellationToken ct = default);
}
