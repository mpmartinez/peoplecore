using M2NET.Core.Enums;
using PeopleCore.Domain.Entities;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Entities.Organization;

namespace PeopleCore.Domain.Entities.Employees;

public class Employee : M2NET.Core.Entities.Employee, IAuditableEntity
{
    // Shadow MiddleName to allow null (base has non-nullable string)
    public new string? MiddleName { get; set; }

    // Shadow HireDate/SeparationDate to use DateOnly (base uses DateTime)
    public new DateOnly HireDate { get; set; }
    public new DateOnly? SeparationDate { get; set; }

    // Audit fields not in M2NET.Core base
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }

    // PeopleCore-specific identity fields
    public string? Suffix { get; set; }
    public DateOnly DateOfBirth { get; set; }      // base has BirthDate (DateTime?) - different name/type
    public string Nationality { get; set; } = "Filipino";
    public string? PersonalEmail { get; set; }     // base has Email
    public string WorkEmail { get; set; } = string.Empty;
    public string? MobileNumber { get; set; }      // base has Mobile/Phone
    public string? Address { get; set; }

    // Navigation properties (DepartmentId/PositionId FKs come from M2NET.Core base)
    public Department? Department { get; set; }    // nav for DepartmentId
    public Position? Position { get; set; }        // nav for PositionId
    public Guid? TeamId { get; set; }
    public Team? Team { get; set; }
    public Guid? ReportingManagerId { get; set; }
    public Employee? ReportingManager { get; set; }

    // Employment details
    public EmploymentStatus EmploymentStatus { get; set; }
    public DateOnly? RegularizationDate { get; set; }
    public bool Is13thMonthEligible { get; set; } = true;

    // Collections
    public ICollection<EmployeeGovernmentId> GovernmentIds { get; set; } = [];
    public ICollection<EmergencyContact> EmergencyContacts { get; set; } = [];
    public ICollection<EmployeeDocument> Documents { get; set; } = [];

    public string FullName => string.Join(" ", new[] { FirstName, MiddleName, LastName }
        .Where(s => !string.IsNullOrWhiteSpace(s)));
}
