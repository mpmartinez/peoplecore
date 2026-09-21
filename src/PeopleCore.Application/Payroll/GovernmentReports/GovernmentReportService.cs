using System.Globalization;
using PeopleCore.Application.Common.Time;
using PeopleCore.Application.Organization.Interfaces;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using static PeopleCore.Application.Payroll.GovernmentReports.GovernmentReportMath;

namespace PeopleCore.Application.Payroll.GovernmentReports;

/// <summary>
/// The monthly SSS, PhilHealth, Pag-IBIG and BIR 1601-C reports, built from Paid payroll runs as
/// they stand. SSS, PhilHealth and Pag-IBIG count a run toward the month its period ends in, the
/// month the pay was earned; 1601-C toward the month it was paid, as BIR taxes compensation when
/// paid. Nothing here is stored, so a report cannot disagree with the payslips it summarises.
/// </summary>
public sealed class GovernmentReportService : IGovernmentReportService
{
    private readonly IPayrollRunRepository _runs;
    private readonly ICompanyRepository _companies;
    private readonly IPayrollSettingsRepository _settings;
    private readonly TimeProvider _clock;

    public GovernmentReportService(IPayrollRunRepository runs, ICompanyRepository companies,
        IPayrollSettingsRepository settings, TimeProvider clock)
    {
        _runs = runs;
        _companies = companies;
        _settings = settings;
        _clock = clock;
    }

