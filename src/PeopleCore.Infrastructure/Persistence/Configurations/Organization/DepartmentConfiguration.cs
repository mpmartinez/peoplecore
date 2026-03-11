using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Organization;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Organization;

public class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Name).IsRequired().HasMaxLength(200);
        builder.Property(d => d.Code).HasMaxLength(50);
        builder.HasOne(d => d.ParentDepartment)
               .WithMany(d => d.SubDepartments)
               .HasForeignKey(d => d.ParentDepartmentId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(d => d.Company)
               .WithMany(c => c.Departments)
               .HasForeignKey(d => d.CompanyId);
        builder.ToTable("departments");
    }
}
