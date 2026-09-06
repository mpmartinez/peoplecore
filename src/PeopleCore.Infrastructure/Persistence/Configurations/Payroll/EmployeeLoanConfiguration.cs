using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Payroll;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Payroll;

public class EmployeeLoanConfiguration : IEntityTypeConfiguration<EmployeeLoan>
{
    public void Configure(EntityTypeBuilder<EmployeeLoan> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TotalAmount).HasColumnType("numeric(18,2)");
        builder.Property(x => x.MonthlyDeduction).HasColumnType("numeric(18,2)");
        builder.Property(x => x.RemainingBalance).HasColumnType("numeric(18,2)");

        // Keyed by EmployeeId, not related to EmployeeCompensation or Employee by navigation -
        // see EmployeeCompensationConfiguration. Indexed for lookups by employee.
        builder.HasIndex(x => x.EmployeeId);
    }
}
