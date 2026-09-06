using PeopleCore.Domain.Enums;

namespace PeopleCore.Domain.Entities.Payroll;

public class EmployeeAllowance : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public AllowanceType Type { get; set; }
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public bool IsTaxable { get; set; } = false;
}
