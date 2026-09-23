using PeopleCore.Application.Payroll.DTOs;
using PeopleCore.Application.Payroll.FinalPay;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Payroll;

namespace PeopleCore.Application.Payroll.Services;

/// <summary>
/// Philippine payroll computation service.
/// Implements the SSS January 2025 schedule (Circular 2024-006), PhilHealth 5% (2024), Pag-IBIG, and BIR TRAIN law withholding tax.
/// </summary>
public class PayrollComputationService
{
    /// <param name="asOf">
    /// The date the contribution applies to - normally the payroll run's period start. When
    /// null the newest registered schedule is used, which is only correct for current periods.
    /// </param>
    // Premium rates come from PeopleCore.Domain.Payroll.DolePremiumRates, which expresses the
    // handbook's forty published rates as the one rule that generates them. Rates are used
    // unrounded and only the resulting money is rounded.

    /// <summary>Default DOLE equivalent-monthly-rate factor (Handbook 2024, section E).</summary>
    private const decimal DefaultDailyRateFactor = 365m;

    public (decimal Employee, decimal Employer) ComputeSSS(decimal monthlySalary, ContributionRates? rates = null,
        DateOnly? asOf = null)
    {
        if (rates?.SSSEmployeeRate is decimal eRate && rates?.SSSEmployerRate is decimal rRate)
            return (Math.Round(monthlySalary * eRate, 2), Math.Round(monthlySalary * rRate, 2));

        var schedule = asOf is DateOnly date
            ? SssContributionSchedule.ForPeriod(date)
            : SssContributionSchedule.Schedules[0];

        foreach (var (maxSalary, emp, emr) in schedule.Rows)
        {
            if (monthlySalary <= maxSalary)
                return (emp, emr);
        }

        var top = schedule.Rows[^1];
        return (top.EmployeeContrib, top.EmployerContrib);
    }

    /// <summary>
    /// Treats a non-positive configured value as "not configured". Company settings rows that
    /// predate the contribution-rate columns were backfilled with 0, and those zeros are passed
    /// straight through by the payroll path - a plain ?? would accept them and silently zero the
    /// contribution. No statutory rate, floor or ceiling is ever legitimately zero.
    /// </summary>
    private static decimal? Configured(decimal? value) => value is > 0m ? value : null;

    public (decimal Employee, decimal Employer) ComputePhilHealth(decimal monthlySalary, ContributionRates? rates = null)
    {
        // PhilHealth Advisory 2025-0002: 5.0% of Monthly Basic Salary, income floor 10,000 and
        // ceiling 100,000, shared equally - so each share runs 250.00 to 2,500.00.
        decimal rate = Configured(rates?.PhilHealthRate) ?? 0.05m;
        decimal minShare = Configured(rates?.PhilHealthMinShare) ?? 250m;
        decimal maxShare = Configured(rates?.PhilHealthMaxShare) ?? 2_500m;
        decimal premium = Math.Round(monthlySalary * rate / 2m, 2);
        premium = Math.Max(premium, minShare);
        premium = Math.Min(premium, maxShare);
        return (premium, premium);
    }

    public (decimal Employee, decimal Employer) ComputePagIbig(decimal monthlySalary, ContributionRates? rates = null)
    {
        // Pag-IBIG Fund Circular No. 460 (15 Jan 2024, effective February 2024), Section C:
        //   fund salary 1,500 and below -> employee 1.0%, employer 2.0%
        //   fund salary over 1,500      -> employee 2.0%, employer 2.0%
        // The Maximum Fund Salary rose from 5,000 to 10,000, capping each share at 200.00.
        // Note the lower tier applies to the EMPLOYEE share only; the employer pays 2% on both.
        decimal empRate   = Configured(rates?.PagIbigEmployeeRate) ?? 0.02m;
        decimal lowRate   = Configured(rates?.PagIbigLowEmployeeRate) ?? 0.01m;
        decimal threshold = Configured(rates?.PagIbigLowRateThreshold) ?? 1_500m;
        decimal emrRate   = Configured(rates?.PagIbigEmployerRate) ?? 0.02m;
        decimal maxFundSalary = Configured(rates?.PagIbigMaxFundSalary) ?? 10_000m;

        decimal fundSalary = Math.Min(monthlySalary, maxFundSalary);
        decimal employeeRate = fundSalary <= threshold ? lowRate : empRate;

        return (Math.Round(fundSalary * employeeRate, 2), Math.Round(fundSalary * emrRate, 2));
    }

