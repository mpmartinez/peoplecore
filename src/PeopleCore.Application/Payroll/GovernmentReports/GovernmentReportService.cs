using System.Globalization;
using PeopleCore.Application.Common.Time;
using PeopleCore.Application.Employees.Interfaces;
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
    private readonly IBir2316Service _bir2316;
    private readonly IEmployeeRepository _employees;

    public GovernmentReportService(IPayrollRunRepository runs, ICompanyRepository companies,
        IPayrollSettingsRepository settings, TimeProvider clock, IBir2316Service bir2316, IEmployeeRepository employees)
    {
        _runs = runs;
        _companies = companies;
        _settings = settings;
        _clock = clock;
        _bir2316 = bir2316;
        _employees = employees;
    }

    public async Task<GovernmentReportDto> BuildAsync(string report, int year, int month, CancellationToken ct = default)
    {
        var key = report.ToLowerInvariant();
        if (key is not ("sss" or "philhealth" or "pagibig" or "1601c"))
            throw new KeyNotFoundException($"There is no '{report}' report.");

        if (month is < 1 or > 12)
            throw new DomainException("Choose a month from 1 to 12.");
        if (year is < 1 or > 9999)
            throw new DomainException("Choose a year.");
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
        else
        {
            bool tinBlank = string.IsNullOrWhiteSpace(company.TIN);
            bool agencyNumberBlank = string.IsNullOrWhiteSpace(employerNumber);
            // On 1601-C the agency number IS the TIN, so checking both here would warn twice
            // about the same blank field.
            if (key != "1601c" && tinBlank && agencyNumberBlank)
                warnings.Add($"The company's TIN and {agency} number are blank. Add them on the Company page.");
            else if (key != "1601c" && tinBlank)
                warnings.Add("The company's TIN is blank. Add it on the Company page.");
            else if (agencyNumberBlank)
                warnings.Add($"The company's {agency} number is blank. Add it on the Company page.");
        }

        (IReadOnlyList<string> columns, List<GovernmentReportRowDto> rows, IReadOnlyList<string> totals,
         IReadOnlyList<GovernmentReportLineDto> summary) = key switch
        {
            "sss" => await SssAsync(people, runs, warnings, ct),
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

        return new GovernmentReportDto(key, title, year, month, basis, employer, columns, rows, totals, summary, warnings, []);
    }

    public async Task<GovernmentReportDto> BuildAnnualAsync(string report, int year, CancellationToken ct = default)
    {
        if (!string.Equals(report, "1604c", StringComparison.OrdinalIgnoreCase))
            throw new KeyNotFoundException($"There is no annual '{report}' report.");
        if (year is < 1 or > 9999)
            throw new DomainException("Choose a year.");
        var today = DateOnly.FromDateTime(PhilippineTime.Now(_clock));
        if (year > today.Year)
            throw new DomainException($"{year} hasn't started yet, so there is nothing to report.");

        var forms = await _bir2316.BuildAllAsync(year, ct);

        // January-November and December tax withheld aren't on the 2316; they come from the same
        // year's Paid runs by pay month, and add up to its present-employer tax withheld.
        // Same Paid/PayDate.Year predicate as Bir2316Service.BuildAsync, re-applied here so this
        // split can't silently disagree with the present-employer withheld tax (Item 25A) behind it.
        var runs = (await _runs.GetPaidRunsInYearAsync(year, ct))
            .Where(r => r.Status == PayrollRunStatus.Paid && r.PayDate.Year == year)
            .ToList();
        var withheldByEmployee = runs
            .SelectMany(r => r.Employees.Select(e => (e.EmployeeId, December: r.PayDate.Month == 12, e.WithholdingTax)))
            .GroupBy(x => x.EmployeeId)
            .ToDictionary(g => g.Key, g => (JanToNov: g.Where(x => !x.December).Sum(x => x.WithholdingTax),
                                            December: g.Where(x => x.December).Sum(x => x.WithholdingTax)));

        var employees = (await _employees.GetByIdsAsync(forms.Select(f => f.EmployeeId), ct)).ToDictionary(e => e.Id);
        var people = forms
            .Where(f => employees.ContainsKey(f.EmployeeId))
            .Select(f =>
            {
                var e = employees[f.EmployeeId];
                var withheld = withheldByEmployee.GetValueOrDefault(f.EmployeeId);
                return new Bir1604CAlphalist.Person(f.EmployeeId, f, e.HireDate, e.SeparationDate, withheld.JanToNov, withheld.December);
            })
            .ToList();

        var company = await _companies.GetDefaultAsync(ct);
        var employer = new GovernmentReportEmployerDto(company?.Name ?? "", company?.Address, company?.TIN ?? "", company?.RdoCode, company?.TIN ?? "");
        var result = Bir1604CAlphalist.Build(year, employer, people, await _runs.CountUnpaidRunsPaidInYearAsync(year, ct));

        // Company checks, worded as the monthly reports word them.
        var companyWarnings = new List<string>();
        if (company is null)
            companyWarnings.Add("The company's details are missing. Fill in the Company page.");
        else
        {
            bool tinBlank = string.IsNullOrWhiteSpace(company.TIN), rdoBlank = string.IsNullOrWhiteSpace(company.RdoCode);
            if (tinBlank && rdoBlank) companyWarnings.Add("The company's TIN and RDO code are blank. Add them on the Company page.");
            else if (tinBlank) companyWarnings.Add("The company's TIN is blank. Add it on the Company page.");
            else if (rdoBlank) companyWarnings.Add("The company's RDO code is blank. Add it on the Company page.");
        }
        return result with { Warnings = [.. companyWarnings, .. result.Warnings] };
    }

    private async Task<(IReadOnlyList<string>, List<GovernmentReportRowDto>, IReadOnlyList<string>, IReadOnlyList<GovernmentReportLineDto>)>
        SssAsync(List<(Employee Employee, List<PayrollRunEmployee> Entries)> people, IReadOnlyList<PayrollRun> runs,
            List<string> warnings, CancellationToken ct)
    {
        // Working the MSC and EC back only holds under the statutory schedule, which is what
        // ComputeSSS uses unless both rates are overridden.
        var settings = await _settings.GetDefaultAsync(ct);
        bool overridden = settings?.SSSEmployeeRate is not null && settings.SSSEmployerRate is not null;
        if (overridden)
            warnings.Add("The company's payroll settings override the SSS rates, so the MSC and EC can't be worked out and are left blank.");

        // Payroll deducts half the month's SSS in each semi-monthly cutoff and all of it in a
        // monthly run, so the MSC only works back once the month's cutoffs are all in; a single
        // cutoff's half share would understate the MSC and the EC that goes with it. Cutoffs are
        // counted rather than calendar days checked, because many companies' cutoffs (26th-10th,
        // 21st-20th) never cover the end of the month they close in. A final pay completes the
        // month whatever its frequency: it tops the month's SSS up to the whole month.
        var sssCutoffsByEmployee = runs
            .SelectMany(r => r.Employees.Where(e => e.SSSEmployee > 0).Select(e => (e.EmployeeId, r.Frequency, r.RunType)))
            .ToLookup(x => x.EmployeeId, x => (x.Frequency, x.RunType));

        var rows = new List<GovernmentReportRowDto>();
        decimal ee = 0, erSs = 0, ec = 0, er = 0;
        int partialMonthCount = 0;
        foreach (var (employee, entries) in people)
        {
            decimal employeeShare = entries.Sum(e => e.SSSEmployee);
            decimal employerTotal = entries.Sum(e => e.SSSEmployer);
            var cutoffs = sssCutoffsByEmployee[employee.Id].ToList();
            bool fullMonth = cutoffs.Any(c => c.RunType == PayrollRunType.FinalPay)
                             || cutoffs.Any(c => c.Frequency == PayFrequency.Monthly)
                             || cutoffs.Count(c => c.Frequency == PayFrequency.SemiMonthly) >= 2;

            (decimal? msc, decimal? credit) = (null, null);
            if (!overridden && fullMonth)
                (msc, credit) = SssCredit(employeeShare);
            else if (!overridden && !fullMonth && employeeShare > 0)
                partialMonthCount++;
            decimal? employerSs = credit is { } c ? employerTotal - c : null;

            ee += employeeShare; er += employerTotal;
            if (credit is not null) { ec += credit.Value; erSs += employerSs!.Value; }
            var number = Id(employee, GovernmentIdType.SSS);
            rows.Add(new(employee.Id, [
                FullName(employee), number ?? "", Blank(msc), Money(employeeShare), Blank(employerSs), Blank(credit),
                Money(employerTotal), Money(employeeShare + employerTotal)
            ], number is null));
        }

        if (partialMonthCount > 0)
            warnings.Add(partialMonthCount == 1
                ? "The MSC and EC are left blank for 1 employee whose pay for the whole month isn't paid yet."
                : $"The MSC and EC are left blank for {partialMonthCount} employees whose pay for the whole month isn't paid yet.");

        // The employer share and EC totals only stand when every row has them; totalled over some
        // rows they would disagree with the Employer total column beside them.
        bool allWorkedBack = !overridden && partialMonthCount == 0;
        return (["Employee", "SSS number", "MSC", "Employee share", "Employer share", "EC", "Employer total", "Total"],
                rows,
                ["Total", "", "", Money(ee), allWorkedBack ? Money(erSs) : "", allWorkedBack ? Money(ec) : "", Money(er), Money(ee + er)],
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
        // "13th month and other benefits" - the 13th month plus leave converted beyond de minimis
        // (PayrollRunEmployee.ThirteenthMonthAndOtherBenefits) - paid earlier in the year has used
        // its share of the 90,000 exemption first, as the 2316 counts it. Skip the year-wide query
        // entirely when nothing this month even has any to offset - most months don't.
        Dictionary<Guid, decimal> earlierThirteenth = [];
        if (people.Any(p => p.Entries.Any(e => e.ThirteenthMonthAndOtherBenefits > 0)))
        {
            var monthStart = new DateOnly(year, month, 1);
            earlierThirteenth = (await _runs.GetPaidRunsInYearAsync(year, ct))
                .Where(r => r.PayDate < monthStart)
                .SelectMany(r => r.Employees)
                .GroupBy(e => e.EmployeeId)
                .ToDictionary(g => g.Key, g => g.Sum(e => e.ThirteenthMonthAndOtherBenefits));
        }

        var rows = new List<GovernmentReportRowDto>();
        decimal gross = 0, thirteenth = 0, shares = 0, deMinimis = 0, otherNonTaxable = 0, taxable = 0, tax = 0;
        foreach (var (employee, entries) in people)
        {
            decimal g = entries.Sum(e => e.GrossPay);
            decimal t13 = NonTaxableThirteenthMonth(entries.Sum(e => e.ThirteenthMonthAndOtherBenefits),
                                                    earlierThirteenth.GetValueOrDefault(employee.Id));
            decimal s = entries.Sum(e => e.SSSEmployee + e.PhilHealthEmployee + e.PagIbigEmployee);
            // LeaveConversionNonTaxable is final pay's de minimis slice - the form's own "De
            // minimis" column/line, kept apart from the "Other non-taxable" column/line below so
            // the two don't double-count it. It has its own column (between "13th month
            // (non-taxable)" and "Employee shares") because, unlike allowances, it is subtracted
            // from Compensation to reach Taxable - without the column the row's own numbers would
            // no longer add up to what's printed.
            decimal dm = entries.Sum(e => e.LeaveConversionNonTaxable);
            // NonTaxableAllowances plus whatever of final pay's non-taxable amount is NOT the de
            // minimis slice above (separation or retirement pay that qualifies for exemption) -
            // both are non-taxable compensation with no line of their own, the same catch-all
            // "Other non-taxable" bucket Bir2316Service.Item37 uses for the same reason.
            decimal otherNt = entries.Sum(e => e.NonTaxableAllowances) +
                               entries.Sum(e => e.FinalPayNonTaxable - e.LeaveConversionNonTaxable);
            decimal tx = g - t13 - s - dm - otherNt;
            decimal w = entries.Sum(e => e.WithholdingTax);

            gross += g; thirteenth += t13; shares += s; deMinimis += dm; otherNonTaxable += otherNt; taxable += tx; tax += w;
            var tin = Id(employee, GovernmentIdType.TIN);
            rows.Add(new(employee.Id,
                [FullName(employee), tin ?? "", Money(g), Money(t13), Money(dm), Money(s), Money(otherNt), Money(tx), Money(w)],
                tin is null));
        }

        decimal nonTaxable = thirteenth + shares + deMinimis + otherNonTaxable;
        return (["Employee", "TIN", "Compensation", "13th month and other benefits (non-taxable)", "De minimis", "Employee shares",
                 "Other non-taxable", "Taxable", "Tax withheld"],
                rows,
                ["Total", "", Money(gross), Money(thirteenth), Money(deMinimis), Money(shares), Money(otherNonTaxable), Money(taxable), Money(tax)],
                [
                    new("Total amount of compensation", gross),
                    new("Statutory minimum wage (MWEs)", 0m),
                    new("Holiday, overtime, night differential and hazard pay (MWEs)", 0m),
                    new("13th month pay and other benefits", thirteenth),
                    new("De minimis benefits", deMinimis),
                    new("SSS, PhilHealth and Pag-IBIG employee shares", shares),
                    new("Other non-taxable compensation", otherNonTaxable),
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
