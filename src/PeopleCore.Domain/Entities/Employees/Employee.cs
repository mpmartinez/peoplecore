using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Entities.Organization;

namespace PeopleCore.Domain.Entities.Employees;

public class Employee : AuditableEntity
{
    public string EmployeeNumber { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = string.Empty;
    public string? Suffix { get; set; }
    public DateOnly DateOfBirth { get; set; }
    public string Gender { get; set; } = string.Empty;
    public string? CivilStatus { get; set; }
    public string Nationality { get; set; } = "Filipino";
    public string? PersonalEmail { get; set; }
    public string WorkEmail { get; set; } = string.Empty;
    public string? MobileNumber { get; set; }
    public string? Address { get; set; }
    public Guid? DepartmentId { get; set; }
    public Department? Department { get; set; }
    public Guid? PositionId { get; set; }
    public Position? Position { get; set; }
    public Guid? TeamId { get; set; }
    public Team? Team { get; set; }
    public Guid? ReportingManagerId { get; set; }
    public Employee? ReportingManager { get; set; }
    public EmploymentStatus EmploymentStatus { get; set; }
    public string EmploymentType { get; set; } = "FullTime";
    public DateOnly HireDate { get; set; }
    public DateOnly? RegularizationDate { get; set; }
    public DateOnly? SeparationDate { get; set; }
    public bool IsActive { get; set; } = true;
    public bool Is13thMonthEligible { get; set; } = true;
    public ICollection<EmployeeGovernmentId> GovernmentIds { get; set; } = [];
    public ICollection<EmergencyContact> EmergencyContacts { get; set; } = [];
    public ICollection<EmployeeDocument> Documents { get; set; } = [];

    public string FullName => string.Join(" ", new[] { FirstName, MiddleName, LastName }
        .Where(s => !string.IsNullOrWhiteSpace(s)));
}
