using PeopleCore.Domain.Enums;

namespace PeopleCore.Domain.Entities.Payroll;

/// <summary>
/// An employee's pay basis. Deliberately separate from Employee: PeopleCore serves employee
/// data to the Manager and Employee roles through ESS, and compensation must never travel in
/// an HR DTO.
/// </summary>
public class EmployeeCompensation : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public Employees.Employee Employee { get; set; } = null!;

    public decimal BasicSalary { get; set; }
    public PayFrequency PayFrequency { get; set; } = PayFrequency.SemiMonthly;
    public string TaxCode { get; set; } = "ME";
    public int Dependents { get; set; }

    /// <summary>
    /// The employee's recurring allowances. Both collections relate on <see cref="EmployeeId"/>
    /// rather than on this row's own key - allowances and loans stay keyed by employee, as the
    /// design requires, so a loan can be recorded before a compensation row exists.
    /// </summary>
    public List<EmployeeAllowance> Allowances { get; set; } = [];

    /// <summary>The employee's loans, related on <see cref="EmployeeId"/>. See <see cref="Allowances"/>.</summary>
    public List<EmployeeLoan> Loans { get; set; } = [];
}
