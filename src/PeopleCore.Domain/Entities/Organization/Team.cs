namespace PeopleCore.Domain.Entities.Organization;

public class Team : AuditableEntity
{
    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
}
