using PeopleCore.Domain.Payroll;

namespace PeopleCore.Application.Payroll.DTOs;

/// <summary>
/// BIR Form 2316 — Certificate of Compensation Payment / Tax Withheld.
/// <para>
/// Every <c>ItemNN_</c> name is the numbered box it prints into on the government form. They are
/// deliberately not renamed to read better in C#: the number IS the link between this record and
/// the form, and severing it would make the document layout unverifiable against the source.
/// </para>
/// <para>
/// The properties are settable rather than <c>init</c>-only because the Blazor preview page binds
/// the manual half of the form two-way. That mutability is not an input channel: nothing that
/// reaches this type from a request body is ever trusted — see <see cref="Bir2316ManualInputs"/>,
/// which is the only shape a caller may submit.
/// </para>
/// </summary>
public record Bir2316Dto
{
    // Header
    public int Year { get; set; }
    public string PeriodFrom { get; set; } = "";
    public string PeriodTo { get; set; } = "";

    // Part I — Employee Info
    public string EmployeeTin { get; set; } = "";
    public string EmployeeLastName { get; set; } = "";
    public string EmployeeFirstName { get; set; } = "";
    public string EmployeeMiddleName { get; set; } = "";
    public string RdoCode { get; set; } = "";
    public string RegisteredAddress { get; set; } = "";
    public string RegisteredZipCode { get; set; } = "";
    public string LocalHomeAddress { get; set; } = "";
    public string LocalZipCode { get; set; } = "";
    public string ForeignAddress { get; set; } = "";
    public string DateOfBirth { get; set; } = "";
    public string ContactNumber { get; set; } = "";
    public decimal StatutoryMinWagePerDay { get; set; }
    public decimal StatutoryMinWagePerMonth { get; set; }

    /// <summary>
    /// NOT YET DERIVED. Nothing populates Items 29-32 (the MWE non-taxable boxes for basic,
    /// holiday, overtime and night-differential pay), so setting this flag without also
    /// apportioning those boxes produces a certificate that declares the employee a minimum-wage
    /// earner while every taxable box (39/44A/44B/48/50/51A) still carries their full income and
    /// tax is still computed on it - an invalid, self-contradictory form. There is deliberately no
    /// UI control and no <c>Bir2316ManualInputs</c> field for this flag right now (see
    /// <c>Bir2316.razor</c> and <c>Bir2316ManualInputs</c>): offering the checkbox before Items
    /// 29-32 are implemented would let a caller produce that invalid certificate. Deciding when an
    /// employee qualifies as an MWE and how their pay is apportioned across 29-32 is a tax
    /// question that needs its own design before this flag is reintroduced.
    /// </summary>
    public bool IsMinimumWageEarner { get; set; }

    // Part II — Employer Info (Present)
    public string EmployerTin { get; set; } = "";
    public string EmployerName { get; set; } = "";
    public string EmployerAddress { get; set; } = "";
    public string EmployerZipCode { get; set; } = "";
    /// <summary>
    /// The employer's Revenue District Office code. Not in the PayZen source and not a numbered
    /// box on the form, but <c>Company.RdoCode</c> is stored precisely so it is not re-typed for
    /// every employee each January; carrying it here is what makes the stored value reachable.
    /// </summary>
    public string EmployerRdoCode { get; set; } = "";
    public bool IsMainEmployer { get; set; } = true;

    // Part III — Employer Info (Previous)
    public string PrevEmployerTin { get; set; } = "";
    public string PrevEmployerName { get; set; } = "";
    public string PrevEmployerAddress { get; set; } = "";
    public string PrevEmployerZipCode { get; set; } = "";

    // Part IV-B Section A — Non-Taxable/Exempt
    public decimal Item29_NonTaxableBasicSalary { get; set; }
    public decimal Item30_HolidayPayMwe { get; set; }
    public decimal Item31_OvertimePayMwe { get; set; }
    public decimal Item32_NightShiftDiffMwe { get; set; }
    public decimal Item33_HazardPayMwe { get; set; }
    public decimal Item34_ThirteenthMonthAndBenefits { get; set; }
    public decimal Item35_DeMinimis { get; set; }
    public decimal Item36_SssPhicPagibigContributions { get; set; }
    public decimal Item37_SalariesOtherForms { get; set; }

    // Part IV-B Section B — Taxable Regular
    public decimal Item39_BasicSalary { get; set; }
    public decimal Item40_Representation { get; set; }
    public decimal Item41_Transportation { get; set; }
    public decimal Item42_Cola { get; set; }
    public decimal Item43_FixedHousing { get; set; }
    public decimal Item44A_OtherAmount { get; set; }
    public string Item44A_OtherLabel { get; set; } = "";
    public decimal Item44B_OtherAmount { get; set; }
    public string Item44B_OtherLabel { get; set; } = "";

