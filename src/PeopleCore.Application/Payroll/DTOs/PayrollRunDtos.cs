using PeopleCore.Domain.Enums;

namespace PeopleCore.Application.Payroll.DTOs;

/// <param name="IncludeLeaveConversion">
/// Pays out each eligible employee's unused year-end-convertible leave in cash. Allowed only on a
/// Regular run whose PeriodEnd falls in December.
/// </param>
public record CreatePayrollRunRequest(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly PayDate,
    PayFrequency Frequency,
    IReadOnlyList<PayrollRunEmployeeInput> Employees,
    Guid? AttendancePeriodId = null,
    bool IncludeLeaveConversion = false);

/// <summary>Turns a run's year-end leave conversion on or off (PUT api/payroll-runs/{id}/leave-conversion).</summary>
public record SetLeaveConversionRequest(bool Include);

/// <summary>Includes or leaves out a run's 13th month for every employee (PUT api/payroll-runs/{id}/thirteenth-month).</summary>
public record SetThirteenthMonthRequest(bool Include);

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
/// <para>
/// <see cref="AdvanceMaternityBenefit"/> advances the employee's SSS maternity benefit on this run:
/// the benefit of their Draft claim with an allowance, once per claim. It is stored on the entry, so
/// a recompute keeps it.
/// </para>
/// </summary>
public record PayrollRunEmployeeInput(
    Guid EmployeeId,
    decimal? DaysWorked = null,
    decimal? OvertimeHours = null,
    decimal? HolidayDays = null,
    bool IncludeThirteenthMonth = false,
    bool AdvanceMaternityBenefit = false);

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
    // figure from RegularPay + AbsenceDeduction + TardinessDeduction + MaternityBenefitOffset and
    // must not have these added again anywhere they touch TotalDeductions.
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
    decimal OtherDeductions,
    // Final-pay earnings. A regular run has only the leave conversion, and only when it converts
    // unused year-end leave (IncludesLeaveConversion); separation and retirement pay are final pay's
    // alone. FinalPayNonTaxable is the part of the three non-taxable outright
    // (LeaveConversionNonTaxable plus exempt separation/retirement pay); the leave beyond de minimis
    // is other benefits, exempt with the 13th month up to the year's 90,000.
    decimal LeaveConversionPay = 0m,
    decimal LeaveConversionNonTaxable = 0m,
    decimal SeparationPay = 0m,
    decimal RetirementPay = 0m,
    decimal FinalPayNonTaxable = 0m,
    // Maternity pay (RA 11210). The advance is in GrossPay (tax-free); the offset is already
    // netted out of RegularPay, and PayslipLineBuilder restores it to the basic figure the way it
    // restores AbsenceDeduction and TardinessDeduction.
    decimal MaternityBenefitAdvance = 0m,
    decimal MaternityBenefitOffset = 0m,
    // The salary differential: the pay for the leave days the offset leaves. Still in RegularPay,
    // but non-taxable (RMC 105-2019); PayslipLineBuilder shows it as its own earning.
    decimal MaternityDifferential = 0m,
    // Shares a maternity-covered period couldn't pay, deferred (they are in the three employee
    // shares above and added back to NetPay), and earlier deferred shares this entry collects
    // (taken from NetPay, not part of TotalDeductions). See PayslipLineBuilder.DeductionsTotal.
    decimal ContributionsDeferred = 0m,
    decimal DeferredContributionsCollected = 0m);

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
    IReadOnlyList<PayrollRunEmployeeDto> Employees,
    PayrollRunType RunType = PayrollRunType.Regular,
    bool IncludesLeaveConversion = false,
    // True when any entry includes the 13th month (PayrollRunEmployeeInput.IncludeThirteenthMonth).
    bool IncludesThirteenthMonth = false,
    // Worked out afresh on every load: the maternity warnings, what HR still has to do before the run
    // pays maternity right (see IMaternityPayCalculator.WarningsAsync), then the opening-balance
    // double-count warnings, for employees whose opening balance already covers the run's pay date.
    // Null in the constructor reads as none.
    IReadOnlyList<string>? Warnings = null)
{
    public IReadOnlyList<string> Warnings { get; init; } = Warnings ?? [];
}

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
    DateTime CreatedAt,
    PayrollRunType RunType = PayrollRunType.Regular,
    bool IncludesLeaveConversion = false,
    bool IncludesThirteenthMonth = false);
