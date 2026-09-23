using System.Globalization;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Organization.Interfaces;
using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.Interfaces;
using PeopleCore.Application.Payroll.Validation;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Entities.Organization;
using PeopleCore.Domain.Entities.Payroll;
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
/// <see cref="GetPreviewAsync"/> is the same call overlaid with whatever inputs were last saved
/// for the employee and year (empty when nothing was), so preview and generation present the same
/// numbers.
/// </para>
/// </summary>
public class Bir2316Service : IBir2316Service
{
    private readonly IPayrollRunRepository _runRepo;
    private readonly IEmployeeRepository _employeeRepo;
    private readonly ICompanyRepository _companyRepo;
    private readonly IBir2316InputsRepository _inputsRepo;

    public Bir2316Service(
        IPayrollRunRepository runRepo,
        IEmployeeRepository employeeRepo,
        ICompanyRepository companyRepo,
        IBir2316InputsRepository inputsRepo)
    {
        _runRepo = runRepo;
        _employeeRepo = employeeRepo;
        _companyRepo = companyRepo;
        _inputsRepo = inputsRepo;
    }

    public async Task<IReadOnlyList<int>> GetAvailableYearsAsync(Guid employeeId, CancellationToken ct = default)
        => await _runRepo.GetPaidYearsForEmployeeAsync(employeeId, ct);

    public async Task<IReadOnlyList<Guid>> GetEmployeeIdsWithPaidRunsAsync(int year, CancellationToken ct = default)
        => await _runRepo.GetEmployeeIdsWithPaidRunsInYearAsync(year, ct);

    public async Task<Bir2316Dto?> GetPreviewAsync(Guid employeeId, int year, CancellationToken ct = default)
    {
        var saved = await _inputsRepo.GetAsync(employeeId, year, ct);
        return await BuildAsync(employeeId, year, saved?.ToManualInputs() ?? new Bir2316ManualInputs(), ct);
    }

    /// <summary>
    /// Builds the certificate with the inputs HR entered and, when the employee was paid in the
    /// year, saves them - so the next preview, "Generate all" and the 1604-C alphalist use the same
    /// figures. <see cref="BuildAsync"/> itself saves nothing.
    /// </summary>
    public async Task<Bir2316Dto?> GenerateAsync(Guid employeeId, int year, Bir2316ManualInputs manual, CancellationToken ct = default)
    {
        var dto = await BuildAsync(employeeId, year, manual, ct);
        if (dto is not null)
            await _inputsRepo.SaveAsync(employeeId, year, manual, ct);
        return dto;
    }

    public async Task<Bir2316ManualInputs> GetInputsAsync(Guid employeeId, int year, CancellationToken ct = default)
        => (await _inputsRepo.GetAsync(employeeId, year, ct))?.ToManualInputs() ?? new Bir2316ManualInputs();

    public async Task<Bir2316Dto?> BuildAsync(
        Guid employeeId, int year, Bir2316ManualInputs manual, CancellationToken ct = default)
    {
        // Before the employee lookup on purpose: a malformed overlay is the caller's error either
        // way, and rejecting it without a database round trip keeps the failure cheap.
        Bir2316ManualInputsValidator.Validate(manual);

        var employee = await _employeeRepo.GetByIdAsync(employeeId, ct);
        if (employee is null)
            return null;

        var (runs, entries) = await LoadRunsAndEntriesAsync(employeeId, year, draftRun: null, draftEntry: null, ct);
        if (entries.Count == 0)
            return null;

        var company = await _companyRepo.GetDefaultAsync(ct)
            ?? throw new DomainException(
                "No Company record is configured. The database seeder always creates one, so " +
                "its absence means the database is misconfigured.");

        return BuildDto(employee, company, runs, entries, year, manual);
    }

