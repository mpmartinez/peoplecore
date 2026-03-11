using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Employees;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Employees;

public class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.EmployeeNumber).IsUnique();
        builder.Property(e => e.EmployeeNumber).IsRequired().HasMaxLength(50);
        builder.Property(e => e.FirstName).IsRequired().HasMaxLength(100);
        builder.Property(e => e.LastName).IsRequired().HasMaxLength(100);
        builder.Property(e => e.WorkEmail).IsRequired().HasMaxLength(200);
        builder.Property(e => e.EmploymentStatus).HasConversion<string>();
        builder.HasOne(e => e.ReportingManager)
               .WithMany()
               .HasForeignKey(e => e.ReportingManagerId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(e => e.GovernmentIds)
               .WithOne(g => g.Employee)
               .HasForeignKey(g => g.EmployeeId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(e => e.EmergencyContacts)
               .WithOne(c => c.Employee)
               .HasForeignKey(c => c.EmployeeId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(e => e.Documents)
               .WithOne(d => d.Employee)
               .HasForeignKey(d => d.EmployeeId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.Ignore(e => e.FullName);
        builder.ToTable("employees");
    }
}
