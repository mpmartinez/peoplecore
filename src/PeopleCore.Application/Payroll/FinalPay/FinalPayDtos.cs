using PeopleCore.Domain.Enums;

namespace PeopleCore.Application.Payroll.FinalPay;

/// <summary>
/// What HR decides for a final pay. Everything else - the period's end, the working days, leave,
/// separation or retirement pay, loans and the settled tax - is worked out from the separation.
/// </summary>
/// <param name="PeriodStart">Null for the default: the day after the last Paid regular run's period, or the first of the last working day's month.</param>
/// <param name="SeparationPayOverride">Replaces the computed separation pay; needs <paramref name="OverrideNote"/>.</param>
/// <param name="RetirementPayOverride">Replaces the computed retirement pay; needs <paramref name="OverrideNote"/>.</param>
/// <param name="Deductions">HR-added deductions (unreturned property, advances outside the loans module), taken after loans.</param>
public record FinalPayRequest(
    DateOnly PayDate,
    DateOnly? PeriodStart,
    decimal? SeparationPayOverride,
    decimal? RetirementPayOverride,
    string? OverrideNote,
    IReadOnlyList<FinalPayDeductionDto> Deductions);

public record FinalPayDeductionDto(string Label, decimal Amount);

/// <summary>One convertible leave type paid out, and whether its days count toward the 10-day de minimis ceiling.</summary>
public record FinalPayLeaveLineDto(string LeaveType, decimal Days, bool CountsAsVacation);

/// <summary>
/// A loan against the final pay: its balance, what the final pay deducted, and what it could not
/// cover (which stays on the loan).
/// </summary>
public record FinalPayLoanLineDto(string LoanType, decimal Balance, decimal Deducted, decimal Uncovered);

/// <summary>A separation's final pay, as the final-pay page shows it.</summary>
/// <param name="ComputedSeparationOrRetirementPay">
/// The statutory figure whether or not HR overrode it: separation pay for an authorized cause,
/// retirement pay for a retirement (0 when not eligible), null for any other separation type.
/// </param>
public record FinalPaySummaryDto(
    Guid RunId,
    string RunNumber,
    PayrollRunStatus Status,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly PayDate,
    decimal WorkingDays,
    decimal LeaveConversionPay,
    decimal LeaveConversionNonTaxable,
    IReadOnlyList<FinalPayLeaveLineDto> LeaveLines,
    decimal SeparationPay,
    decimal RetirementPay,
    decimal? ComputedSeparationOrRetirementPay,
    string? OverrideNote,
    int ServiceYears,
    IReadOnlyList<FinalPayDeductionDto> Deductions,
    IReadOnlyList<FinalPayLoanLineDto> Loans,
    decimal WithholdingTax,
    decimal GrossPay,
    decimal NetPay,
    bool ClearanceComplete,
    IReadOnlyList<string> OutstandingClearance);
