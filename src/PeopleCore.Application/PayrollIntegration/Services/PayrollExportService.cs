using PeopleCore.Application.Attendance.Interfaces;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Leave.Interfaces;
using PeopleCore.Application.PayrollIntegration.DTOs;
using PeopleCore.Application.PayrollIntegration.Interfaces;
using PeopleCore.Domain.Entities.Attendance;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Enums;

namespace PeopleCore.Application.PayrollIntegration.Services;

public class PayrollExportService : IPayrollExportService
{
    private readonly IEmployeeRepository _employeeRepo;
    private readonly IAttendanceRepository _attendanceRepo;
    private readonly ILeaveRequestRepository _leaveRepo;
    private readonly IOvertimeRepository _overtimeRepo;

    public PayrollExportService(
        IEmployeeRepository employeeRepo,
        IAttendanceRepository attendanceRepo,
        ILeaveRequestRepository leaveRepo,
        IOvertimeRepository overtimeRepo)
    {
        _employeeRepo = employeeRepo;
        _attendanceRepo = attendanceRepo;
        _leaveRepo = leaveRepo;
        _overtimeRepo = overtimeRepo;
    }

    public async Task<IReadOnlyList<PayrollEmployeeDto>> GetEmployeeMasterDataAsync(CancellationToken ct = default)
    {
        var employees = await _employeeRepo.GetAllAsync(ct);

        return employees
            .Where(e => e.IsActive)
            .Select(e => new PayrollEmployeeDto(
                EmployeeId: e.Id,
                EmployeeNumber: e.EmployeeNumber,
                FirstName: e.FirstName,
                MiddleName: e.MiddleName,
                LastName: e.LastName,
                WorkEmail: e.WorkEmail,
                DepartmentName: e.Department?.Name ?? string.Empty,
                PositionTitle: e.Position?.Title ?? string.Empty,
                EmploymentStatus: e.EmploymentStatus.ToString(),
                EmploymentType: e.EmploymentType,
                HireDate: e.HireDate,
                RegularizationDate: e.RegularizationDate,
                Is13thMonthEligible: e.Is13thMonthEligible,
                IsActive: e.IsActive,
                SssNumber: GetGovId(e, GovernmentIdType.SSS),
                PhilHealthNumber: GetGovId(e, GovernmentIdType.PhilHealth),
                PagIbigNumber: GetGovId(e, GovernmentIdType.PagIbig),
                TinNumber: GetGovId(e, GovernmentIdType.TIN)))
            .ToList();
    }

    public async Task<IReadOnlyList<PayrollAttendanceSummaryDto>> GetAttendanceSummaryAsync(
        DateOnly from, DateOnly to, Guid? employeeId = null, CancellationToken ct = default)
    {
        if (employeeId.HasValue)
        {
            var employee = await _employeeRepo.GetByIdAsync(employeeId.Value, ct);
            if (employee is null || !employee.IsActive) return [];

            var records = await _attendanceRepo.GetByEmployeeAndPeriodAsync(employeeId.Value, from, to, ct);
            return [BuildSummary(employee, records, from, to)];
        }

        // Single query for all records in period — eliminates N+1
        var allRecords = await _attendanceRepo.GetAllByPeriodAsync(from, to, ct);
        var recordsByEmployee = allRecords
            .GroupBy(r => r.EmployeeId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<AttendanceRecord>)g.ToList());

        var allEmployees = await _employeeRepo.GetAllAsync(ct);
        var summaries = new List<PayrollAttendanceSummaryDto>();
        foreach (var emp in allEmployees.Where(e => e.IsActive))
        {
            var records = recordsByEmployee.GetValueOrDefault(emp.Id) ?? [];
            summaries.Add(BuildSummary(emp, records, from, to));
        }

