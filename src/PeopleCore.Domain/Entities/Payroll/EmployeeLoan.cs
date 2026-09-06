using PeopleCore.Domain.Enums;

namespace PeopleCore.Domain.Entities.Payroll;

public class EmployeeLoan : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public LoanType LoanType { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal MonthlyDeduction { get; set; }
    public decimal RemainingBalance { get; set; }
    public DateOnly StartDate { get; set; }
    public bool IsActive { get; set; } = true;
}
