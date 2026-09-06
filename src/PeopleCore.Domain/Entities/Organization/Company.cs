namespace PeopleCore.Domain.Entities.Organization;

public class Company : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public ICollection<Department> Departments { get; set; } = [];

    // Statutory identity, used on payslips and BIR filings.
    public string TIN { get; set; } = "";
    public string SSSNumber { get; set; } = "";
    public string PhilHealthNumber { get; set; } = "";
    public string PagIbigNumber { get; set; } = "";
    public string City { get; set; } = "";
    public byte[]? Logo { get; set; }
    public string? RdoCode { get; set; }
    public string? ZipCode { get; set; }
}
