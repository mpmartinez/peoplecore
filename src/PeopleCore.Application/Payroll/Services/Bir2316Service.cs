using System.Globalization;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Organization.Interfaces;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using PeopleCore.Domain.Payroll;

namespace PeopleCore.Application.Payroll.Services;

/// <summary>
/// Aggregates one employee's calendar year of paid payroll into BIR Form 2316.
/// <para>
/// Two rules decide whether this service is correct, and both are departures from the PayZen
/// source it was ported from:
/// </para>
/// <para>
/// <b>Income is attributed by pay date.</b> PayZen aggregated on <c>PeriodStart.Year</c>. BIR taxes
/// compensation in the year it is PAID, so a run covering 26 December to 10 January and paid on
/// 15 January is the later year's income. In a semi-monthly cycle that period is the ordinary
/// case, not an edge case, and attributing it by period start shifts income between tax years for
/// a large part of the workforce.
/// </para>
/// <para>
/// <b>Derived figures are never read from caller input.</b> PayZen's generate endpoint took an
/// entire <c>BIR2316Dto</c> in the request body and rendered whatever it was given, so a caller
/// could post any withheld-tax figure and receive a tax certificate stating it. Here
/// <see cref="BuildAsync"/> recomputes every derived figure from payroll and overlays only
/// <see cref="Bir2316ManualInputs"/>, whose fields are exactly what a human legitimately supplies.
/// <see cref="GetPreviewAsync"/> is the same call with an empty overlay, so preview and generation
/// cannot present two different sets of numbers.
/// </para>
/// </summary>
public class Bir2316Service : IBir2316Service
{
    private readonly IPayrollRunRepository _runRepo;
    private readonly IEmployeeRepository _employeeRepo;
    private readonly ICompanyRepository _companyRepo;

    public Bir2316Service(
        IPayrollRunRepository runRepo,
        IEmployeeRepository employeeRepo,
        ICompanyRepository companyRepo)
    {
        _runRepo = runRepo;
        _employeeRepo = employeeRepo;
        _companyRepo = companyRepo;
    }

    public async Task<IReadOnlyList<int>> GetAvailableYearsAsync(Guid employeeId, CancellationToken ct = default)
        => await _runRepo.GetPaidYearsForEmployeeAsync(employeeId, ct);

    public async Task<IReadOnlyList<Guid>> GetEmployeeIdsWithPaidRunsAsync(int year, CancellationToken ct = default)
        => await _runRepo.GetEmployeeIdsWithPaidRunsInYearAsync(year, ct);

    public Task<Bir2316Dto?> GetPreviewAsync(Guid employeeId, int year, CancellationToken ct = default)
        => BuildAsync(employeeId, year, new Bir2316ManualInputs(), ct);

