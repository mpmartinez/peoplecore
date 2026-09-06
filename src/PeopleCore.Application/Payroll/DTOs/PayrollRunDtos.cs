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
/// Per-employee inputs for a payroll run. Every quantity is a manual override: null means "use
/// what the attendance bridge derived from punches, approved leave, approved overtime, the
/// holiday calendar and the shift schedule".
/// <para>
/// They are nullable rather than defaulted to zero on purpose. A request that simply omits
/// <see cref="OvertimeHours"/> or <see cref="HolidayDays"/> would otherwise send a zero that
/// beats the derived figure, silently unpaying overtime and holiday premiums the employee
/// actually earned - the exact failure the bridge exists to prevent.
/// </para>
/// <para>
/// An override is stated in the caller's terms, not the engine's: <see cref="OvertimeHours"/>
/// is taken as ordinary overtime (priced at 1.25x) and <see cref="HolidayDays"/> as regular
/// holidays, and supplying either discards the derived rest-day overtime or special-holiday
/// days respectively. Overriding half of a split the caller cannot see would otherwise leave
/// the entry paying more hours or days than the caller asked for.
/// </para>
/// </summary>
public record PayrollRunEmployeeInput(
    Guid EmployeeId,
    decimal? DaysWorked = null,
    decimal? OvertimeHours = null,
    decimal? HolidayDays = null,
    bool IncludeThirteenthMonth = false);

/// <summary>
/// One employee's line in a run. Deliberately carries only what this run computed - not the
/// compensation it computed from (BasicSalary/PayFrequency/TaxCode/Dependents live only on
/// EmployeeCompensationDto; see that record's remarks). EmployeeNumber is an identifier, not
/// compensation, and DaysWorked is a figure this run itself computed (persisted on
/// PayrollRunEmployee), so both belong here alongside EmployeeName.
/// </summary>
public record PayrollRunEmployeeDto(
    Guid Id,
    Guid EmployeeId,
    string EmployeeName,
    string EmployeeNumber,
    decimal DaysWorked,
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
    // Already netted out of RegularPay above - a figure this run computed, not compensation
    // (see PayrollRunEmployee's remarks), which is why these two are here and BasicSalary,
    // PayFrequency, TaxCode and Dependents are not. PayslipLineBuilder reconstructs the basic
    // figure from RegularPay + AbsenceDeduction + TardinessDeduction and must not have these
    // added again anywhere they touch TotalDeductions.
    decimal AbsenceDeduction,
    decimal TardinessDeduction,
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

/// <summary>
/// A run as it appears in a list. Deliberately omits the Employees collection that
/// <see cref="PayrollRunDto"/> carries: a page of twelve runs of two hundred employees would
/// otherwise ship 2,400 nested records to render twelve table rows.
/// </summary>
public record PayrollRunSummaryDto(
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
    decimal TotalNetPay,
    int EmployeesMissingAttendance,
    DateTime CreatedAt);
