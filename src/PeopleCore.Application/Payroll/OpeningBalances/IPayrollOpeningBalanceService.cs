namespace PeopleCore.Application.Payroll.OpeningBalances;

/// <summary>
/// Payroll opening balances: what each employee was paid in a year before PeopleCore, which every
/// "earlier this year" figure adds to the year's Paid runs. HR creates, edits and deletes them at
/// any time; Paid runs are never recomputed, so a save warns of the ones that used the figures.
/// </summary>
public interface IPayrollOpeningBalanceService
{
    /// <summary>Every opening balance for the year, by employee name, each with its double-count warnings.</summary>
    Task<IReadOnlyList<OpeningBalanceDto>> ListAsync(int year, CancellationToken ct = default);

    /// <summary>One opening balance, with its double-count warnings.</summary>
    Task<OpeningBalanceDto> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Records an employee's opening balance for a year, once per employee and year.</summary>
    Task<OpeningBalanceDto> CreateAsync(OpeningBalanceRequest request, CancellationToken ct = default);

    /// <summary>Changes an opening balance's through date and figures; its employee and year stay.</summary>
    Task<OpeningBalanceDto> UpdateAsync(Guid id, OpeningBalanceRequest request, CancellationToken ct = default);

    /// <summary>Removes an opening balance.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Imports a CSV in the template's layout (<see cref="OpeningBalanceCsv"/>). Every row is
    /// checked first, with the same validation as the form; any problem refuses the whole file and
    /// comes back in <see cref="OpeningBalanceImportResult.Errors"/>. Otherwise each row's
    /// (employee, year) balance is created, or updated in place, in one save.
    /// </summary>
    Task<OpeningBalanceImportResult> ImportAsync(Stream csv, CancellationToken ct = default);
}
