using PeopleCore.Domain.Entities.Employees;

namespace PeopleCore.Domain.Entities.Payroll;

/// <summary>
/// What an employee was paid in a calendar year before PeopleCore: the months paid outside it, up
/// to <see cref="ThroughDate"/>. Every "earlier this year" figure (the 13th month, the ₱90,000
/// exemption, the 2316, the de minimis leave-days cap) adds it to the year's Paid runs.
/// One per employee and year.
/// </summary>
public class PayrollOpeningBalance : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;

    /// <summary>The calendar year the figures belong to.</summary>
    public int Year { get; set; }

    /// <summary>The last pay date the figures include; it falls in <see cref="Year"/>.</summary>
    public DateOnly ThroughDate { get; set; }

    /// <summary>Basic salary earned, gross (before absences and contributions).</summary>
    public decimal BasicSalary { get; set; }

    /// <summary>13th month already paid.</summary>
    public decimal ThirteenthMonthPaid { get; set; }

    /// <summary>Other benefits (bonuses and the like) paid, which count toward the ₱90,000 exemption.</summary>
    public decimal OtherBenefitsPaid { get; set; }

    /// <summary>Overtime, holiday, night differential and taxable allowances, as one total.</summary>
    public decimal OtherTaxablePay { get; set; }

    /// <summary>De minimis benefits paid.</summary>
    public decimal DeMinimis { get; set; }

    /// <summary>Other non-taxable compensation.</summary>
    public decimal OtherNonTaxable { get; set; }

    /// <summary>The employee's SSS, PhilHealth and Pag-IBIG shares, as one total.</summary>
    public decimal EmployeeContributions { get; set; }

    /// <summary>Withholding tax deducted.</summary>
    public decimal TaxWithheld { get; set; }

    /// <summary>Leave days already converted as de minimis (0 to 10).</summary>
    public decimal DeMinimisLeaveDays { get; set; }
}
