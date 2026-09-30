using System.Globalization;
using System.Text;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Domain.Entities.Employees;
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

    /// <summary>Every amount must be below this: ₱10 billion, well inside numeric(18,2).</summary>
    private const decimal AmountLimit = 10_000_000_000m;

    /// <summary>The most problems an import lists before saying how many more there are.</summary>
    private const int MaxImportErrors = 200;

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
        return balances.Select(b => ToDto(b, DoubleCountWarnings(b, b.Employee.FullName, RunsOf(b, runs)))).ToList();
    }

    public async Task<OpeningBalanceDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var balance = await FindAsync(id, ct);
        var runs = RunsOf(balance, await _runs.GetPaidRunsInYearAsync(balance.Year, ct));
        return ToDto(balance, DoubleCountWarnings(balance, balance.Employee.FullName, runs));
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

    public async Task<OpeningBalanceImportResult> ImportAsync(Stream csv, CancellationToken ct = default)
    {
        string text;
        using (var reader = new StreamReader(csv, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true))
            text = await reader.ReadToEndAsync(ct);

        var file = OpeningBalanceCsv.Read(text);
        if (file.Problem is not null)
            return Refused([file.Problem]);

        var numbers = file.Rows.Select(r => r.EmployeeNumber).Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        var employees = (await _employees.GetByNumbersAsync(numbers, ct))
            .ToDictionary(e => e.EmployeeNumber, StringComparer.Ordinal);

        // Every row is checked before anything is loaded for saving, so a refused file changes nothing.
        var errors = new List<string>();
        var requests = new List<(Employee Employee, OpeningBalanceRequest Request)>();
        var seen = new HashSet<(string EmployeeNumber, int Year)>();
        foreach (var row in file.Rows)
        {
            var problems = new List<string>();
            Employee? employee = null;
            if (row.EmployeeNumber.Length == 0)
                problems.Add("Enter an employee number.");
            else if (!employees.TryGetValue(row.EmployeeNumber, out employee))
                problems.Add($"Unknown employee number {OpeningBalanceCsv.Echo(row.EmployeeNumber)}.");
            if (row.EmployeeNumber.Length > 0 && row.Year is { } year && !seen.Add((row.EmployeeNumber, year)))
                problems.Add($"{OpeningBalanceCsv.Echo(row.EmployeeNumber)} appears more than once for {year}.");
            problems.AddRange(row.Problems);

            // A row whose cells didn't all parse isn't validated: its figures aren't what was meant.
            if (row.Problems.Count == 0)
            {
                var n = row.Numbers;
                var request = new OpeningBalanceRequest(employee?.Id ?? Guid.Empty, row.Year!.Value,
                    row.ThroughDate!.Value, n[0], n[1], n[2], n[3], n[4], n[5], n[6], n[7], n[8]);
                if (ProblemWith(request) is { } problem)
                    problems.Add(problem);
                else if (problems.Count == 0)
                    requests.Add((employee!, request));
            }

            errors.AddRange(problems.Select(p => $"Row {row.Row}: {p}"));
        }
        if (errors.Count > MaxImportErrors)
        {
            var more = errors.Count - MaxImportErrors;
            errors = [.. errors.Take(MaxImportErrors), $"…and {more} more {(more == 1 ? "problem" : "problems")}."];
        }
        if (errors.Count > 0)
            return Refused(errors);

        var years = requests.Select(r => r.Request.Year).Distinct().ToList();
        var existing = new Dictionary<(Guid EmployeeId, int Year), PayrollOpeningBalance>();
        foreach (var year in years)
        {
            var ids = requests.Where(r => r.Request.Year == year).Select(r => r.Request.EmployeeId).ToList();
            foreach (var balance in await _balances.GetForEmployeesForUpdateAsync(ids, year, ct))
                existing[(balance.EmployeeId, year)] = balance;
        }

        var added = new List<PayrollOpeningBalance>();
        var saved = new List<(Employee Employee, PayrollOpeningBalance Balance)>();
        foreach (var (employee, request) in requests)
        {
            // An existing balance keeps its employee and year; only its through date and figures change.
            if (!existing.TryGetValue((request.EmployeeId, request.Year), out var balance))
            {
                // Only the id: the employee was read untracked, and a navigation to it would insert it.
                balance = new PayrollOpeningBalance { EmployeeId = request.EmployeeId, Year = request.Year };
                added.Add(balance);
            }
            Apply(balance, request);
            saved.Add((employee, balance));
        }
        await _balances.SaveAllAsync(added, ct);

        // Each year's Paid runs once, for every saved balance's warnings, in file order.
        var paidRuns = new Dictionary<int, IReadOnlyList<PayrollRun>?>();
        foreach (var year in years)
            paidRuns[year] = await _runs.GetPaidRunsInYearAsync(year, ct);
        // The double-count warning already opens with her name, so it takes only her number.
        var warnings = saved
            .SelectMany(s =>
            {
                var (number, name) = (s.Employee.EmployeeNumber, s.Employee.FullName);
                var runs = RunsOf(s.Balance, paidRuns[s.Balance.Year]);
                return DoubleCountWarnings(s.Balance, name, runs).Select(w => $"{number}: {w}")
                    .Concat(EditWarnings(s.Balance, runs).Select(w => $"{number} {name}: {w}"));
            })
            .ToList();
        return new OpeningBalanceImportResult(added.Count, saved.Count - added.Count, [], warnings);

        static OpeningBalanceImportResult Refused(IReadOnlyList<string> problems) => new(0, 0, problems, []);
    }

    /// <summary>The saved balance with its double-count warnings, then the edit warnings.</summary>
    private async Task<OpeningBalanceDto> SavedAsync(PayrollOpeningBalance balance, CancellationToken ct)
    {
        var runs = RunsOf(balance, await _runs.GetPaidRunsInYearAsync(balance.Year, ct));
        return ToDto(balance, SaveWarnings(balance, balance.Employee.FullName, runs));
    }

    /// <summary>What a save warns of: the double-count warnings, then the edit warnings.</summary>
    private static List<string> SaveWarnings(PayrollOpeningBalance balance, string name, List<PayrollRun> runs)
        => [.. DoubleCountWarnings(balance, name, runs), .. EditWarnings(balance, runs)];

    /// <summary>
    /// One warning per Paid run of hers paid after the through date: it used the figures and won't
    /// be recomputed.
    /// </summary>
    private static IEnumerable<string> EditWarnings(PayrollOpeningBalance balance, IEnumerable<PayrollRun> runs)
        => runs.Where(r => r.PayDate > balance.ThroughDate)
            .Select(r => $"{r.RunNumber} used these figures; its 13th month and tax won't change. " +
                         "Reissue the 2316 to pick up the change.");

    /// <summary>The Paid runs in the balance's year that paid its employee, earliest pay date first.</summary>
    private static List<PayrollRun> RunsOf(PayrollOpeningBalance balance, IReadOnlyList<PayrollRun>? paidRuns)
        => (paidRuns ?? [])
            .Where(r => r.PayDate.Year == balance.Year && r.Employees.Any(e => e.EmployeeId == balance.EmployeeId))
            .OrderBy(r => r.PayDate)
            .ThenBy(r => r.RunNumber, StringComparer.Ordinal)
            .ToList();

    /// <summary>One warning per Paid run of hers paid on or before the through date: pay the balance already covers.</summary>
    private static List<string> DoubleCountWarnings(PayrollOpeningBalance balance, string name, IEnumerable<PayrollRun> runs)
        => runs.Where(r => r.PayDate <= balance.ThroughDate)
            .Select(r => $"{name}'s opening balance already covers pay through " +
                         $"{Date(balance.ThroughDate)}; {r.RunNumber} was paid on {Date(r.PayDate)}.")
            .ToList();

    private static string Date(DateOnly date) => date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

    private static void EnsureYear(int year)
    {
        if (!IsYear(year))
            throw new DomainException("Enter a year.");
    }

    // DateOnly's range; anything earlier than 1900 is a typo, not a payroll year.
    private static bool IsYear(int year) => year is >= 1900 and <= 9999;

    private static void Validate(OpeningBalanceRequest request)
    {
        if (ProblemWith(request) is { } problem)
            throw new DomainException(problem);
    }

    /// <summary>
    /// The first thing wrong with the request's year, through date and figures, or null. The form
    /// and the CSV import both go through it, so they refuse exactly the same things.
    /// </summary>
    private static string? ProblemWith(OpeningBalanceRequest request)
    {
        if (!IsYear(request.Year))
            return "Enter a year.";
        if (request.ThroughDate.Year != request.Year)
            return $"The through date must fall in {request.Year}.";
        decimal[] amounts =
        [
            request.BasicSalary, request.ThirteenthMonthPaid, request.OtherBenefitsPaid, request.OtherTaxablePay,
            request.DeMinimis, request.OtherNonTaxable, request.EmployeeContributions, request.TaxWithheld,
        ];
        if (amounts.Any(a => a < 0m))
            return "Amounts can't be negative.";
        // A fat-fingered figure is refused here rather than failing the save past numeric(18,2).
        if (amounts.Any(a => a >= AmountLimit))
            return "Enter an amount below ₱10,000,000,000.";
        // The 2316 certifies the basic net of the contributions (Item 39); more contributions than
        // basic would certify a negative basic salary.
        if (request.EmployeeContributions > request.BasicSalary)
            return "Contributions can't be more than the basic salary.";
        if (request.DeMinimisLeaveDays is < 0m or > MaxDeMinimisLeaveDays)
            return "De minimis leave days must be between 0 and 10.";
        return null;
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
