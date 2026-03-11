using PeopleCore.Domain.Enums;

namespace PeopleCore.Domain.Entities.Employees;

public class EmployeeGovernmentId : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public GovernmentIdType IdType { get; set; }
    public string IdNumber { get; set; } = string.Empty;
}
