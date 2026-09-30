using System.Globalization;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.Application.Payroll.OpeningBalances;

/// <summary>
/// Payroll opening balances: what each employee was paid in a year before PeopleCore. Reads carry
/// the double-count warning (a Paid run of hers in the year on or before the through date, whose
/// pay the balance already covers); a save adds the edit warning (a Paid run of hers in the year
/// after the through date relied on the figures and won't be recomputed). Both are warnings only.
/// </summary>
public sealed class PayrollOpeningBalanceService : IPayrollOpeningBalanceService
{
    private const decimal MaxDeMinimisLeaveDays = 10m;

    private readonly IPayrollOpeningBalanceRepository _balances;
    private readonly IEmployeeRepository _employees;
    private readonly IPayrollRunRepository _runs;

    public PayrollOpeningBalanceService(IPayrollOpeningBalanceRepository balances, IEmployeeRepository employees,
        IPayrollRunRepository runs)
    {
        _balances = balances;
        _employees = employees;
        _runs = runs;
    }

    public async Task<IReadOnlyList<OpeningBalanceDto>> ListAsync(int year, CancellationToken ct = default)
    {
        EnsureYear(year);
        var balances = await _balances.GetForYearAsync(year, ct);
        if (balances.Count == 0) return [];

        var runs = await _runs.GetPaidRunsInYearAsync(year, ct);
        return balances.Select(b => ToDto(b, DoubleCountWarnings(b, RunsOf(b, runs)))).ToList();
    }