    // Supplementary
    public decimal Item45_Commission { get; set; }
    public decimal Item46_ProfitSharing { get; set; }
    public decimal Item47_Fees { get; set; }
    public decimal Item48_TaxableThirteenthMonth { get; set; }
    public decimal Item49_HazardPay { get; set; }
    public decimal Item50_OvertimePay { get; set; }
    public decimal Item51A_OtherAmount { get; set; }
    public string Item51A_OtherLabel { get; set; } = "";
    public decimal Item51B_OtherAmount { get; set; }
    public string Item51B_OtherLabel { get; set; } = "";

    // Part IVA — Summary inputs
    public decimal Item22_PrevTaxableCompensation { get; set; }
    public decimal Item25B_PrevTaxWithheld { get; set; }
    public decimal Item25A_PresentTaxWithheld { get; set; }
    public decimal Item27_PeraTaxCredit { get; set; }

    // Computed
    public decimal Item38_TotalNonTaxable =>
        Item29_NonTaxableBasicSalary + Item30_HolidayPayMwe + Item31_OvertimePayMwe +
        Item32_NightShiftDiffMwe + Item33_HazardPayMwe + Item34_ThirteenthMonthAndBenefits +
        Item35_DeMinimis + Item36_SssPhicPagibigContributions + Item37_SalariesOtherForms;

    public decimal Item52_TotalTaxableCompensation =>
        Item39_BasicSalary + Item40_Representation + Item41_Transportation +
        Item42_Cola + Item43_FixedHousing + Item44A_OtherAmount + Item44B_OtherAmount +
        Item45_Commission + Item46_ProfitSharing + Item47_Fees +
        Item48_TaxableThirteenthMonth + Item49_HazardPay + Item50_OvertimePay +
        Item51A_OtherAmount + Item51B_OtherAmount;

    public decimal Item19_GrossCompensation => Item38_TotalNonTaxable + Item52_TotalTaxableCompensation;
    public decimal Item20_LessNonTaxable => Item38_TotalNonTaxable;
    public decimal Item21_TaxableFromPresent => Item52_TotalTaxableCompensation;
    public decimal Item23_GrossTaxable => Item21_TaxableFromPresent + Item22_PrevTaxableCompensation;

    /// <summary>
    /// Item 24 (Tax Due): the annual liability computed on Item 23. This is NOT the amount
    /// withheld -- Item 26 is. They coincide only when withholding was correct, which is the
    /// condition the employee attests to under substituted filing (Item 56), so deriving one
    /// from the other would assert that condition instead of testing it.
    /// </summary>
    public decimal Item24_TaxDue => BirWithholdingTax.ComputeAnnualTaxDue(Item23_GrossTaxable);

    public decimal Item26_TotalTaxWithheld => Item25A_PresentTaxWithheld + Item25B_PrevTaxWithheld;
    public decimal Item28_TotalTaxes => Item26_TotalTaxWithheld + Item27_PeraTaxCredit;
}

/// <summary>
/// The Form 2316 fields a person provides rather than payroll deriving. Everything else is
/// recomputed server-side at generation: a tax certificate's money comes from the payroll
/// records, never from the request body.
/// <para>
/// The type is the enforcement. There is no field here capable of carrying a derived figure —
/// no basic salary, no overtime, no present-employer tax withheld — so there is no request body
/// a caller can craft that states one. Adding such a field would reopen the hole this record
/// exists to close; <c>Bir2316ServiceTests.BuildAsync_IgnoresDerivedFiguresSuppliedByTheCaller</c>
/// pins the field list by name so that it cannot happen quietly.
/// </para>
/// </summary>
public record Bir2316ManualInputs
{
    public string? PrevEmployerTin { get; init; }
    public string? PrevEmployerName { get; init; }
    public string? PrevEmployerAddress { get; init; }
    public string? PrevEmployerZipCode { get; init; }
    public decimal Item22_PrevTaxableCompensation { get; init; }
    public decimal Item25B_PrevTaxWithheld { get; init; }

    /// <summary>
    /// Item 27, the PERA tax credit. Not in the brief's list, and added after checking the ported
    /// DTO for fields the aggregation cannot derive: a Personal Equity and Retirement Account
    /// contribution happens outside payroll, so nothing in a payroll run knows about it. It sits
    /// here for the same reason <see cref="Item25B_PrevTaxWithheld"/> does — a tax credit an
    /// accountant supplies, not a figure this employer paid — and leaving it out would make the
    /// box permanently zero with no way to fill it.
    /// </summary>
    public decimal Item27_PeraTaxCredit { get; init; }

    public decimal Item35_DeMinimis { get; init; }
    public decimal Item33_HazardPayMwe { get; init; }

    // IsMinimumWageEarner is deliberately NOT a field here. Ticking it without also deriving
    // Items 29-32 (the MWE non-taxable boxes) produces a certificate that declares the employee a
    // minimum-wage earner while still taxing all of their income - see the doc comment on
    // Bir2316Dto.IsMinimumWageEarner. Do not add it back until Items 29-32 are populated.
    public decimal StatutoryMinWagePerDay { get; init; }
    public decimal StatutoryMinWagePerMonth { get; init; }
}
