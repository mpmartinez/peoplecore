using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Employees;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Employees;

public class EmployeeGovernmentIdConfiguration : IEntityTypeConfiguration<EmployeeGovernmentId>
{
    public void Configure(EntityTypeBuilder<EmployeeGovernmentId> builder)
    {
        builder.HasKey(g => g.Id);
        builder.HasIndex(g => new { g.EmployeeId, g.IdType }).IsUnique();
        builder.Property(g => g.IdType).HasConversion<string>();
        builder.Property(g => g.IdNumber).IsRequired().HasMaxLength(100);
        builder.ToTable("employee_government_ids");
    }
}
