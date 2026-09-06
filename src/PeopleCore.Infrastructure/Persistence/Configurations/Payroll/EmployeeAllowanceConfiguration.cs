using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Payroll;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Payroll;

public class EmployeeAllowanceConfiguration : IEntityTypeConfiguration<EmployeeAllowance>
{
    public void Configure(EntityTypeBuilder<EmployeeAllowance> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Amount).HasColumnType("numeric(18,2)");

        // Keyed by EmployeeId, not related to EmployeeCompensation by navigation - see
        // EmployeeCompensationConfiguration. Indexed for lookups by employee.
        builder.HasIndex(x => x.EmployeeId);

        // FK-only relationship: EmployeeAllowance has no Employee navigation property (a loan
        // must be recordable before a compensation row exists, so this is deliberately not tied
        // to EmployeeCompensation). An employee carrying financial records should not be
        // silently deletable - see PayrollRunEmployeeConfiguration.
        builder.HasOne<PeopleCore.Domain.Entities.Employees.Employee>()
               .WithMany()
               .HasForeignKey(x => x.EmployeeId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
