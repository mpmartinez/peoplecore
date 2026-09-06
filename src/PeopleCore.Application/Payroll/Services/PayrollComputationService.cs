using PeopleCore.Application.Payroll.DTOs;
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
    /// Compute BIR withholding tax for a semi-monthly payroll period.
    /// taxableIncome = semi-monthly gross taxable (after mandatory deductions).
    /// Returns the semi-monthly withholding tax.
    /// </summary>
    public decimal ComputeWithholdingTax(decimal semiMonthlyTaxableIncome)
    {
        // Annualize the semi-monthly taxable income (24 periods), tax it on the shared annual
        // bracket table, then de-annualize. BIR Form 2316 Item 24 reads the same table.
        decimal annualTax = BirWithholdingTax.ComputeAnnualTax(semiMonthlyTaxableIncome * 24m);

        return Math.Round(annualTax / 24m, 2);
    }

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
    public PayrollRunEmployee Compute(EmployeeCompensation compensation, PayrollRun run, decimal daysWorked = 0,
        decimal overtimeHours = 0, decimal holidayDays = 0, bool includeThirteenthMonth = false,
        ContributionRates? rates = null, PayrollAttendanceInput? attendance = null,
        decimal? dailyRateFactor = null)
    {
        bool isSemiMonthly = compensation.PayFrequency == PayFrequency.SemiMonthly;
        decimal periodsPerMonth = isSemiMonthly ? 2m : 1m;

        // Rate basis. DOLE Handbook 2024 section E gives the equivalent monthly rate as
        // daily x factor / 12; inverted, the applicable daily rate is monthly x 12 / factor.
        // The handbook calls these formulas suggestions "without prejudice to existing company
        // policies", so the factor is configurable per company.
        decimal factor = dailyRateFactor is > 0m ? dailyRateFactor.Value : DefaultDailyRateFactor;
        decimal dailyRate = Math.Round(compensation.BasicSalary * 12m / factor, 2);
        decimal hourlyRate = Math.Round(dailyRate / 8m, 2);

        // A full period pays the full salary slice, and attendance adjusts it from there.
        // Rebuilding gross from the daily rate would stop a full month reconciling to the
        // monthly salary, because the factor counts unworked rest days and holidays as paid.
        decimal basePeriodPay = Math.Round(compensation.BasicSalary / periodsPerMonth, 2);

        decimal absenceDeduction = Math.Round(dailyRate * (attendance?.AbsenceDays ?? 0m), 2);
        decimal lostMinutes = (attendance?.LateMinutes ?? 0m) + (attendance?.UndertimeMinutes ?? 0m);
        decimal tardinessDeduction = Math.Round(hourlyRate * lostMinutes / 60m, 2);
        decimal regularPay = Math.Max(0m, basePeriodPay - absenceDeduction - tardinessDeduction);

        // Overtime: 125% on an ordinary day, 169% on a rest day (1.30 x 1.30).
        decimal ordinaryOtHours = attendance?.OvertimeHours ?? overtimeHours;
        decimal restDayOtHours = attendance?.RestDayOTHours ?? 0m;
        decimal overtimePay = Math.Round(
            (hourlyRate * DolePremiumRates.Rate(WorkDayType.Ordinary, overtime: true) * ordinaryOtHours) +
            (hourlyRate * DolePremiumRates.Rate(WorkDayType.RestDay, overtime: true) * restDayOtHours), 2);

        // Holiday work adds the increment above 100%, not the whole multiplier: under the 365
        // factor the monthly salary already pays these days, so charging the full 200% would
        // pay 300% for a worked regular holiday.
        decimal regularHolidayDays = attendance?.HolidayRegularDays ?? holidayDays;
        decimal specialHolidayDays = attendance?.HolidaySpecialDays ?? 0m;
        decimal holidayPay = Math.Round(
            (dailyRate * DolePremiumRates.Premium(WorkDayType.RegularHoliday) * regularHolidayDays) +
            (dailyRate * DolePremiumRates.Premium(WorkDayType.SpecialNonWorking) * specialHolidayDays), 2);

        // Night shift differential: the hour is already paid, so only the 10% premium is added.
        decimal nightDiffPay = Math.Round(
            hourlyRate * DolePremiumRates.Premium(WorkDayType.Ordinary, nightShift: true)
                       * (attendance?.NightDiffHours ?? 0m), 2);

        // Allowances
        decimal taxableAllowances = compensation.Allowances
            .Where(a => a.IsTaxable)
            .Sum(a => a.Amount / periodsPerMonth);
        decimal nonTaxableAllowances = compensation.Allowances
            .Where(a => !a.IsTaxable)
            .Sum(a => a.Amount / periodsPerMonth);

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
        decimal customDeductions = 0m;
        customDeductions = Math.Round(customDeductions, 2);

        // Custom earnings are taxable compensation, so they join the allowances before the
        // contribution and withholding bases are struck rather than after.
        taxableAllowances = Math.Round(taxableAllowances + customEarnings, 2);

        // 13th month (included in December or when flagged)
        decimal thirteenthMonth = 0m;
        if (includeThirteenthMonth)
            thirteenthMonth = Math.Round(compensation.BasicSalary, 2); // 1 month basic

        // Gross pay for contribution base = regular pay + OT + holiday + taxable allowances (non-taxable excluded from BIR base)
        decimal grossForContribs = regularPay + overtimePay + holidayPay + nightDiffPay + taxableAllowances;

        // Mandatory contributions based on monthly salary
        var (sssEmp, sssEmr) = ComputeSSS(compensation.BasicSalary, rates, run.PeriodStart);
        var (phEmp, phEmr) = ComputePhilHealth(compensation.BasicSalary, rates);
        var (piEmp, piEmr) = ComputePagIbig(compensation.BasicSalary, rates);

        // For semi-monthly, SSS/PhilHealth/Pag-IBIG deducted once per month (half each period)
        // Standard practice: deduct full contribution in first cut-off, 0 in second; or split evenly.
        // We split evenly (half each cut-off).
        if (isSemiMonthly)
        {
            sssEmp /= 2m; sssEmr /= 2m;
            phEmp /= 2m; phEmr /= 2m;
            piEmp /= 2m; piEmr /= 2m;
        }

        sssEmp = Math.Round(sssEmp, 2); sssEmr = Math.Round(sssEmr, 2);
        phEmp = Math.Round(phEmp, 2); phEmr = Math.Round(phEmr, 2);
        piEmp = Math.Round(piEmp, 2); piEmr = Math.Round(piEmr, 2);

        // Taxable income for BIR = gross taxable - mandatory deductions
        decimal taxableForBIR = grossForContribs - sssEmp - phEmp - piEmp;
        decimal withholdingTax = ComputeWithholdingTax(taxableForBIR);

        // Loan deductions. Each active loan contributes its per-period instalment, but never
        // more than is still owed - an employee must not be charged past the payoff - and only
        // once the loan has started. The per-loan detail is recorded so marking the run paid can
        // retire exactly what was withheld instead of re-deriving it from the loan's schedule.
        var loanLines = new List<PayrollLoanDeduction>();
        foreach (var loan in compensation.Loans.Where(l => l.IsActive))
        {
            if (loan.StartDate > run.PeriodEnd)
                continue;

            decimal instalment = Math.Round(loan.MonthlyDeduction / periodsPerMonth, 2);
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
            nonTaxableAllowances + thirteenthMonth - statutory);

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
            OvertimeHours = ordinaryOtHours + restDayOtHours,
            HolidayDays = regularHolidayDays + specialHolidayDays,
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
            SSSEmployee = sssEmp,
            SSSEmployer = sssEmr,
            PhilHealthEmployee = phEmp,
            PhilHealthEmployer = phEmr,
            PagIbigEmployee = piEmp,
            PagIbigEmployer = piEmr,
            WithholdingTax = withholdingTax,
            LoanDeductions = loanDeductions,
            OtherDeductions = otherDeductions,
            LoanDeductionLines = loanLines
        };
    }
}
