namespace PeopleCore.Domain.Entities.Organization;

public class Position : AuditableEntity
{
    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string? Level { get; set; }
}