    public async Task<OpeningBalanceDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var balance = await FindAsync(id, ct);
        var runs = RunsOf(balance, await _runs.GetPaidRunsInYearAsync(balance.Year, ct));
        return ToDto(balance, DoubleCountWarnings(balance, runs));
    }

    public async Task<OpeningBalanceDto> CreateAsync(OpeningBalanceRequest request, CancellationToken ct = default)
    {
        Validate(request);
        var employee = await _employees.GetByIdAsync(request.EmployeeId, ct)
                       ?? throw new KeyNotFoundException($"Employee {request.EmployeeId} not found.");
        if (await _balances.GetAsync(employee.Id, request.Year, ct) is not null)
            throw new DomainException($"{employee.FullName} already has an opening balance for {request.Year}.");

        var balance = new PayrollOpeningBalance
        {
            EmployeeId = employee.Id,
            Employee = employee,
            Year = request.Year,
        };
        Apply(balance, request);
        // A rival create that commits between the check above and this save is refused by the
        // unique index, and AddNewAsync turns that into the same message.
        await _balances.AddNewAsync(balance, ct);
        return await SavedAsync(balance, ct);
    }

    public async Task<OpeningBalanceDto> UpdateAsync(Guid id, OpeningBalanceRequest request, CancellationToken ct = default)
    {
        var balance = await FindAsync(id, ct);
        if (request.EmployeeId != balance.EmployeeId || request.Year != balance.Year)
            throw new DomainException("An opening balance's employee and year can't change.");
        Validate(request);

        Apply(balance, request);
        await _balances.UpdateAsync(balance, ct);
        return await SavedAsync(balance, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var balance = await FindAsync(id, ct);
        await _balances.DeleteAsync(balance, ct);
    }

    /// <summary>The saved balance with its double-count warnings, then the edit warnings.</summary>
    private async Task<OpeningBalanceDto> SavedAsync(PayrollOpeningBalance balance, CancellationToken ct)
    {
        var runs = RunsOf(balance, await _runs.GetPaidRunsInYearAsync(balance.Year, ct));
        var warnings = DoubleCountWarnings(balance, runs);
        warnings.AddRange(runs
            .Where(r => r.PayDate > balance.ThroughDate)
            .Select(r => $"{r.RunNumber} used these figures; its 13th month and tax won't change. " +
                         "Reissue her 2316 to pick up the change."));
        return ToDto(balance, warnings);
    }

    /// <summary>The Paid runs in the balance's year that paid its employee, earliest pay date first.</summary>
    private static List<PayrollRun> RunsOf(PayrollOpeningBalance balance, IReadOnlyList<PayrollRun>? paidRuns)
        => (paidRuns ?? [])
            .Where(r => r.PayDate.Year == balance.Year && r.Employees.Any(e => e.EmployeeId == balance.EmployeeId))
            .OrderBy(r => r.PayDate)
            .ThenBy(r => r.RunNumber, StringComparer.Ordinal)
            .ToList();

    /// <summary>One warning per Paid run of hers paid on or before the through date: pay the balance already covers.</summary>
    private static List<string> DoubleCountWarnings(PayrollOpeningBalance balance, IEnumerable<PayrollRun> runs)
        => runs.Where(r => r.PayDate <= balance.ThroughDate)
            .Select(r => $"{balance.Employee.FullName}'s opening balance already covers pay through " +
                         $"{Date(balance.ThroughDate)}; {r.RunNumber} was paid on {Date(r.PayDate)}.")
            .ToList();

    private static string Date(DateOnly date) => date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

    private static void EnsureYear(int year)
    {
        // DateOnly's range; anything earlier than 1900 is a typo, not a payroll year.
        if (year is < 1900 or > 9999)
            throw new DomainException("Enter a year.");
    }

    private static void Validate(OpeningBalanceRequest request)
    {
        EnsureYear(request.Year);
        if (request.ThroughDate.Year != request.Year)
            throw new DomainException($"The through date must fall in {request.Year}.");
        decimal[] amounts =
        [
            request.BasicSalary, request.ThirteenthMonthPaid, request.OtherBenefitsPaid, request.OtherTaxablePay,
            request.DeMinimis, request.OtherNonTaxable, request.EmployeeContributions, request.TaxWithheld,
        ];
        if (amounts.Any(a => a < 0m))
            throw new DomainException("Amounts can't be negative.");
        if (request.DeMinimisLeaveDays is < 0m or > MaxDeMinimisLeaveDays)
            throw new DomainException("De minimis leave days must be between 0 and 10.");
    }

    /// <summary>The request's through date and figures, each rounded to the 2 dp it is stored to.</summary>
    private static void Apply(PayrollOpeningBalance balance, OpeningBalanceRequest request)
    {
        balance.ThroughDate = request.ThroughDate;
        balance.BasicSalary = Round(request.BasicSalary);
        balance.ThirteenthMonthPaid = Round(request.ThirteenthMonthPaid);
        balance.OtherBenefitsPaid = Round(request.OtherBenefitsPaid);
        balance.OtherTaxablePay = Round(request.OtherTaxablePay);
        balance.DeMinimis = Round(request.DeMinimis);
        balance.OtherNonTaxable = Round(request.OtherNonTaxable);
        balance.EmployeeContributions = Round(request.EmployeeContributions);
        balance.TaxWithheld = Round(request.TaxWithheld);
        balance.DeMinimisLeaveDays = Round(request.DeMinimisLeaveDays);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private async Task<PayrollOpeningBalance> FindAsync(Guid id, CancellationToken ct)
        => await _balances.GetByIdAsync(id, ct)
           ?? throw new KeyNotFoundException($"Opening balance {id} not found.");

    private static OpeningBalanceDto ToDto(PayrollOpeningBalance b, IReadOnlyList<string> warnings) => new(
        b.Id, b.EmployeeId, b.Employee.FullName, b.Employee.EmployeeNumber, b.Year, b.ThroughDate,
        b.BasicSalary, b.ThirteenthMonthPaid, b.OtherBenefitsPaid, b.OtherTaxablePay, b.DeMinimis, b.OtherNonTaxable,
        b.EmployeeContributions, b.TaxWithheld, b.DeMinimisLeaveDays, warnings);
}