    /// <summary>
    /// Compute BIR withholding tax for one payroll period.
    /// periodTaxableIncome = the period's gross taxable (after mandatory deductions).
    /// Returns the withholding tax for that period.
    /// </summary>
    public decimal ComputeWithholdingTax(decimal periodTaxableIncome, PayFrequency frequency)
    {
        // Annualize over the number of periods in a year (24 semi-monthly, 12 monthly), tax it
        // on the shared annual bracket table, then de-annualize. BIR Form 2316 Item 24 reads
        // the same table.
        decimal periodsPerYear = frequency == PayFrequency.SemiMonthly ? 24m : 12m;
        decimal annualTax = BirWithholdingTax.ComputeAnnualTax(periodTaxableIncome * periodsPerYear);

        return Math.Round(annualTax / periodsPerYear, 2);
    }

    /// <summary>
    /// Tax withheld on the part of a 13th month payment above the 90,000 exemption (NIRC
    /// Sec. 32(B)(7)(e)), after the 13th month already paid earlier in the year has used up
    /// its share of that exemption.
    /// <para>
    /// The taxable excess is a one-off, so it is not annualized with the period's pay - that
    /// would tax it as if it recurred every payslip. It is taxed at the margin instead: the
    /// annual tax on the year's regular pay plus the excess, less the annual tax on the regular
    /// pay alone, all withheld on this payslip.
    /// </para>
    /// </summary>
    public decimal ComputeThirteenthMonthTax(decimal periodTaxableIncome, PayFrequency frequency,
        decimal thirteenthMonth, decimal thirteenthMonthPaidEarlierInYear)
    {
        decimal exemptionLeft = Math.Max(0m,
            StatutoryCaps.ThirteenthMonthExemption - thirteenthMonthPaidEarlierInYear);
        decimal taxableExcess = Math.Max(0m, thirteenthMonth - exemptionLeft);
        if (taxableExcess == 0m)
            return 0m;

        decimal periodsPerYear = frequency == PayFrequency.SemiMonthly ? 24m : 12m;
        decimal annualRegular = Math.Max(0m, periodTaxableIncome * periodsPerYear);
        decimal marginalTax = BirWithholdingTax.ComputeAnnualTax(annualRegular + taxableExcess)
                            - BirWithholdingTax.ComputeAnnualTax(annualRegular);

        return Math.Round(marginalTax, 2);
    }

    /// <summary>
    /// The applicable daily rate <see cref="Compute"/> prices a day at: monthly x 12 / factor,
    /// rounded to centavos, with a missing or non-positive factor meaning the default 365. Public
    /// so final pay prices leave conversion and retirement pay at exactly the same rate.
    /// </summary>
    public static decimal DailyRateFor(decimal monthlyBasic, decimal? dailyRateFactor)
    {
        decimal factor = dailyRateFactor is > 0m ? dailyRateFactor.Value : DefaultDailyRateFactor;
        return Math.Round(monthlyBasic * 12m / factor, 2);
    }

    /// <summary>
    /// Whether the salary pays rest days under <paramref name="dailyRateFactor"/> (a missing or
    /// non-positive factor meaning the default 365). The 365 factor counts every day of the year,
    /// rest days included; 313 and 261 leave rest days out, so a rest day is unpaid.
    /// </summary>
    public static bool PaysRestDays(decimal? dailyRateFactor)
        => (dailyRateFactor is > 0m ? dailyRateFactor.Value : DefaultDailyRateFactor) >= 365m;

