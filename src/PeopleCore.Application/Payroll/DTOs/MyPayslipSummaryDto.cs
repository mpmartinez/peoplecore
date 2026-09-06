namespace PeopleCore.Application.Payroll.DTOs;

/// <summary>
/// One row of the caller's own payslip history - just enough for the ESS page to list the runs
/// the employee appears in and let them open one. Deliberately carries only this employee's
/// NetPay, never the run's totals or any other employee's figures: this DTO is what
/// PayslipService.GetMyPayslipsAsync returns, and it must never become a channel for another
/// employee's compensation to leak out.
/// </summary>
public record MyPayslipSummaryDto(
    Guid RunId,
    string RunNumber,
    string PeriodLabel,
    DateOnly PayDate,
    decimal NetPay);
