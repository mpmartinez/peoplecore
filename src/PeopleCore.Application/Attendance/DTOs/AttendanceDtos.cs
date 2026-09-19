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

/// <summary>An employee as the attendance import sees them: who a time clock's number can point at.</summary>
public record AttendanceImportEmployeeDto(Guid Id, string EmployeeNumber, string FullName, string? BiometricId, bool IsActive);

/// <summary>A number in the file that matches no employee, and how many of the day's punches carry it.</summary>
public record UnmatchedDeviceIdDto(string DeviceId, int Punches);

/// <summary>
/// What an import would do, before anything is written: the file's shape, who and which days it
/// covers, the numbers that match nobody yet, and every employee they could be linked to.
/// </summary>
public record AttendanceImportPreviewDto(
    string Layout,
    int Punches,
    int MatchedPeople,
    DateOnly? From,
    DateOnly? To,
    IReadOnlyList<UnmatchedDeviceIdDto> Unmatched,
    IReadOnlyList<string> Errors,
    IReadOnlyList<AttendanceImportEmployeeDto> Employees);

public record SetBiometricIdDto(string? BiometricId);

/// <summary>A change to one day's times: an HR edit, an import that replaced the day, or an employee's request.</summary>
public record AttendanceCorrectionDto(
    Guid Id,
    Guid EmployeeId,
    string EmployeeName,
    string EmployeeNumber,
    DateOnly AttendanceDate,
    DateTime? PreviousTimeIn,
    DateTime? PreviousTimeOut,
    DateTime? NewTimeIn,
    DateTime? NewTimeOut,
    string Reason,
    string Source,
    string Status,
    string RequestedBy,
    DateTime RequestedAt,
    string? ReviewedBy,
    DateTime? ReviewedAt,
    string? RejectionReason);

/// <summary>HR setting a day's times. Both null clears the day to absent.</summary>
public record CorrectAttendanceDto(Guid EmployeeId, DateOnly Date, TimeOnly? TimeIn, TimeOnly? TimeOut, string Reason);

/// <summary>An employee asking for their own day to be corrected.</summary>
public record RequestAttendanceCorrectionDto(DateOnly Date, TimeOnly? TimeIn, TimeOnly? TimeOut, string Reason);

public record RejectAttendanceCorrectionDto(string Reason);