    public async Task<GovernmentReportDto> BuildAsync(string report, int year, int month, CancellationToken ct = default)
    {
        var key = report.ToLowerInvariant();
        if (key is not ("sss" or "philhealth" or "pagibig" or "1601c"))
            throw new KeyNotFoundException($"There is no '{report}' report.");

        if (month is < 1 or > 12)
            throw new DomainException("Choose a month from 1 to 12.");
        var today = DateOnly.FromDateTime(PhilippineTime.Now(_clock));
        if (new DateOnly(year, month, 1) > new DateOnly(today.Year, today.Month, 1))
            throw new DomainException($"{MonthName(year, month)} hasn't started yet, so there is nothing to report.");

        bool byPayDate = key == "1601c";
        var runs = byPayDate
            ? await _runs.GetPaidRunsByPayMonthAsync(year, month, ct)
            : await _runs.GetPaidRunsByPeriodEndMonthAsync(year, month, ct);
        var people = runs.SelectMany(r => r.Employees)
            .GroupBy(e => e.EmployeeId)
            .Select(g => (Employee: g.First().Employee!, Entries: g.ToList()))
            .OrderBy(p => p.Employee.LastName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Employee.FirstName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var company = await _companies.GetDefaultAsync(ct);
        var warnings = new List<string>();
        var basis = byPayDate ? $"Paid in {MonthName(year, month)}" : $"Pay earned in {MonthName(year, month)}";

        var (title, idType, agency, employerNumber) = key switch
        {
            "sss" => ("SSS contributions", GovernmentIdType.SSS, "SSS", company?.SSSNumber),
            "philhealth" => ("PhilHealth premiums", GovernmentIdType.PhilHealth, "PhilHealth", company?.PhilHealthNumber),
            "pagibig" => ("Pag-IBIG contributions", GovernmentIdType.PagIbig, "Pag-IBIG", company?.PagIbigNumber),
            _ => ("BIR 1601-C", GovernmentIdType.TIN, "TIN", company?.TIN)
        };

        if (company is null)
            warnings.Add("The company's details are missing. Fill in the Company page.");
        else if (string.IsNullOrWhiteSpace(employerNumber))
            warnings.Add($"The company's {agency} number is blank. Add it on the Company page.");

        (IReadOnlyList<string> columns, List<GovernmentReportRowDto> rows, IReadOnlyList<string> totals,
         IReadOnlyList<GovernmentReportLineDto> summary) = key switch
        {
            "sss" => await SssAsync(people, warnings, ct),
            "philhealth" => PhilHealth(people),
            "pagibig" => PagIbig(people),
            _ => await Bir1601CAsync(people, year, month, ct)
        };

        int missing = rows.Count(r => r.MissingNumber);
        if (missing > 0)
            warnings.Add(missing == 1
                ? $"1 employee has no {agency} number."
                : $"{missing} employees have no {agency} number.");

        int unpaid = await _runs.CountUnpaidRunsAsync(year, month, byPayDate, ct);
        if (unpaid > 0)
            warnings.Add(unpaid == 1
                ? "1 payroll run for this month isn't paid yet and isn't included."
                : $"{unpaid} payroll runs for this month aren't paid yet and aren't included.");

        var employer = new GovernmentReportEmployerDto(
            company?.Name ?? "", company?.Address, company?.TIN ?? "", company?.RdoCode, employerNumber ?? "");

        return new GovernmentReportDto(key, title, year, month, basis, employer, columns, rows, totals, summary, warnings);
    }

    private async Task<(IReadOnlyList<string>, List<GovernmentReportRowDto>, IReadOnlyList<string>, IReadOnlyList<GovernmentReportLineDto>)>
        SssAsync(List<(Employee Employee, List<PayrollRunEmployee> Entries)> people, List<string> warnings, CancellationToken ct)
    {
        // Working the MSC and EC back only holds under the statutory schedule, which is what
        // ComputeSSS uses unless both rates are overridden.
        var settings = await _settings.GetDefaultAsync(ct);
        bool overridden = settings?.SSSEmployeeRate is not null && settings.SSSEmployerRate is not null;
        if (overridden)
            warnings.Add("The company's payroll settings override the SSS rates, so the MSC and EC can't be worked out and are left blank.");

        var rows = new List<GovernmentReportRowDto>();
        decimal ee = 0, erSs = 0, ec = 0, er = 0;
        foreach (var (employee, entries) in people)
        {
            decimal employeeShare = entries.Sum(e => e.SSSEmployee);
            decimal employerTotal = entries.Sum(e => e.SSSEmployer);
            var (msc, credit) = overridden ? ((decimal?)null, (decimal?)null) : SssCredit(employeeShare);
            decimal? employerSs = credit is { } c ? employerTotal - c : null;

            ee += employeeShare; er += employerTotal; ec += credit ?? 0; erSs += employerSs ?? 0;
            var number = Id(employee, GovernmentIdType.SSS);
            rows.Add(new(employee.Id, [
                FullName(employee), number ?? "", Blank(msc), Money(employeeShare), Blank(employerSs), Blank(credit),
                Money(employerTotal), Money(employeeShare + employerTotal)
            ], number is null));
        }

        return (["Employee", "SSS number", "MSC", "Employee share", "Employer share", "EC", "Employer total", "Total"],
                rows,
                ["Total", "", "", Money(ee), overridden ? "" : Money(erSs), overridden ? "" : Money(ec), Money(er), Money(ee + er)],
                []);
    }

    private static (IReadOnlyList<string>, List<GovernmentReportRowDto>, IReadOnlyList<string>, IReadOnlyList<GovernmentReportLineDto>)
        PhilHealth(List<(Employee Employee, List<PayrollRunEmployee> Entries)> people)
    {
        var rows = new List<GovernmentReportRowDto>();
        decimal ee = 0, er = 0;
        foreach (var (employee, entries) in people)
        {
            decimal employeeShare = entries.Sum(e => e.PhilHealthEmployee);
            decimal employerShare = entries.Sum(e => e.PhilHealthEmployer);
            ee += employeeShare; er += employerShare;
            var number = Id(employee, GovernmentIdType.PhilHealth);
            rows.Add(new(employee.Id, [FullName(employee), number ?? "", Money(employeeShare), Money(employerShare),
                                       Money(employeeShare + employerShare)], number is null));
        }

        return (["Employee", "PhilHealth number", "Employee share", "Employer share", "Total"], rows,
                ["Total", "", Money(ee), Money(er), Money(ee + er)], []);
    }

    private static (IReadOnlyList<string>, List<GovernmentReportRowDto>, IReadOnlyList<string>, IReadOnlyList<GovernmentReportLineDto>)
        PagIbig(List<(Employee Employee, List<PayrollRunEmployee> Entries)> people)
    {
        var rows = new List<GovernmentReportRowDto>();
        decimal ee = 0, er = 0;
        foreach (var (employee, entries) in people)
        {
            decimal employeeShare = entries.Sum(e => e.PagIbigEmployee);
            decimal employerShare = entries.Sum(e => e.PagIbigEmployer);
            ee += employeeShare; er += employerShare;
            var number = Id(employee, GovernmentIdType.PagIbig);
            rows.Add(new(employee.Id, [
                employee.LastName, employee.FirstName, employee.MiddleName ?? "",
                employee.DateOfBirth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), number ?? "",
                Money(employeeShare), Money(employerShare), Money(employeeShare + employerShare)
            ], number is null));
        }

        return (["Last name", "First name", "Middle name", "Date of birth", "Pag-IBIG number", "Employee share", "Employer share", "Total"],
                rows, ["Total", "", "", "", "", Money(ee), Money(er), Money(ee + er)], []);
    }

