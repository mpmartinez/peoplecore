namespace PeopleCore.Domain.Entities.Payroll;

public class PayrollRunEmployee : AuditableEntity
{
    public Guid PayrollRunId { get; set; }
    public PayrollRun PayrollRun { get; set; } = null!;
    public Guid EmployeeId { get; set; }
    /// <summary>
    /// Populated by EF fixup when a run is loaded with .Include(...).ThenInclude(e => e.Employee)
    /// (see IPayrollRunRepository.GetWithEntriesAsync) - never set by
    /// PayrollComputationService.Compute, which works from the employee's compensation record,
    /// not the person. Null on an entry that has not been reloaded from the database.
    /// </summary>
    public Employees.Employee? Employee { get; set; }

    // Work details
    public decimal DaysWorked { get; set; } = 22m;
    public decimal OvertimeHours { get; set; } = 0m;
    public decimal HolidayDays { get; set; } = 0m;
    public bool IncludeThirteenthMonth { get; set; } = false;

    /// <summary>
    /// The attendance totals this entry was computed from, snapshotted so a recompute cannot
    /// change what an employee was paid when a punch is edited later. OvertimeHours and
    /// HolidayDays above are the computed roll-ups; these are the inputs, and RestDayOTHours
    /// is the part of OvertimeHours that attracts the rest-day rate.
    /// </summary>
    public decimal AbsenceDays { get; set; }
    public decimal LateMinutes { get; set; }
    public decimal UndertimeMinutes { get; set; }
    public decimal NightDiffHours { get; set; }
    public decimal RestDayOTHours { get; set; }
    public decimal HolidayRegularDays { get; set; }
    public decimal HolidaySpecialDays { get; set; }

    /// <summary>
    /// The attendance above broken down by the kind of day it fell on - what the premiums are
    /// actually priced from. The seven totals above are kept as roll-ups for display; an entry
    /// saved before this breakdown existed has none, and is repriced from those totals instead.
    /// </summary>
    public List<PayrollRunPremiumDay> PremiumDays { get; set; } = [];

    // Earnings
    /// <summary>Pay for ordinary time, already net of absences and tardiness.</summary>
    public decimal RegularPay { get; set; }
    public decimal OvertimePay { get; set; }
    /// <summary>Premium for work on holidays, special days and rest days, above what the salary pays.</summary>
    public decimal HolidayPay { get; set; }
    /// <summary>The 10% night shift premium on hours worked between 10 p.m. and 6 a.m.</summary>
    public decimal NightDiffPay { get; set; }
    public decimal TaxableAllowances { get; set; }
    public decimal NonTaxableAllowances { get; set; }
    public decimal ThirteenthMonth { get; set; }

    /// <summary>
    /// The 13th month already paid in the pay year (by the year's other Paid runs) when this
    /// entry's <see cref="ThirteenthMonth"/> was computed - the figure it was netted of. Mark Paid
    /// compares it with what those runs total now, so a 13th month paid elsewhere since the entry
    /// was computed can't be paid a second time. Null when the entry computed no 13th month
    /// (not included, or the employee isn't eligible), and on entries computed before it was kept.
    /// </summary>
    public decimal? ThirteenthMonthPaidEarlierInYear { get; set; }

    /// <summary>
    /// The basic salary earned earlier in the pay year (the year's other Paid runs plus the
    /// employee's opening balance) when this entry's <see cref="ThirteenthMonth"/> was computed - the
    /// figure its 13th month due was worked out from. Mark Paid compares it with the figure now, so
    /// an opening balance edited, added or removed since can't change what the 13th month should be
    /// unnoticed. Null as <see cref="ThirteenthMonthPaidEarlierInYear"/> is.
    /// </summary>
    public decimal? BasicEarnedEarlierInYear { get; set; }

    /// <summary>
    /// How much of the 90,000 exemption for 13th month and other benefits was used earlier in the
    /// pay year (on the year's other Paid runs and the opening balance) when this entry's
    /// <see cref="ThirteenthMonth"/> was computed - what its tax was worked out with. Mark Paid
    /// compares it with the figure now, as <see cref="BasicEarnedEarlierInYear"/>. Null as
    /// <see cref="ThirteenthMonthPaidEarlierInYear"/> is.
    /// </summary>
    public decimal? ExemptUsedEarlierInYear { get; set; }

