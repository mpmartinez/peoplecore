using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Payroll;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Payroll;

public class EmployeeCompensationConfiguration : IEntityTypeConfiguration<EmployeeCompensation>
{
    public void Configure(EntityTypeBuilder<EmployeeCompensation> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.BasicSalary).HasColumnType("numeric(18,2)");
        builder.Property(x => x.TaxCode).HasMaxLength(8);

        // One compensation row per employee.
        builder.HasIndex(x => x.EmployeeId).IsUnique();

        builder.HasOne(x => x.Employee)
               .WithOne()
               .HasForeignKey<EmployeeCompensation>(x => x.EmployeeId)
               .OnDelete(DeleteBehavior.Cascade);

        // Populated by the application service from their own repositories before computation.
        // Not mapped: allowances and loans are keyed by EmployeeId and stand alone, so a loan can
        // be recorded before a compensation row exists.
        builder.Ignore(x => x.Allowances);
        builder.Ignore(x => x.Loans);
    }
}