    private async Task<(IReadOnlyList<string>, List<GovernmentReportRowDto>, IReadOnlyList<string>, IReadOnlyList<GovernmentReportLineDto>)>
        Bir1601CAsync(List<(Employee Employee, List<PayrollRunEmployee> Entries)> people, int year, int month, CancellationToken ct)
    {
        // 13th month paid earlier in the year has used its share of the exemption first, as it did
        // when payroll withheld tax on this month's.
        var monthStart = new DateOnly(year, month, 1);
        var earlierThirteenth = (await _runs.GetPaidRunsInYearAsync(year, ct))
            .Where(r => r.PayDate < monthStart)
            .SelectMany(r => r.Employees)
            .GroupBy(e => e.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Sum(e => e.ThirteenthMonth));

        var rows = new List<GovernmentReportRowDto>();
        decimal gross = 0, thirteenth = 0, shares = 0, allowances = 0, taxable = 0, tax = 0;
        foreach (var (employee, entries) in people)
        {
            decimal g = entries.Sum(e => e.GrossPay);
            decimal t13 = NonTaxableThirteenthMonth(entries.Sum(e => e.ThirteenthMonth),
                                                    earlierThirteenth.GetValueOrDefault(employee.Id));
            decimal s = entries.Sum(e => e.SSSEmployee + e.PhilHealthEmployee + e.PagIbigEmployee);
            decimal a = entries.Sum(e => e.NonTaxableAllowances);
            decimal tx = g - t13 - s - a;
            decimal w = entries.Sum(e => e.WithholdingTax);

            gross += g; thirteenth += t13; shares += s; allowances += a; taxable += tx; tax += w;
            var tin = Id(employee, GovernmentIdType.TIN);
            rows.Add(new(employee.Id, [FullName(employee), tin ?? "", Money(g), Money(t13), Money(s), Money(a), Money(tx), Money(w)],
                         tin is null));
        }

        decimal nonTaxable = thirteenth + shares + allowances;
        return (["Employee", "TIN", "Compensation", "13th month (non-taxable)", "Employee shares", "Other non-taxable", "Taxable", "Tax withheld"],
                rows,
                ["Total", "", Money(gross), Money(thirteenth), Money(shares), Money(allowances), Money(taxable), Money(tax)],
                [
                    new("Total amount of compensation", gross),
                    new("Statutory minimum wage (MWEs)", 0m),
                    new("Holiday, overtime, night differential and hazard pay (MWEs)", 0m),
                    new("13th month pay and other benefits", thirteenth),
                    new("De minimis benefits", 0m),
                    new("SSS, PhilHealth and Pag-IBIG employee shares", shares),
                    new("Other non-taxable compensation", allowances),
                    new("Total non-taxable compensation", nonTaxable),
                    new("Total taxable compensation", gross - nonTaxable),
                    new("Total taxes withheld", tax)
                ]);
    }

    private static string? Id(Employee employee, GovernmentIdType type)
        => employee.GovernmentIds.FirstOrDefault(g => g.IdType == type && !string.IsNullOrWhiteSpace(g.IdNumber))?.IdNumber;

    private static string FullName(Employee e)
        => string.IsNullOrWhiteSpace(e.MiddleName) ? $"{e.LastName}, {e.FirstName}" : $"{e.LastName}, {e.FirstName} {e.MiddleName}";

    private static string Blank(decimal? amount) => amount is { } a ? Money(a) : "";

    private static string MonthName(int year, int month)
        => new DateOnly(year, month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);
}
