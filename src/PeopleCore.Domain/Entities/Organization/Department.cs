namespace PeopleCore.Domain.Entities.Organization;

public class Department : AuditableEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public Guid? ParentDepartmentId { get; set; }
    public Department? ParentDepartment { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public ICollection<Department> SubDepartments { get; set; } = [];
    public ICollection<Position> Positions { get; set; } = [];
    public ICollection<Team> Teams { get; set; } = [];
}
