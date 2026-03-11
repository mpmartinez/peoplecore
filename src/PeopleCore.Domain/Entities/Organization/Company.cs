namespace PeopleCore.Domain.Entities.Organization;

public class Company : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public ICollection<Department> Departments { get; set; } = [];
}
