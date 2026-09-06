namespace PeopleCore.Domain.Entities.Payroll;

public class PayrollRunEmployee : AuditableEntity
{
    public Guid PayrollRunId { get; set; }
    public PayrollRun PayrollRun { get; set; } = null!;
    public Guid EmployeeId { get; set; }
    public Employees.Employee Employee { get; set; } = null!;

    // Work details
    public decimal DaysWorked { get; set; } = 22m;
    public decimal OvertimeHours { get; set; } = 0m;
    public decimal HolidayDays { get; set; } = 0m;
    public bool IncludeThirteenthMonth { get; set; } = false;

    // Earnings
    /// <summary>Pay for ordinary time, already net of absences and tardiness.</summary>
    public decimal RegularPay { get; set; }
    public decimal OvertimePay { get; set; }
    public decimal HolidayPay { get; set; }
    /// <summary>The 10% night shift premium on hours worked between 10 p.m. and 6 a.m.</summary>
    public decimal NightDiffPay { get; set; }
    public decimal TaxableAllowances { get; set; }
    public decimal NonTaxableAllowances { get; set; }
    public decimal ThirteenthMonth { get; set; }
    public decimal GrossPay => RegularPay + OvertimePay + HolidayPay + NightDiffPay + TaxableAllowances + NonTaxableAllowances + ThirteenthMonth;

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
    public decimal NetPay => GrossPay - TotalDeductions;

    // Employer cost total
    public decimal TotalEmployerCost =>
        GrossPay + SSSEmployer + PhilHealthEmployer + PagIbigEmployer;
}