    public async Task<Bir2316Dto?> BuildAsync(
        Guid employeeId, int year, Bir2316ManualInputs manual, CancellationToken ct = default)
    {
        var employee = await _employeeRepo.GetByIdAsync(employeeId, ct);
        if (employee is null)
            return null;

        // The repository query already restricts to Paid runs in this pay-date year. Both rules
        // are re-applied here rather than trusted: they are what makes the certificate right, so
        // they belong somewhere unit-testable, and a future widening of the query - to feed a
        // register, say - must not be able to admit a Draft run or another year's pay into a tax
        // certificate as a side effect.
        var runs = (await _runRepo.GetPaidRunsForEmployeeInYearAsync(employeeId, year, ct))
            .Where(r => r.Status == PayrollRunStatus.Paid
                        && r.PayDate.Year == year
                        && r.Employees.Any(e => e.EmployeeId == employeeId))
            .ToList();

        // Whole runs come back, carrying every employee's entry - pick out only this employee's
        // line, exactly as PayslipService already does for a single run.
        var entries = runs
            .SelectMany(r => r.Employees.Where(e => e.EmployeeId == employeeId))
            .ToList();

        if (entries.Count == 0)
            return null;

        var company = await _companyRepo.GetDefaultAsync(ct)
            ?? throw new DomainException(
                "No Company record is configured. The database seeder always creates one, so " +
                "its absence means the database is misconfigured.");

        decimal thirteenthMonthTotal = entries.Sum(e => e.ThirteenthMonth);
        decimal thirteenthMonthNonTaxable = Math.Min(thirteenthMonthTotal, StatutoryCaps.ThirteenthMonthExemption);
        decimal thirteenthMonthTaxable = Math.Max(0m, thirteenthMonthTotal - StatutoryCaps.ThirteenthMonthExemption);

        return new Bir2316Dto
        {
            Year = year,
            PeriodFrom = Format(runs.Min(r => r.PeriodStart)),
            PeriodTo = Format(runs.Max(r => r.PeriodEnd)),

            // Part I — the employee. TIN comes from the EmployeeGovernmentId row: the M2NET.Core
            // base Employee.TIN is explicitly ignored by EmployeeConfiguration ("PeopleCore uses
            // GovernmentIds collection"), so it has no column and is always empty off the
            // database. GetGovId in PayrollExportService is the same lookup.
            EmployeeTin = GovernmentId(employee, GovernmentIdType.TIN),
            EmployeeLastName = employee.LastName,
            EmployeeFirstName = employee.FirstName,
            EmployeeMiddleName = employee.MiddleName ?? "",
            RdoCode = employee.RdoCode ?? "",
            RegisteredAddress = employee.Address ?? "",
            RegisteredZipCode = employee.ZipCode ?? "",
            DateOfBirth = employee.DateOfBirth.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture),
            ContactNumber = employee.MobileNumber ?? "",

            // Part II — the employer. Single-company deployment, so this is always the main one.
            EmployerTin = company.TIN,
            EmployerName = company.Name,
            EmployerAddress = company.Address ?? "",
            EmployerZipCode = company.ZipCode ?? "",
            EmployerRdoCode = company.RdoCode ?? "",
            IsMainEmployer = true,

            // Part III and the minimum-wage block — supplied by a human, never derived.
            PrevEmployerTin = manual.PrevEmployerTin ?? "",
            PrevEmployerName = manual.PrevEmployerName ?? "",
            PrevEmployerAddress = manual.PrevEmployerAddress ?? "",
            PrevEmployerZipCode = manual.PrevEmployerZipCode ?? "",
            IsMinimumWageEarner = manual.IsMinimumWageEarner,
            StatutoryMinWagePerDay = manual.StatutoryMinWagePerDay,
            StatutoryMinWagePerMonth = manual.StatutoryMinWagePerMonth,

            // Part IV-B Section A — non-taxable.
            Item33_HazardPayMwe = manual.Item33_HazardPayMwe,
            Item34_ThirteenthMonthAndBenefits = thirteenthMonthNonTaxable,
            Item35_DeMinimis = manual.Item35_DeMinimis,
            Item36_SssPhicPagibigContributions =
                entries.Sum(e => e.SSSEmployee + e.PhilHealthEmployee + e.PagIbigEmployee),
            // NonTaxableAllowances is non-taxable compensation that is not de minimis, not a
            // contribution and not 13th-month pay - Item 37 ("Salaries and Other Forms of
            // Compensation") is Section A's catch-all for exactly that: everything non-taxable
            // that lacks its own numbered box. Putting it here, rather than skipping it, is what
            // keeps Item 19 (gross compensation) from understating what the employee was actually
            // paid.
            Item37_SalariesOtherForms = entries.Sum(e => e.NonTaxableAllowances),

            // Part IV-B Section B and the supplementary block — taxable.
            //
            // PayrollComputationService's withholding base is
            // regularPay + overtimePay + holidayPay + nightDiffPay + taxableAllowances, so every
            // one of those five components has to land in a taxable box here or Item 21/23 (the
            // certificate's taxable compensation) understates what tax was actually withheld
            // against - silently manufacturing a false "tax due < tax withheld" result for any
            // employee who worked a holiday, drew night differential, or received a taxable
            // allowance. Item39/50 already carry RegularPay/OvertimePay; HolidayPay,
            // NightDiffPay and TaxableAllowances have no dedicated taxable box on the form (their
            // only numbered boxes - Items 30-32 - are the Section A exemption for minimum-wage
            // earners), so they go into the "Others (specify)" boxes Section B provides for
            // exactly this: compensation that is real and taxable but has no line of its own.
            // Holiday pay and night differential are placed in 44A/44B (alongside the regular
            // per-period allowances 40-43); taxable allowances go in 51A (alongside the other
            // supplemental, ad hoc pay in 45-49) since a fixed "allowance" bucket does not fit
            // Section B's first group of named, per-period pay items as cleanly as it does the
            // supplemental group. All four boxes are summed identically into Item 52, so this is
            // a labeling choice, not a computation one.
            Item39_BasicSalary = entries.Sum(e => e.RegularPay),
            Item44A_OtherAmount = entries.Sum(e => e.HolidayPay),
            Item44A_OtherLabel = "Holiday Pay",
            Item44B_OtherAmount = entries.Sum(e => e.NightDiffPay),
            Item44B_OtherLabel = "Night Shift Differential",
            Item48_TaxableThirteenthMonth = thirteenthMonthTaxable,
            Item50_OvertimePay = entries.Sum(e => e.OvertimePay),
            Item51A_OtherAmount = entries.Sum(e => e.TaxableAllowances),
            Item51A_OtherLabel = "Taxable Allowances",

            // Part IVA. Item 25A is this employer's withholding, summed from the runs; 22, 25B and
            // 27 concern another employer or another account and can only come from a human.
            // Item 24 is deliberately absent: it is a computed property on the DTO, derived from
            // Item 23 through BirWithholdingTax, and must never be summed from withheld tax - the
            // two agreeing is exactly what qualifies an employee for substituted filing.
            Item25A_PresentTaxWithheld = entries.Sum(e => e.WithholdingTax),
            Item22_PrevTaxableCompensation = manual.Item22_PrevTaxableCompensation,
            Item25B_PrevTaxWithheld = manual.Item25B_PrevTaxWithheld,
            Item27_PeraTaxCredit = manual.Item27_PeraTaxCredit
        };
    }

    /// <summary>Invariant culture: "MM/dd" would otherwise pick up the ambient date separator.</summary>
    private static string Format(DateOnly date) => date.ToString("MM/dd", CultureInfo.InvariantCulture);

    private static string GovernmentId(Employee employee, GovernmentIdType idType)
        => employee.GovernmentIds.FirstOrDefault(g => g.IdType == idType)?.IdNumber ?? "";
}