    /// <summary>
    /// The part of <see cref="ThirteenthMonth"/> above what was left of the 90,000 exemption for 13th
    /// month and other benefits when the entry was computed: <c>max(0, ThirteenthMonth - max(0,
    /// 90,000 - exemption used earlier in the year))</c>. The 13th month fills the exemption first; the
    /// leave beyond de minimis takes what remains after it. A record of the split for the payslip
    /// only - not part of <see cref="GrossPay"/>, taxable pay or any tax formula. 0 when nothing is
    /// taxable; null on entries computed before it was stored.
    /// </summary>
    public decimal? ThirteenthMonthTaxable { get; set; }

    /// <summary>
    /// The part of <see cref="ThirteenthMonth"/> within the exemption: the 13th month less
    /// <see cref="ThirteenthMonthTaxable"/> (all of it when that is null).
    /// </summary>
    public decimal ThirteenthMonthExempt => ThirteenthMonth - (ThirteenthMonthTaxable ?? 0m);

    // Final-pay earnings. A Regular run's entries carry leave conversion too when the run
    // includes the year-end conversion; separation and retirement pay are a final pay's only.
    /// <summary>
    /// Cash value of convertible leave balances: its de minimis part
    /// (<see cref="LeaveConversionNonTaxable"/>) plus the rest (<see cref="LeaveConversionOtherBenefits"/>).
    /// </summary>
    public decimal LeaveConversionPay { get; set; }
    /// <summary>
    /// The de minimis (non-taxable) part of <see cref="LeaveConversionPay"/>: vacation-type days up
    /// to the 10 a tax year allows, less those earlier conversions in the pay year used.
    /// </summary>
    public decimal LeaveConversionNonTaxable { get; set; }
    public decimal SeparationPay { get; set; }
    public decimal RetirementPay { get; set; }
    /// <summary>
    /// The part of leave conversion, separation pay and retirement pay that is non-taxable
    /// outright - <see cref="LeaveConversionNonTaxable"/> plus whatever of separation/retirement
    /// pay is exempt. The leave beyond de minimis isn't in it: that is other benefits
    /// (<see cref="LeaveConversionOtherBenefits"/>), exempt or taxable only by the year's total.
    /// </summary>
    public decimal FinalPayNonTaxable { get; set; }

    // Maternity pay (RA 11210).
    /// <summary>Input: this entry advances the employee's SSS maternity benefit (their Draft claim).</summary>
    public bool AdvanceMaternityBenefit { get; set; }
    /// <summary>The SSS maternity benefit advanced on this entry: tax-free, and in gross pay only.</summary>
    public decimal MaternityBenefitAdvance { get; set; }
    /// <summary>The part of regular pay SSS covers during maternity leave, already netted out of <see cref="RegularPay"/>.</summary>
    public decimal MaternityBenefitOffset { get; set; }
    /// <summary>
    /// The salary differential: the pay for the maternity days that <see cref="MaternityBenefitOffset"/>
    /// leaves. It is still in <see cref="RegularPay"/> (and so in the 13th-month basis and gross), but
    /// it is part of the maternity benefit and exempt from tax and withholding (RMC 105-2019): the
    /// 2316 reports it in Item 37, not the taxable basic, and the 1601-C as other non-taxable.
    /// </summary>
    public decimal MaternityDifferential { get; set; }

    /// <summary>
    /// The part of this entry's employee shares (SSS, PhilHealth, Pag-IBIG) not collected here: a
    /// period SSS maternity covers can have too little pay for them. The shares stay in full in
    /// their own fields - the employer remits them - and this part is collected on a later entry.
    /// Only an entry with a <see cref="MaternityBenefitOffset"/> defers.
    /// </summary>
    public decimal ContributionsDeferred { get; set; }

    /// <summary>
    /// Shares deferred on the employee's earlier Paid entries that this entry collects, out of the
    /// cash it has left after its own deductions (the maternity advance counts for this). Not one of
    /// <see cref="TotalDeductions"/>: it comes off <see cref="NetPay"/> on its own.
    /// </summary>
    public decimal DeferredContributionsCollected { get; set; }
    /// <summary>
    /// The claim whose benefit <see cref="MaternityBenefitAdvance"/> advances, when there is one. It
    /// is how a claim is advanced on one payroll only, and which claim Mark Paid settles.
    /// </summary>
    public Guid? MaternityClaimId { get; set; }

