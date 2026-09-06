namespace PeopleCore.Domain.Entities.Payroll;

/// <summary>
/// One loan's deduction within one payroll entry - the record of what was actually withheld,
/// as opposed to what the loan's schedule says should be withheld.
/// </summary>
public class PayrollLoanDeduction : AuditableEntity
{
    public Guid PayrollRunEmployeeId { get; set; }
    public PayrollRunEmployee PayrollRunEmployee { get; set; } = null!;
    public Guid EmployeeLoanId { get; set; }
    public string LoanType { get; set; } = "";
    public decimal Amount { get; set; }
}
