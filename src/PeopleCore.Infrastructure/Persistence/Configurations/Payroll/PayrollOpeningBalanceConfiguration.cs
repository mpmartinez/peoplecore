using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Payroll;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Payroll;

public class PayrollOpeningBalanceConfiguration : IEntityTypeConfiguration<PayrollOpeningBalance>
{
    public void Configure(EntityTypeBuilder<PayrollOpeningBalance> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.BasicSalary).HasColumnType("numeric(18,2)").HasDefaultValue(0m);
        builder.Property(x => x.ThirteenthMonthPaid).HasColumnType("numeric(18,2)").HasDefaultValue(0m);
        builder.Property(x => x.OtherBenefitsPaid).HasColumnType("numeric(18,2)").HasDefaultValue(0m);
        builder.Property(x => x.OtherTaxablePay).HasColumnType("numeric(18,2)").HasDefaultValue(0m);
        builder.Property(x => x.DeMinimis).HasColumnType("numeric(18,2)").HasDefaultValue(0m);
        builder.Property(x => x.OtherNonTaxable).HasColumnType("numeric(18,2)").HasDefaultValue(0m);
        builder.Property(x => x.EmployeeContributions).HasColumnType("numeric(18,2)").HasDefaultValue(0m);
        builder.Property(x => x.TaxWithheld).HasColumnType("numeric(18,2)").HasDefaultValue(0m);
        builder.Property(x => x.DeMinimisLeaveDays).HasColumnType("numeric(6,2)").HasDefaultValue(0m);

        // One opening balance per employee and year.
        builder.HasIndex(x => new { x.EmployeeId, x.Year }).IsUnique();

        builder.HasOne(x => x.Employee)
               .WithMany()
               .HasForeignKey(x => x.EmployeeId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