        return summaries;
    }

    public async Task<IReadOnlyList<PayrollLeaveDeductionDto>> GetApprovedLeavesAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var leaves = await _leaveRepo.GetApprovedByPeriodAsync(from, to, ct);

        return leaves.Select(l => new PayrollLeaveDeductionDto(
            LeaveRequestId: l.Id,
            EmployeeId: l.EmployeeId,
            EmployeeNumber: l.Employee?.EmployeeNumber ?? string.Empty,
            FullName: l.Employee?.FullName ?? string.Empty,
            LeaveTypeCode: l.LeaveType?.Code ?? string.Empty,
            LeaveTypeName: l.LeaveType?.Name ?? string.Empty,
            IsPaid: l.LeaveType?.IsPaid ?? false,
            StartDate: l.StartDate,
            EndDate: l.EndDate,
            TotalDays: l.TotalDays,
            ApprovedAt: l.ApprovedAt!.Value))
        .ToList();
    }

    public async Task<IReadOnlyList<PayrollOvertimeDto>> GetApprovedOvertimeAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var overtimes = await _overtimeRepo.GetApprovedByPeriodAsync(from, to, ct);

        return overtimes.Select(o => new PayrollOvertimeDto(
            OvertimeRequestId: o.Id,
            EmployeeId: o.EmployeeId,
            EmployeeNumber: o.Employee?.EmployeeNumber ?? string.Empty,
            FullName: o.Employee?.FullName ?? string.Empty,
            OvertimeDate: o.OvertimeDate,
            TotalMinutes: o.TotalMinutes,
            ApprovedAt: o.ApprovedAt!.Value))
        .ToList();
    }

    public async Task<IReadOnlyList<PayrollStatusChangeDto>> GetStatusChangesAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var employees = await _employeeRepo.GetAllAsync(ct);

        // Employees whose regularization date falls within the period represent a status change
        // from Probationary to Regular
        return employees
            .Where(e => e.RegularizationDate.HasValue
                && e.RegularizationDate.Value >= from
                && e.RegularizationDate.Value <= to)
            .Select(e => new PayrollStatusChangeDto(
                EmployeeId: e.Id,
                EmployeeNumber: e.EmployeeNumber,
                FullName: e.FullName,
                OldStatus: EmploymentStatus.Probationary.ToString(),
                NewStatus: EmploymentStatus.Regular.ToString(),
                EffectiveDate: e.RegularizationDate!.Value))
            .ToList();
    }

    private static PayrollAttendanceSummaryDto BuildSummary(
        Employee employee,
        IReadOnlyList<AttendanceRecord> records,
        DateOnly from,
        DateOnly to)
    {
        int daysPresent = records.Count(r => r.IsPresent);
        int totalLateMinutes = records.Sum(r => r.LateMinutes);
        int totalUndertimeMinutes = records.Sum(r => r.UndertimeMinutes);
        int totalOvertimeMinutes = records.Sum(r => r.OvertimeMinutes);
        int regularHolidaysWorked = records.Count(r => r.IsPresent && r.IsHoliday && r.HolidayType == HolidayType.RegularHoliday);
        int specialHolidaysWorked = records.Count(r => r.IsPresent && r.IsHoliday && r.HolidayType == HolidayType.SpecialNonWorking);

        return new PayrollAttendanceSummaryDto(
            EmployeeId: employee.Id,
            EmployeeNumber: employee.EmployeeNumber,
            FullName: employee.FullName,
            PeriodFrom: from,
            PeriodTo: to,
            DaysPresent: daysPresent,
            TotalLateMinutes: totalLateMinutes,
            TotalUndertimeMinutes: totalUndertimeMinutes,
            TotalApprovedOvertimeMinutes: totalOvertimeMinutes,
            RegularHolidaysWorked: regularHolidaysWorked,
            SpecialHolidaysWorked: specialHolidaysWorked);
    }

    private static string? GetGovId(Employee employee, GovernmentIdType idType)
        => employee.GovernmentIds.FirstOrDefault(g => g.IdType == idType)?.IdNumber;
}
