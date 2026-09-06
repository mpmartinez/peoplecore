using M2NET.Core.Enums;

namespace M2NET.Core.Entities;

public class Employee
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EmployeeNumber { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string MiddleName { get; set; } = "";
    public string LastName { get; set; } = "";
    public Guid? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public Guid? PositionId { get; set; }
    public string? PositionName { get; set; }
    public EmploymentType EmploymentType { get; set; }
    public CivilStatus CivilStatus { get; set; }
    public Gender Gender { get; set; }
    public string TIN { get; set; } = "";
    public string SSSNumber { get; set; } = "";
    public string PhilHealthNumber { get; set; } = "";
    public string PagIbigNumber { get; set; } = "";
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Mobile { get; set; }
    public DateTime? BirthDate { get; set; }
    public string BankName { get; set; } = "";
    public string BankAccountNumber { get; set; } = "";
    public DateTime HireDate { get; set; }
    public DateTime? SeparationDate { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public string DisplayName => $"{LastName}, {FirstName} {MiddleName}".TrimEnd();
}
