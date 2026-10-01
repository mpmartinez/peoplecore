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

/// <summary>What an opening-balance import saved: the balances it created and the ones it updated.</summary>
/// <param name="Warnings">
/// For each saved balance, in file order, its double-count warnings then its edit warnings (as on
/// <see cref="OpeningBalanceDto.Warnings"/> after a save): a double-count warning prefixed
/// "{EmployeeNumber}: " (it already opens with her name), an edit warning "{EmployeeNumber} {name}: ".
/// </param>
public record OpeningBalanceImportDto(int Created, int Updated, IReadOnlyList<string> Warnings);

/// <summary>
/// An opening-balance import's outcome. With any <paramref name="Errors"/> nothing was saved, the
/// counts are 0 and there are no warnings: each error is "Row {n}: {message}" (the header is row
/// 1), or one problem with the whole file; past 200, the list ends "…and {n} more problems.".
/// </summary>
public record OpeningBalanceImportResult(int Created, int Updated, IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);