    /// <summary>
    /// Full computation for one employee in one payroll run.
    /// </summary>
    /// <param name="attendance">
    /// The employee's record for the run's attendance period, or null when the run was computed
    /// without attendance - in which case the employee is treated as fully present with no
    /// premiums, and <paramref name="overtimeHours"/> / <paramref name="holidayDays"/> still apply.
    /// </param>
    /// <param name="dailyRateFactor">
    /// DOLE equivalent-monthly-rate factor; defaults to 365. See <see cref="DefaultDailyRateFactor"/>.
    /// </param>
    /// <param name="thirteenthMonthPaidEarlierInYear">
    /// 13th month the employee was already paid in earlier paid runs of this run's pay year,
    /// which comes off the 13th month due and counts against the 90,000 exemption first.
    /// </param>
    /// <param name="basicEarnedEarlierInYear">
    /// Regular pay from earlier paid runs of this run's pay year - the basic salary the 13th
    /// month is one twelfth of, together with this period's.
    /// </param>
    /// <param name="isThirteenthMonthEligible">
    /// False for an employee HR has marked as not entitled; no 13th month is paid even when the
    /// run includes it.
    /// </param>
    public PayrollRunEmployee Compute(EmployeeCompensation compensation, PayrollRun run, decimal daysWorked = 0,
        decimal overtimeHours = 0, decimal holidayDays = 0, bool includeThirteenthMonth = false,
        ContributionRates? rates = null, PayrollAttendanceInput? attendance = null,
        decimal? dailyRateFactor = null, decimal thirteenthMonthPaidEarlierInYear = 0m,
        decimal basicEarnedEarlierInYear = 0m, bool isThirteenthMonthEligible = true,
        FinalPayExtras? finalPay = null)
    {
        bool isSemiMonthly = compensation.PayFrequency == PayFrequency.SemiMonthly;
        decimal periodsPerMonth = isSemiMonthly ? 2m : 1m;

        // Rate basis. DOLE Handbook 2024 section E gives the equivalent monthly rate as
        // daily x factor / 12; inverted, the applicable daily rate is monthly x 12 / factor.
        // The handbook calls these formulas suggestions "without prejudice to existing company
        // policies", so the factor is configurable per company.
        decimal factor = dailyRateFactor is > 0m ? dailyRateFactor.Value : DefaultDailyRateFactor;
        decimal dailyRate = DailyRateFor(compensation.BasicSalary, factor);
        decimal hourlyRate = Math.Round(dailyRate / 8m, 2);

        // A full period pays the full salary slice, and attendance adjusts it from there.
        // Rebuilding gross from the daily rate would stop a full month reconciling to the
        // monthly salary, because the factor counts unworked rest days and holidays as paid.
        // A final-pay run instead covers a short, partial period, so it is priced day by day
        // at the daily rate rather than as a share of the full monthly salary.
        decimal basePeriodPay = finalPay is not null
            ? Math.Round(dailyRate * finalPay.WorkingDays, 2)
            : Math.Round(compensation.BasicSalary / periodsPerMonth, 2);

        decimal absenceDeduction = Math.Round(dailyRate * (attendance?.AbsenceDays ?? 0m), 2);
        decimal lostMinutes = (attendance?.LateMinutes ?? 0m) + (attendance?.UndertimeMinutes ?? 0m);
        decimal tardinessDeduction = Math.Round(hourlyRate * lostMinutes / 60m, 2);
        decimal regularPay = Math.Max(0m, basePeriodPay - absenceDeduction - tardinessDeduction);

        // Premiums, priced per kind of day from DolePremiumRates. Without attendance the caller's
        // overtime and holiday figures stand for ordinary overtime and regular holiday days.
        var premiumDays = (attendance ?? new PayrollAttendanceInput
        {
            OvertimeHours = overtimeHours, HolidayRegularDays = holidayDays
        }).ResolvePremiumDays();

        decimal overtimePay = 0m, holidayPay = 0m, nightDiffPay = 0m;
        foreach (var day in premiumDays)
        {
            // Work on these days adds only what the salary does not already pay. The salary
            // pays 100% of every working day, holiday and special day - a worked regular holiday
            // adds 100%, not 200%, or it would be paid at 300%. It pays rest days too only under
            // the 365 factor; under 313 or 261 a rest day is unpaid, so work on one earns its
            // whole rate.
            decimal alreadyPaid = IsRestDay(day.DayType) && !PaysRestDays(factor) ? 0m : 1m;
            decimal premium = DolePremiumRates.BaseRate(day.DayType) - alreadyPaid;

            // A worked day of this type, and the first eight hours of work on a rest day.
            holidayPay += (dailyRate * premium * day.Days) + (hourlyRate * premium * day.Hours);

            // Overtime is never paid by the salary, so it earns its full rate: 125% on an
            // ordinary day, 130% of the day's rate on any other.
            overtimePay += hourlyRate * DolePremiumRates.Rate(day.DayType, overtime: true) * day.OvertimeHours;

            // A night hour is already paid at the day's rate; the differential is 10% of it.
            nightDiffPay += hourlyRate * day.NightDiffHours
                * (DolePremiumRates.Rate(day.DayType, nightShift: true) - DolePremiumRates.Rate(day.DayType));
        }

        overtimePay = Math.Round(overtimePay, 2);
        holidayPay = Math.Round(holidayPay, 2);
        nightDiffPay = Math.Round(nightDiffPay, 2);

        // Allowances: a regular period pays each monthly allowance's share of the month. A final
        // period is short, so each is pro-rated like its base pay instead - the monthly amount at
        // the factor's daily rate (x 12 / factor) for each salary day, none for none.
        decimal AllowanceFor(EmployeeAllowance allowance) => finalPay is not null
            ? allowance.Amount * 12m / factor * finalPay.WorkingDays
            : allowance.Amount / periodsPerMonth;
        decimal taxableAllowances = compensation.Allowances
            .Where(a => a.IsTaxable)
            .Sum(AllowanceFor);
        decimal nonTaxableAllowances = compensation.Allowances
            .Where(a => !a.IsTaxable)
            .Sum(AllowanceFor);

        taxableAllowances = Math.Round(taxableAllowances, 2);
        nonTaxableAllowances = Math.Round(nonTaxableAllowances, 2);

        // Custom attendance fields flagged as affecting earnings or deductions. The schema
        // carries a free-text Unit but no rate, so a value is taken as a peso amount - the only
        // reading the current tables can support. A quantity-times-rate field would need a Rate
        // on AttendanceCustomField, which is additive and does not touch the values payload.
        //
        // Phase 1 has no source for these: PayrollAttendanceInput carries no custom values, so
        // both stay zero and the arithmetic below is unchanged. Phase 2 fills them from
        // PeopleCore's own custom attendance fields.
        decimal customEarnings = 0m;
        // HR's final-pay deductions (unreturned property, unliquidated advances, ...) feed the
        // same capped "other deductions" bucket as an ordinary custom deduction would.
        decimal customDeductions = finalPay?.Deductions.Sum(d => d.Amount) ?? 0m;
        customDeductions = Math.Round(customDeductions, 2);

        // Custom earnings are taxable compensation, so they join the allowances before the
        // contribution and withholding bases are struck rather than after.
        taxableAllowances = Math.Round(taxableAllowances + customEarnings, 2);

        // 13th month (PD 851, Revised Guidelines 1987): one twelfth of the basic salary actually
        // earned in the year. Regular pay is that basic - net of absences and tardiness, and
        // without overtime, premiums or allowances - so this period's regular pay joins what the
        // year's earlier runs paid. Any part already paid this year (a mid-year advance) comes off.
        decimal thirteenthMonth = 0m;
        if (includeThirteenthMonth && isThirteenthMonthEligible)
        {
            decimal dueForYear = Math.Round((basicEarnedEarlierInYear + regularPay) / 12m, 2);
            thirteenthMonth = Math.Max(0m, dueForYear - thirteenthMonthPaidEarlierInYear);
        }

        // Final-pay earnings arrive already computed (see FinalPayMath). The non-taxable parts
        // are recorded as-is; the taxable remainder joins the withholding base below exactly
        // like a taxable allowance, but never the SSS/PhilHealth/Pag-IBIG base, which statute
        // fixes to the basic salary regardless of what else is paid out.
        decimal leaveConversionPay = 0m, leaveConversionNonTaxable = 0m, separationPay = 0m, retirementPay = 0m;
        decimal finalPayNonTaxable = 0m, finalPayTaxable = 0m;
        if (finalPay is not null)
        {
            leaveConversionPay = Math.Round(finalPay.LeaveConversionNonTaxable + finalPay.LeaveConversionTaxable, 2);
            leaveConversionNonTaxable = finalPay.LeaveConversionNonTaxable;
            separationPay = finalPay.SeparationPay;
            retirementPay = finalPay.RetirementPay;
            finalPayNonTaxable = leaveConversionNonTaxable + finalPay.SeparationAndRetirementNonTaxable;
            finalPayTaxable = finalPay.LeaveConversionTaxable
                + separationPay + retirementPay - finalPay.SeparationAndRetirementNonTaxable;
        }

        // Gross pay for contribution base = regular pay + OT + holiday + taxable allowances (non-taxable excluded from BIR base)
        decimal grossForContribs = regularPay + overtimePay + holidayPay + nightDiffPay + taxableAllowances
            + finalPayTaxable;

        // Mandatory contributions based on monthly salary
        // The SSS schedule in force for the run: a regular run's as of its period start; a final
        // pay's as of its contribution month - the first of the last working day's month (its
        // PeriodEnd), the month it tops up below - since its period can start in an earlier month.
        var sssAsOf = finalPay is not null
            ? new DateOnly(run.PeriodEnd.Year, run.PeriodEnd.Month, 1)
            : run.PeriodStart;
        var (sssEmp, sssEmr) = ComputeSSS(compensation.BasicSalary, rates, sssAsOf);
        var (phEmp, phEmr) = ComputePhilHealth(compensation.BasicSalary, rates);
        var (piEmp, piEmr) = ComputePagIbig(compensation.BasicSalary, rates);

        // For semi-monthly, SSS/PhilHealth/Pag-IBIG deducted once per month (half each period)
        // Standard practice: deduct full contribution in first cut-off, 0 in second; or split evenly.
        // We split evenly (half each cut-off). A final pay isn't a cutoff: it tops the month up
        // below instead.
        if (isSemiMonthly && finalPay is null)
        {
            sssEmp /= 2m; sssEmr /= 2m;
            phEmp /= 2m; phEmr /= 2m;
            piEmp /= 2m; piEmr /= 2m;
        }

        sssEmp = Math.Round(sssEmp, 2); sssEmr = Math.Round(sssEmr, 2);
        phEmp = Math.Round(phEmp, 2); phEmr = Math.Round(phEmr, 2);
        piEmp = Math.Round(piEmp, 2); piEmr = Math.Round(piEmr, 2);

        // A final pay tops the separation month up to exactly one month's contributions: each
        // share is the month's full amount less what the month's Paid runs already deducted, and
        // never below zero - so a month regular payroll already covered costs nothing more.
        if (finalPay is not null)
        {
            var deducted = finalPay.ContributionsDeductedInMonth ?? ContributionShares.None;
            sssEmp = Math.Max(0m, sssEmp - deducted.SssEmployee);
            sssEmr = Math.Max(0m, sssEmr - deducted.SssEmployer);
            phEmp = Math.Max(0m, phEmp - deducted.PhilHealthEmployee);
            phEmr = Math.Max(0m, phEmr - deducted.PhilHealthEmployer);
            piEmp = Math.Max(0m, piEmp - deducted.PagIbigEmployee);
            piEmr = Math.Max(0m, piEmr - deducted.PagIbigEmployer);
        }

        // Taxable income for BIR = gross taxable - mandatory deductions
        decimal taxableForBIR = grossForContribs - sssEmp - phEmp - piEmp;

        // A settled final-pay tax replaces both the per-period withholding and the 13th-month
        // excess tax below - it is the actual figure HR has already worked out, not an estimate
        // to be layered on top of one.
        decimal withholdingTax = finalPay?.WithholdingTaxOverride ??
            (ComputeWithholdingTax(taxableForBIR, compensation.PayFrequency)
                + ComputeThirteenthMonthTax(taxableForBIR, compensation.PayFrequency,
                    thirteenthMonth, thirteenthMonthPaidEarlierInYear));

        // Loan deductions. Each active loan contributes its per-period instalment, but never
        // more than is still owed - an employee must not be charged past the payoff - and only
        // once the loan has started. The per-loan detail is recorded so marking the run paid can
        // retire exactly what was withheld instead of re-deriving it from the loan's schedule.
        var loanLines = new List<PayrollLoanDeduction>();
        foreach (var loan in compensation.Loans.Where(l => l.IsActive))
        {
            if (loan.StartDate > run.PeriodEnd)
                continue;

            // A final pay retires each loan outright instead of taking one more instalment -
            // there is no next period left to keep collecting from.
            decimal instalment = finalPay is not null
                ? loan.RemainingBalance
                : Math.Round(loan.MonthlyDeduction / periodsPerMonth, 2);
            decimal amount = Math.Min(instalment, loan.RemainingBalance);
            if (amount <= 0m)
                continue;

            loanLines.Add(new PayrollLoanDeduction
            {
                EmployeeLoanId = loan.Id,
                LoanType = loan.LoanType.ToString(),
                Amount = amount
            });
        }

        // Statutory deductions must be remitted whatever the period looked like, so only the
        // discretionary ones give way when they would not fit. Clamping net pay itself would
        // record deductions that were never actually withheld, and the loan balances retired
        // when the run is paid come from these lines - so they have to be the capped amounts.
        decimal statutory = sssEmp + phEmp + piEmp + withholdingTax;
        decimal discretionaryBudget = Math.Max(0m,
            regularPay + overtimePay + holidayPay + nightDiffPay + taxableAllowances +
            nonTaxableAllowances + thirteenthMonth + leaveConversionPay + separationPay + retirementPay
            - statutory);

        decimal loanDeductions = loanLines.Sum(l => l.Amount);
        if (loanDeductions > discretionaryBudget)
        {
            // Pro-rate the shortfall across the loans so no single one absorbs all of it; the
            // remainder simply stays on the balance and is collected next period.
            decimal affordable = discretionaryBudget;
            decimal running = 0m;
            for (int i = 0; i < loanLines.Count; i++)
            {
                bool last = i == loanLines.Count - 1;
                decimal share = last
                    ? affordable - running
                    : Math.Round(affordable * (loanLines[i].Amount / loanDeductions), 2);
                loanLines[i].Amount = Math.Max(0m, share);
                running += loanLines[i].Amount;
            }
            loanLines.RemoveAll(l => l.Amount <= 0m);
            loanDeductions = loanLines.Sum(l => l.Amount);
        }

        decimal otherDeductions = Math.Min(customDeductions, Math.Max(0m, discretionaryBudget - loanDeductions));

        return new PayrollRunEmployee
        {
            PayrollRunId = run.Id,
            EmployeeId = compensation.EmployeeId,
            PayrollRun = run,
            DaysWorked = daysWorked,
            // Roll-ups for display; PremiumDays below is what a recompute reprices from.
            OvertimeHours = premiumDays.Sum(d => d.OvertimeHours + d.Hours),
            HolidayDays = premiumDays.Where(d => !IsOrdinaryOrRestDay(d.DayType)).Sum(d => d.Days),
            IncludeThirteenthMonth = includeThirteenthMonth,
            RegularPay = regularPay,
            OvertimePay = overtimePay,
            HolidayPay = holidayPay,
            NightDiffPay = nightDiffPay,
            DailyRate = dailyRate,
            DailyRateFactor = factor,
            AbsenceDeduction = absenceDeduction,
            TardinessDeduction = tardinessDeduction,
            TaxableAllowances = taxableAllowances,
            NonTaxableAllowances = nonTaxableAllowances,
            ThirteenthMonth = thirteenthMonth,
            LeaveConversionPay = leaveConversionPay,
            LeaveConversionNonTaxable = leaveConversionNonTaxable,
            SeparationPay = separationPay,
            RetirementPay = retirementPay,
            FinalPayNonTaxable = finalPayNonTaxable,
            SSSEmployee = sssEmp,
            SSSEmployer = sssEmr,
            PhilHealthEmployee = phEmp,
            PhilHealthEmployer = phEmr,
            PagIbigEmployee = piEmp,
            PagIbigEmployer = piEmr,
            WithholdingTax = withholdingTax,
            LoanDeductions = loanDeductions,
            OtherDeductions = otherDeductions,
            LoanDeductionLines = loanLines,
            PremiumDays = premiumDays
                .Select(d => new PayrollRunPremiumDay
                {
                    DayType = d.DayType,
                    Days = d.Days,
                    Hours = d.Hours,
                    OvertimeHours = d.OvertimeHours,
                    NightDiffHours = d.NightDiffHours
                })
                .ToList()
        };
    }

    private static bool IsRestDay(WorkDayType day) => day is WorkDayType.RestDay
        or WorkDayType.SpecialNonWorkingOnRestDay or WorkDayType.DoubleSpecialNonWorkingOnRestDay
        or WorkDayType.RegularHolidayOnRestDay or WorkDayType.DoubleRegularHolidayOnRestDay;

    private static bool IsOrdinaryOrRestDay(WorkDayType day) =>
        day is WorkDayType.Ordinary or WorkDayType.RestDay;
}
