using System.Globalization;
using PeopleCore.Application.Payroll.DTOs;
using static PeopleCore.Application.Payroll.GovernmentReports.GovernmentReportMath;

namespace PeopleCore.Application.Payroll.GovernmentReports;

/// <summary>
/// The BIR 1604-C alphalist: each employee's BIR 2316 for the year, one line each, grouped the way
/// BIR's schedules group them. Built from the certificates rather than from payroll again, so the
/// two can never disagree. Minimum wage earners have their own BIR schedule, which PeopleCore
/// doesn't model yet; the report says so rather than leaving it out silently.
/// </summary>
public static class Bir1604CAlphalist
{
    public sealed record Person(Guid EmployeeId, Bir2316Dto Form, DateOnly HireDate, DateOnly? SeparationDate,
        decimal TaxWithheldJanToNov, decimal TaxWithheldDecember);

    private const string EmptyGroup = "No employees in this group.";

    private static readonly string[] Leading =
        ["TIN", "Last name", "First name", "Middle name", "Employed from", "Employed to",
         "Gross compensation", "13th month and other benefits (non-taxable)", "De minimis",
         "SSS, PhilHealth and Pag-IBIG employee shares", "Other non-taxable compensation", "Total non-taxable",
         "Basic salary", "13th month and other benefits (taxable)", "Other taxable compensation",
         "Total taxable (present employer)"];

    private static readonly string[] Previous =
        ["Previous employer's taxable compensation", "Previous employer's tax withheld"];

    private static readonly string[] Trailing =
        ["Tax due", "Tax withheld, January to November", "Tax withheld, December", "Total tax withheld",
         "To collect / (refund)"];

    public static GovernmentReportDto Build(int year, GovernmentReportEmployerDto employer,
        IReadOnlyList<Person> people, int unpaidRuns)
    {
        var yearEnd = new DateOnly(year, 12, 31);
        bool Terminated(Person p) => p.SeparationDate is { } s && s.Year == year && s < yearEnd;
        bool HasPrevious(Person p) => p.Form.Item22_PrevTaxableCompensation != 0m || p.Form.Item25B_PrevTaxWithheld != 0m;

        var sorted = people
            .OrderBy(p => p.Form.EmployeeLastName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Form.EmployeeFirstName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sections = new List<GovernmentReportSectionDto>
        {
            Section("Terminated before December 31", sorted.Where(Terminated), year, withPrevious: false),
            Section("Employed as of December 31, no previous employer",
                sorted.Where(p => !Terminated(p) && !HasPrevious(p)), year, withPrevious: false),
            Section("Employed as of December 31, with previous employer",
                sorted.Where(p => !Terminated(p) && HasPrevious(p)), year, withPrevious: true)
        };

        var warnings = new List<string>();
        int noTin = people.Count(p => string.IsNullOrWhiteSpace(p.Form.EmployeeTin));
        if (noTin > 0)
            warnings.Add(noTin == 1 ? "1 employee has no TIN." : $"{noTin} employees have no TIN.");
        if (unpaidRuns > 0)
            warnings.Add(unpaidRuns == 1
                ? "1 payroll run paid this year isn't paid yet and isn't included."
                : $"{unpaidRuns} payroll runs paid this year aren't paid yet and aren't included.");
        int hiredWithoutPrevious = people.Count(p => p.HireDate.Year == year && !HasPrevious(p) && !Terminated(p));
        if (hiredWithoutPrevious > 0)
            warnings.Add((hiredWithoutPrevious == 1
                    ? "1 employee hired this year has no previous employer entered."
                    : $"{hiredWithoutPrevious} employees hired this year have no previous employer entered.")
                + " If they worked elsewhere earlier in the year, add it on their BIR 2316 so they move to the right group.");
        warnings.Add("The minimum wage earner schedule isn't included: PeopleCore doesn't record minimum wage earners yet.");

        return new GovernmentReportDto("1604c", "BIR 1604-C alphalist", year, 0, $"Paid in {year}", employer,
            [], [], [], [], warnings, sections);
    }

    private static GovernmentReportSectionDto Section(string title, IEnumerable<Person> people, int year, bool withPrevious)
    {
        var columns = Leading.Concat(withPrevious ? Previous : []).Concat(Trailing).ToList();
        var yearStart = new DateOnly(year, 1, 1);
        var yearEnd = new DateOnly(year, 12, 31);

        var rows = new List<GovernmentReportRowDto>();
        var sums = new decimal[columns.Count];
        foreach (var p in people)
        {
            var f = p.Form;
            decimal otherTaxable = f.Item52_TotalTaxableCompensation - f.Item39_BasicSalary - f.Item48_TaxableThirteenthMonth;
            var money = new List<decimal>
            {
                f.Item19_GrossCompensation, f.Item34_ThirteenthMonthAndBenefits, f.Item35_DeMinimis,
                f.Item36_SssPhicPagibigContributions, f.Item37_SalariesOtherForms, f.Item38_TotalNonTaxable,
                f.Item39_BasicSalary, f.Item48_TaxableThirteenthMonth, otherTaxable, f.Item52_TotalTaxableCompensation
            };
            if (withPrevious)
                money.AddRange([f.Item22_PrevTaxableCompensation, f.Item25B_PrevTaxWithheld]);
            money.AddRange([f.Item24_TaxDue, p.TaxWithheldJanToNov, p.TaxWithheldDecember, f.Item26_TotalTaxWithheld,
                            f.Item24_TaxDue - f.Item26_TotalTaxWithheld]);

            var from = p.HireDate > yearStart ? p.HireDate : yearStart;
            var to = p.SeparationDate is { } s && s < yearEnd ? s : yearEnd;
            var cells = new List<string>
            {
                f.EmployeeTin, f.EmployeeLastName, f.EmployeeFirstName, f.EmployeeMiddleName,
                from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            };
            for (int i = 0; i < money.Count; i++)
            {
                cells.Add(Money(money[i]));
                sums[6 + i] += money[i];
            }
            rows.Add(new GovernmentReportRowDto(p.EmployeeId, cells, string.IsNullOrWhiteSpace(f.EmployeeTin)));
        }

        var totals = columns.Select((_, i) => i == 0 ? "Total" : i < 6 ? "" : Money(sums[i])).ToList();
        return new GovernmentReportSectionDto(title, columns, rows, rows.Count == 0 ? [] : totals, EmptyGroup);
    }
}