    /// <summary>
    /// Every employee with a paid run in <paramref name="year"/>, each built with whatever
    /// <see cref="Bir2316ManualInputs"/> was last saved for that employee and year (empty when
    /// nothing was) - the bulk equivalent of <see cref="BuildAsync"/> for GenerateAll.
    /// <para>
    /// <see cref="BuildAsync"/> re-queries this employee's paid runs (with the same predicate
    /// re-applied, plus a full 6-<c>Include</c> employee load and a company lookup) on every call,
    /// which is fine once but is O(headcount) work when called in a loop: at real headcount that
    /// is hundreds of queries, each materialising and discarding every OTHER employee's entries
    /// out of the run. This method instead fetches the year's paid runs ONCE, groups their entries
    /// by employee, batch-loads the employees that actually have one, and looks the company up
    /// once - four queries regardless of headcount instead of roughly three times the employee
    /// count.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<Bir2316Dto>> BuildAllAsync(int year, CancellationToken ct = default)
    {
        // Ordered by last name then first name (GetEmployeeIdsWithPaidRunsInYearAsync's remarks)
        // so a merged PDF's page order is deterministic and two exports for the same year are
        // comparable. This also fixes the set and the order of who gets built; the runs query
        // below only supplies each employee's entries.
        var employeeIds = await _runRepo.GetEmployeeIdsWithPaidRunsInYearAsync(year, ct);
        if (employeeIds.Count == 0)
            return [];

        // Same Paid/PayDate.Year predicate as BuildAsync, re-applied for the same reason: it is
        // what makes the certificate right, so it belongs somewhere that cannot be silently
        // widened by a future change to the repository query.
        var runs = (await _runRepo.GetPaidRunsInYearAsync(year, ct))
            .Where(r => r.Status == PayrollRunStatus.Paid && r.PayDate.Year == year)
            .ToList();

        var runsAndEntriesByEmployee = runs
            .SelectMany(r => r.Employees.Select(e => (Run: r, Entry: e)))
            .GroupBy(x => x.Entry.EmployeeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var employeesById = (await _employeeRepo.GetByIdsAsync(employeeIds, ct))
            .ToDictionary(e => e.Id);

        var company = await _companyRepo.GetDefaultAsync(ct)
            ?? throw new DomainException(
                "No Company record is configured. The database seeder always creates one, so " +
                "its absence means the database is misconfigured.");

        var saved = await _inputsRepo.GetForYearAsync(year, ct);

        var forms = new List<Bir2316Dto>(employeeIds.Count);
        foreach (var employeeId in employeeIds)
        {
            // Both come from the same Paid/PayDate.Year query that produced employeeIds, so a
            // miss here would mean the two repository calls disagree - it should not happen, but
            // skipping rather than throwing keeps one inconsistent record from failing everyone
            // else's certificate in the same bulk run.
            if (!runsAndEntriesByEmployee.TryGetValue(employeeId, out var runsAndEntries)
                || !employeesById.TryGetValue(employeeId, out var employee))
                continue;

            var employeeRuns = runsAndEntries.Select(x => x.Run).Distinct().ToList();
            var employeeEntries = runsAndEntries.Select(x => x.Entry).ToList();
            var manual = saved.TryGetValue(employeeId, out var s) ? s.ToManualInputs() : new Bir2316ManualInputs();

            forms.Add(BuildDto(employee, company, employeeRuns, employeeEntries, year, manual));
        }

        return forms;
    }

    /// <summary>
    /// Builds the certificate exactly as <see cref="GetPreviewAsync"/> does - the employee's Paid
    /// runs plus whatever manual inputs were last saved for the year - except
    /// <paramref name="draftRun"/> and <paramref name="draftEntry"/> are added to the runs and
    /// entries the certificate sums, even though the run itself is not Paid. This is how the
    /// final-pay flow previews (and settles the withholding tax of) a certificate that includes
    /// the final run before that run is marked Paid; <see cref="BuildAsync"/> and
    /// <see cref="GetPreviewAsync"/> deliberately cannot do this - their Paid-only filter is what
    /// keeps a certificate from changing after it was issued - so this is a separate, narrow entry
    /// point rather than a parameter on those.
    /// <para>
    /// <b>Must never be reachable from an API endpoint.</b> <paramref name="draftRun"/> and
    /// <paramref name="draftEntry"/> are caller-constructed entities, not rows this method loads
    /// and verifies itself - exactly the trust boundary the class doc comment's "derived figures
    /// are never read from caller input" rule exists to keep a request body from crossing. This
    /// method is for a trusted server-side caller (the final-pay flow) that built
    /// <paramref name="draftEntry"/> from payroll's own computation, never for one that forwards a
    /// request payload into it.
    /// </para>
    /// </summary>
    public async Task<Bir2316Dto?> BuildWithDraftEntryAsync(
        Guid employeeId, int year, PayrollRun draftRun, PayrollRunEmployee draftEntry, CancellationToken ct = default)
    {
        // Before any database round trip, for the same reason BuildAsync validates its manual
        // overlay first: a caller bug is a caller bug regardless of what payroll has on file, and
        // admitting another employee's pay onto this one's certificate is exactly the kind of
        // mistake that must never silently succeed.
        if (draftEntry.EmployeeId != employeeId)
            throw new DomainException(
                $"The draft entry belongs to employee {draftEntry.EmployeeId}, not {employeeId} - " +
                "refusing to put someone else's pay on this certificate.");

        var employee = await _employeeRepo.GetByIdAsync(employeeId, ct);
        if (employee is null)
            return null;

        var (runs, entries) = await LoadRunsAndEntriesAsync(employeeId, year, draftRun, draftEntry, ct);

        var company = await _companyRepo.GetDefaultAsync(ct)
            ?? throw new DomainException(
                "No Company record is configured. The database seeder always creates one, so " +
                "its absence means the database is misconfigured.");

        var saved = await _inputsRepo.GetAsync(employeeId, year, ct);
        var manual = saved?.ToManualInputs() ?? new Bir2316ManualInputs();
        // Mirrors GetPreviewAsync, whose saved-inputs overlay reaches this same validation inside
        // BuildAsync - the saved row is trusted storage, not a request body, but it was still
        // written by GenerateAsync's own Validate call, and this path must not skip the check
        // just because it does not route through BuildAsync.
        Bir2316ManualInputsValidator.Validate(manual);

        return BuildDto(employee, company, runs, entries, year, manual);
    }

    /// <summary>
    /// Fetches this employee's Paid runs and entries for the year - the Paid/PayDate.Year/employee
    /// filter shared by <see cref="BuildAsync"/> and <see cref="BuildWithDraftEntryAsync"/>, kept
    /// in one place so it cannot silently drift between them. When
    /// <paramref name="draftRun"/>/<paramref name="draftEntry"/> are supplied
    /// (<see cref="BuildWithDraftEntryAsync"/>'s case), they are appended to what comes back -
    /// unless a run with the same <see cref="PayrollRun.Id"/> is already among the Paid runs, in
    /// which case nothing is added: that run's entry is already counted once, and adding it again
    /// would double every figure on the certificate.
    /// </summary>
    private async Task<(List<PayrollRun> Runs, List<PayrollRunEmployee> Entries)> LoadRunsAndEntriesAsync(
        Guid employeeId, int year, PayrollRun? draftRun, PayrollRunEmployee? draftEntry, CancellationToken ct)
    {
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

        if (draftRun is not null && draftEntry is not null && runs.All(r => r.Id != draftRun.Id))
        {
            runs.Add(draftRun);
            entries.Add(draftEntry);
        }

        return (runs, entries);
    }

    /// <summary>
    /// Builds the DTO from an employee's already-fetched runs and entries for the year - the
    /// aggregation shared by <see cref="BuildAsync"/> (one employee, its own queries) and
    /// <see cref="BuildAllAsync"/> (every employee, one shared query). Neither queries nor the
    /// "no entries" / "no company" checks belong here: both callers already did those before this
    /// point, for reasons specific to how each one fetches its data.
    /// </summary>
    private static Bir2316Dto BuildDto(
        Employee employee,
        Company company,
        IReadOnlyList<PayrollRun> runs,
        IReadOnlyList<PayrollRunEmployee> entries,
        int year,
        Bir2316ManualInputs manual)
    {
        // "13th month and other benefits" (NIRC Sec. 32(B)(7)(e)): the 13th month plus the leave
        // converted beyond de minimis, which RR 5-2011 (as amended by RR 11-2018) treats as other
        // benefits. Together they are exempt up to 90,000 a year (Item 34) and taxable past it
        // (Item 48). The excess leave goes to Item 48 rather than staying in 51B because Item 48
        // is the form's own box for exactly this - "13th month pay and other benefits" in excess
        // of the cap - and it is what the 1604-C alphalist reads as that column; 51B would file
        // the same money under "others" and misstate the alphalist.
        decimal thirteenthMonthAndOtherBenefits = entries.Sum(e => e.ThirteenthMonthAndOtherBenefits);
        decimal thirteenthMonthNonTaxable = Math.Min(thirteenthMonthAndOtherBenefits, StatutoryCaps.ThirteenthMonthExemption);
        decimal thirteenthMonthTaxable = Math.Max(0m, thirteenthMonthAndOtherBenefits - StatutoryCaps.ThirteenthMonthExemption);

        // Final-pay earnings - zero on every entry for an employee who has never had a final run.
        // FinalPayTaxable is the separation or retirement pay that isn't exempt; the leave beyond
        // de minimis is already in the 13th-month split above.
        decimal leaveConversionNonTaxable = entries.Sum(e => e.LeaveConversionNonTaxable);
        decimal finalPayNonTaxable = entries.Sum(e => e.FinalPayNonTaxable);
        decimal finalPayTaxable = entries.Sum(e => e.FinalPayTaxable);

        return new Bir2316Dto
        {
            EmployeeId = employee.Id,
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
            // IsMinimumWageEarner is intentionally not overlaid from manual: see the doc comment
            // on Bir2316Dto.IsMinimumWageEarner. It stays at its default (false) until Items
            // 29-32 are derived.
            StatutoryMinWagePerDay = manual.StatutoryMinWagePerDay,
            StatutoryMinWagePerMonth = manual.StatutoryMinWagePerMonth,

            // Part IV-B Section A — non-taxable.
            Item33_HazardPayMwe = manual.Item33_HazardPayMwe,
            Item34_ThirteenthMonthAndBenefits = thirteenthMonthNonTaxable,
            // LeaveConversionNonTaxable is the de minimis slice of final pay - the cash value of
            // convertible leave, up to the statutory de minimis ceiling, that PayrollComputationService
            // already excluded from the final run's withholding base. Item 35 is the form's de
            // minimis box, so it belongs there alongside whatever a human enters manually. The
            // leave beyond the ceiling is other benefits, in Item 34 (or 48) with the 13th month.
            Item35_DeMinimis = manual.Item35_DeMinimis + leaveConversionNonTaxable,
            Item36_SssPhicPagibigContributions =
                entries.Sum(e => e.SSSEmployee + e.PhilHealthEmployee + e.PagIbigEmployee),
            // NonTaxableAllowances is non-taxable compensation that is not de minimis, not a
            // contribution and not 13th-month pay - Item 37 ("Salaries and Other Forms of
            // Compensation") is Section A's catch-all for exactly that: everything non-taxable
            // that lacks its own numbered box. Putting it here, rather than skipping it, is what
            // keeps Item 19 (gross compensation) from understating what the employee was actually
            // paid. The non-de-minimis part of final pay's non-taxable amount - separation or
            // retirement pay that qualifies for exemption - is the same kind of catch-all
            // non-taxable compensation, so it lands here too (FinalPayNonTaxable minus the de
            // minimis slice already claimed by Item 35, so nothing is counted twice).
            Item37_SalariesOtherForms = entries.Sum(e => e.NonTaxableAllowances) +
                                         (finalPayNonTaxable - leaveConversionNonTaxable),

            // Part IV-B Section B and the supplementary block — taxable.
            //
            // PayrollComputationService's withholding base is
            // regularPay + overtimePay + holidayPay + nightDiffPay + taxableAllowances +
            // finalPayTaxable (the last only nonzero on a final-pay run - see
            // PayrollRunEmployee.FinalPayTaxable), less the employee's SSS, PhilHealth and
            // Pag-IBIG contributions; the 13th month and other benefits past the 90,000 are taxed
            // on top of it (Item 48). Every one of those components has to land in a taxable box
            // here or Item 21/23 (the certificate's taxable compensation) understates what tax was
            // actually withheld against - silently manufacturing a false "tax due < tax withheld"
            // result for any employee who worked a holiday, drew night differential, received a
            // taxable allowance, or was paid taxable separation or retirement pay.
            //
            // The contributions come off Item 39. They are withheld from the basic salary, and
            // Section A already reports them as non-taxable in Item 36; left inside Item 39 as
            // well, Item 19 (non-taxable + taxable) would count them twice and Item 21/23 - and
            // with it Item 24's tax due - would tax income the engine never withheld on. Taxable
            // basic salary on the form is therefore net of the employee's mandatory contributions,
            // exactly as the engine's base is. (They come off the basic even in a period whose
            // contributions exceed it; the engine nets them against the whole taxable gross, so
            // Item 52 still sums to the engine's base either way.)
            //
            // Item 50 carries OvertimePay; HolidayPay, NightDiffPay and TaxableAllowances have no
            // dedicated taxable box on the form (their only numbered boxes - Items 30-32 - are the
            // Section A exemption for minimum-wage earners), so they go into the "Others
            // (specify)" boxes Section B provides for exactly this: compensation that is real and
            // taxable but has no line of its own. Holiday pay and night differential are placed in
            // 44A/44B (alongside the regular per-period allowances 40-43); taxable allowances go
            // in 51A and final pay's taxable separation or retirement pay goes in 51B (alongside
            // the other supplemental, ad hoc pay in 45-49) since a fixed "allowance" bucket does
            // not fit Section B's first group of named, per-period pay items as cleanly as it does
            // the supplemental group. All boxes are summed identically into Item 52, so this is a
            // labeling choice, not a computation one. Item51B is left at its default (0, no label)
            // when there is no taxable separation or retirement pay to report, so the "Others
            // (specify)" box does not appear on an ordinary certificate.
            Item39_BasicSalary = entries.Sum(e => e.RegularPay - e.SSSEmployee - e.PhilHealthEmployee - e.PagIbigEmployee),
            Item44A_OtherAmount = entries.Sum(e => e.HolidayPay),
            Item44A_OtherLabel = "Holiday Pay",
            Item44B_OtherAmount = entries.Sum(e => e.NightDiffPay),
            Item44B_OtherLabel = "Night Shift Differential",
            Item48_TaxableThirteenthMonth = thirteenthMonthTaxable,
            Item50_OvertimePay = entries.Sum(e => e.OvertimePay),
            Item51A_OtherAmount = entries.Sum(e => e.TaxableAllowances),
            Item51A_OtherLabel = "Taxable Allowances",
            Item51B_OtherAmount = finalPayTaxable,
            // Kept short deliberately: the printed form's "Others (specify)" box is 130.5pt wide,
            // and a longer label (the original "Final pay (leave conversion,
            // separation/retirement pay)") overflowed it and printed truncated with an ellipsis.
            // Leave conversion no longer lands here (Items 34/48 and 35 take it).
            Item51B_OtherLabel = finalPayTaxable != 0m ? "Final pay - separation/retirement pay" : "",

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
