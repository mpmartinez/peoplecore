using PeopleCore.Application.PayrollIntegration.DTOs;

namespace PeopleCore.Application.PayrollIntegration.Interfaces;

public interface IPayrollExportService
{
    Task<IReadOnlyList<PayrollEmployeeDto>> GetEmployeeMasterDataAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PayrollAttendanceSummaryDto>> GetAttendanceSummaryAsync(DateOnly from, DateOnly to, Guid? employeeId = null, CancellationToken ct = default);
    /// <summary>
    /// Approved leave overlapping the period. Unless <paramref name="showConfidentialTypes"/>, a
    /// confidential type (VAWC) goes out as "Leave" with no code: payroll needs the dates and
    /// whether it is paid, not which leave it was.
    /// </summary>
    Task<IReadOnlyList<PayrollLeaveDeductionDto>> GetApprovedLeavesAsync(
        DateOnly from, DateOnly to, bool showConfidentialTypes, CancellationToken ct = default);
    Task<IReadOnlyList<PayrollOvertimeDto>> GetApprovedOvertimeAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<IReadOnlyList<PayrollStatusChangeDto>> GetStatusChangesAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}