    /// <summary>
    /// Everything this entry pays the employee. It includes <see cref="MaternityBenefitAdvance"/>,
    /// which is the SSS benefit rather than compensation: the government reports that start from
    /// gross (the 1601-C) take it back out, and the 2316 never sums it.
    /// </summary>
    public decimal GrossPay =>
        RegularPay + OvertimePay + HolidayPay + NightDiffPay + TaxableAllowances + NonTaxableAllowances
        + ThirteenthMonth + LeaveConversionPay + SeparationPay + RetirementPay + MaternityBenefitAdvance;

    /// <summary>
    /// The final-pay earnings taxable outright: separation or retirement pay that isn't exempt.
    /// The leave beyond de minimis isn't here - it is other benefits, taxed only past the 90,000
    /// exemption it shares with the 13th month (see <see cref="ThirteenthMonthAndOtherBenefits"/>).
    /// </summary>
    public decimal FinalPayTaxable => SeparationPay + RetirementPay - (FinalPayNonTaxable - LeaveConversionNonTaxable);

    /// <summary>
    /// The leave paid out beyond its de minimis part. De minimis in excess of its ceiling is
    /// "other benefits" (RR 5-2011, as amended by RR 11-2018), so it counts toward the 90,000
    /// exemption together with the 13th month.
    /// </summary>
    public decimal LeaveConversionOtherBenefits => LeaveConversionPay - LeaveConversionNonTaxable;

    /// <summary>
    /// What this entry pays toward the year's "13th month and other benefits", exempt up to
    /// <c>StatutoryCaps.ThirteenthMonthExemption</c> a year and taxable past it: the 13th month
    /// plus <see cref="LeaveConversionOtherBenefits"/>. Which part is exempt depends on the year's
    /// other entries, so it's split where the year is summed - the 2316 (Items 34 and 48) and the
    /// 1601-C. Only the payslip's split of the 13th month, as at compute, is stored here
    /// (<see cref="ThirteenthMonthTaxable"/>).
    /// </summary>
    public decimal ThirteenthMonthAndOtherBenefits => ThirteenthMonth + LeaveConversionOtherBenefits;

    // Rate basis and attendance adjustments, stored so a payslip cannot drift from a later
    // settings change. Both deductions below are ALREADY netted out of RegularPay - they are
    // recorded for the payslip, and must not be added to TotalDeductions again.
    /// <summary>Applicable daily rate used for deductions and premiums.</summary>
    public decimal DailyRate { get; set; }
    /// <summary>The DOLE equivalent-monthly-rate factor this run used (365 / 313 / 261 / other).</summary>
    public decimal DailyRateFactor { get; set; }
    public decimal AbsenceDeduction { get; set; }
    public decimal TardinessDeduction { get; set; }

    // Mandatory deductions
    public decimal SSSEmployee { get; set; }
    public decimal SSSEmployer { get; set; }
    public decimal PhilHealthEmployee { get; set; }
    public decimal PhilHealthEmployer { get; set; }
    public decimal PagIbigEmployee { get; set; }
    public decimal PagIbigEmployer { get; set; }
    public decimal WithholdingTax { get; set; }

    // Other deductions
    /// <summary>Aggregate of <see cref="LoanDeductionLines"/>, kept for payslips and reporting.</summary>
    public decimal LoanDeductions { get; set; }
    public decimal OtherDeductions { get; set; }

    /// <summary>
    /// What was actually deducted per loan in this period. Marking the run paid retires the
    /// balances from these rows rather than re-deriving the instalment, so the amount taken
    /// off a loan is always exactly the amount taken off the employee's pay.
    /// </summary>
    public List<PayrollLoanDeduction> LoanDeductionLines { get; set; } = [];

    public decimal TotalDeductions =>
        SSSEmployee + PhilHealthEmployee + PagIbigEmployee + WithholdingTax + LoanDeductions + OtherDeductions;
    /// <summary>
    /// What the employee takes home: gross less this entry's deductions, with the shares it
    /// deferred left in her pay and the earlier deferred shares it collects taken out.
    /// </summary>
    public decimal NetPay => GrossPay - TotalDeductions + ContributionsDeferred - DeferredContributionsCollected;

    /// <summary>
    /// What the entry costs the employer. The maternity advance is left out: SSS reimburses it, so
    /// it is cash the employer fronts, not a cost.
    /// </summary>
    public decimal TotalEmployerCost =>
        GrossPay - MaternityBenefitAdvance + SSSEmployer + PhilHealthEmployer + PagIbigEmployer;
}
