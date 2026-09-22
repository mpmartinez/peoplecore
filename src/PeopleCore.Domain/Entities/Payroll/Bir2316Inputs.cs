namespace PeopleCore.Domain.Entities.Payroll;

/// <summary>
/// The BIR 2316 fields a person supplies for one employee and year - a previous employer's
/// figures, a PERA credit, de minimis - saved so a single 2316, "Generate all" and the 1604-C
/// alphalist all use the same values instead of each starting blank. Holds no derived figure:
/// a certificate's money still comes from payroll.
/// </summary>
public class Bir2316Inputs : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public int Year { get; set; }

    public string? PrevEmployerTin { get; set; }
    public string? PrevEmployerName { get; set; }
    public string? PrevEmployerAddress { get; set; }
    public string? PrevEmployerZipCode { get; set; }
    public decimal Item22_PrevTaxableCompensation { get; set; }
    public decimal Item25B_PrevTaxWithheld { get; set; }
    public decimal Item27_PeraTaxCredit { get; set; }
    public decimal Item35_DeMinimis { get; set; }
    public decimal Item33_HazardPayMwe { get; set; }
    public decimal StatutoryMinWagePerDay { get; set; }
    public decimal StatutoryMinWagePerMonth { get; set; }
}
