using PeopleCore.Domain.Enums;

namespace PeopleCore.Application.Payroll.DTOs;

public record CreatePayrollRunRequest(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly PayDate,
    PayFrequency Frequency,
    IReadOnlyList<PayrollRunEmployeeInput> Employees,
    Guid? AttendancePeriodId = null);

/// <summary>
/// Per-employee inputs for a payroll run. Phase 1 supplies these explicitly; Phase 2's
/// attendance bridge will derive them from punches, approved leave and approved overtime.
/// </summary>
public record PayrollRunEmployeeInput(
    Guid EmployeeId,
    decimal? DaysWorked = null,
    decimal OvertimeHours = 0m,
    decimal HolidayDays = 0m,
    bool IncludeThirteenthMonth = false);

/// <summary>
/// One employee's line in a run. Deliberately carries only what this run computed - not the
/// compensation it computed from (BasicSalary/PayFrequency/TaxCode/Dependents live only on
/// EmployeeCompensationDto; see that record's remarks).
/// </summary>
public record PayrollRunEmployeeDto(
    Guid Id,
    Guid EmployeeId,
    string EmployeeName,
    decimal GrossPay,
    decimal TotalDeductions,
    decimal NetPay,
    decimal RegularPay,
    decimal OvertimePay,
    decimal HolidayPay,
    decimal NightDiffPay,
    decimal TaxableAllowances,
    decimal NonTaxableAllowances,
    decimal ThirteenthMonth,
    decimal SSSEmployee,
    decimal SSSEmployer,
    decimal PhilHealthEmployee,
    decimal PhilHealthEmployer,
    decimal PagIbigEmployee,
    decimal PagIbigEmployer,
    decimal WithholdingTax,
    decimal LoanDeductions,
    decimal OtherDeductions);

public record PayrollRunDto(
    Guid Id,
    string RunNumber,
    string PeriodLabel,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly PayDate,
    PayFrequency Frequency,
    PayrollRunStatus Status,
    int EmployeeCount,
    decimal TotalGrossPay,
    decimal TotalDeductions,
    decimal TotalNetPay,
    DateTime CreatedAt,
    Guid? AttendancePeriodId,
    int EmployeesMissingAttendance,
    IReadOnlyList<PayrollRunEmployeeDto> Employees);
