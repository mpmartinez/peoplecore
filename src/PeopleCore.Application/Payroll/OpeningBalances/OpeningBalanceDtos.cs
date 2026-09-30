namespace PeopleCore.Application.Payroll.OpeningBalances;

/// <summary>
/// What an employee was paid in <paramref name="Year"/> before PeopleCore, through
/// <paramref name="ThroughDate"/>.
/// </summary>
/// <param name="Warnings">
/// The double-count warning, one per Paid run of hers in the year paid on or before the through
/// date. On the balance a create or update answers with, the edit warning follows: one per Paid run
/// of hers in the year paid after the through date, whose 13th month and tax won't change.
/// </param>
public record OpeningBalanceDto(Guid Id, Guid EmployeeId, string EmployeeName, string EmployeeNumber, int Year,
    DateOnly ThroughDate, decimal BasicSalary, decimal ThirteenthMonthPaid, decimal OtherBenefitsPaid,
    decimal OtherTaxablePay, decimal DeMinimis, decimal OtherNonTaxable, decimal EmployeeContributions,
    decimal TaxWithheld, decimal DeMinimisLeaveDays, IReadOnlyList<string> Warnings);

/// <summary>
/// An opening balance to create, or the new figures for one. On an update the
/// <paramref name="EmployeeId"/> and <paramref name="Year"/> must be the balance's own.
/// </summary>
public record OpeningBalanceRequest(Guid EmployeeId, int Year, DateOnly ThroughDate, decimal BasicSalary,
    decimal ThirteenthMonthPaid, decimal OtherBenefitsPaid, decimal OtherTaxablePay, decimal DeMinimis,
    decimal OtherNonTaxable, decimal EmployeeContributions, decimal TaxWithheld, decimal DeMinimisLeaveDays);
