namespace PeopleCore.Application.PayrollIntegration.DTOs;

public record PayrollEmployeeDto(
    Guid EmployeeId, string EmployeeNumber, string FirstName, string? MiddleName, string LastName,
    string WorkEmail, string DepartmentName, string PositionTitle,
    string EmploymentStatus, string EmploymentType, DateOnly HireDate, DateOnly? RegularizationDate,
    bool Is13thMonthEligible, bool IsActive,
    string? SssNumber, string? PhilHealthNumber, string? PagIbigNumber, string? TinNumber);

public record PayrollAttendanceSummaryDto(
    Guid EmployeeId, string EmployeeNumber, string FullName,
    DateOnly PeriodFrom, DateOnly PeriodTo,
    int DaysPresent, int TotalLateMinutes, int TotalUndertimeMinutes,
    int TotalApprovedOvertimeMinutes, int RegularHolidaysWorked, int SpecialHolidaysWorked);

public record PayrollLeaveDeductionDto(
    Guid LeaveRequestId, Guid EmployeeId, string EmployeeNumber, string FullName,
    string LeaveTypeCode, string LeaveTypeName, bool IsPaid,
    DateOnly StartDate, DateOnly EndDate, decimal TotalDays, DateTime ApprovedAt);

public record PayrollOvertimeDto(
    Guid OvertimeRequestId, Guid EmployeeId, string EmployeeNumber, string FullName,
    DateOnly OvertimeDate, int TotalMinutes, DateTime ApprovedAt);

public record PayrollStatusChangeDto(
    Guid EmployeeId, string EmployeeNumber, string FullName,
    string OldStatus, string NewStatus, DateOnly EffectiveDate);
