using PeopleCore.Domain.Enums;

namespace PeopleCore.Application.Attendance.DTOs;

public record AttendanceRecordDto(
    Guid Id,
    Guid EmployeeId,
    string EmployeeName,
    DateOnly AttendanceDate,
    DateTime? TimeIn,
    DateTime? TimeOut,
    int LateMinutes,
    int UndertimeMinutes,
    int OvertimeMinutes,
    bool IsPresent,
    bool IsHoliday,
    HolidayType? HolidayType,
    string? Remarks);

/// <summary>
/// Clocking in carries no time: the server stamps the punch with its own clock (see
/// <c>PhilippineTime</c>), so nobody can clock in at a time of their choosing. Older clients still
/// send a <c>timeIn</c> property; System.Text.Json skips unknown properties, so it binds and is ignored.
/// </summary>
public record TimeInRequest(Guid EmployeeId);

/// <summary>As <see cref="TimeInRequest"/>: any <c>timeOut</c> an older client sends is ignored.</summary>
public record TimeOutRequest(Guid EmployeeId);

public record AttendanceSummaryDto(
    Guid EmployeeId,
    string EmployeeName,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    int TotalWorkingDays,
    int DaysPresent,
    int TotalLateMinutes,
    int TotalUndertimeMinutes,
    int TotalOvertimeMinutes,
    int RegularHolidaysWorked,
    int SpecialHolidaysWorked);

public record OvertimeRequestDto(
    Guid Id,
    Guid EmployeeId,
    string EmployeeName,
    DateOnly OvertimeDate,
    DateTime StartTime,
    DateTime EndTime,
    int TotalMinutes,
    string Reason,
    string Status,
    Guid? ApprovedBy,
    DateTime? ApprovedAt,
    string? RejectionReason);

public record CreateOvertimeRequestDto(
    Guid EmployeeId,
    DateOnly OvertimeDate,
    DateTime StartTime,
    DateTime EndTime,
    string Reason);

public record RejectOvertimeDto(string RejectionReason);

public record HolidayDto(Guid Id, string Name, DateOnly HolidayDate, HolidayType HolidayType, bool IsRecurring);
public record CreateHolidayDto(string Name, DateOnly HolidayDate, HolidayType HolidayType, bool IsRecurring);

// Biometric/CSV sync DTOs
public record AttendancePunchDto(
    string EmployeeNumber,
    DateTime PunchTime,
    string? DeviceId = null
);

public record AttendanceImportResultDto(
    int Imported,
    int Skipped,
    IReadOnlyList<string> Errors
);
