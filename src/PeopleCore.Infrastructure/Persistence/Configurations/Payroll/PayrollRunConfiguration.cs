using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Payroll;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Payroll;

public class PayrollRunConfiguration : IEntityTypeConfiguration<PayrollRun>
{
    public void Configure(EntityTypeBuilder<PayrollRun> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.RunNumber).IsUnique();

        // Computed properties - not columns.
        builder.Ignore(x => x.PeriodLabel);
        builder.Ignore(x => x.EmployeeCount);
        builder.Ignore(x => x.TotalGrossPay);
        builder.Ignore(x => x.TotalDeductions);
        builder.Ignore(x => x.TotalNetPay);

        builder.HasMany(x => x.Employees)
               .WithOne(x => x.PayrollRun)
               .HasForeignKey(x => x.PayrollRunId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
