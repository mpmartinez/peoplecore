using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Payroll;
using PeopleCore.Domain.Payroll;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Payroll;

public class Bir2316InputsConfiguration : IEntityTypeConfiguration<Bir2316Inputs>
{
    public void Configure(EntityTypeBuilder<Bir2316Inputs> builder)
    {
        builder.HasKey(x => x.Id);

        // One set of inputs per employee per year.
        builder.HasIndex(x => new { x.EmployeeId, x.Year }).IsUnique();

        builder.Property(x => x.PrevEmployerTin).HasMaxLength(20);
        builder.Property(x => x.PrevEmployerName).HasMaxLength(PayrollInputLimits.MaxEmployerNameLength);
        builder.Property(x => x.PrevEmployerAddress).HasMaxLength(PayrollInputLimits.MaxEmployerAddressLength);
        builder.Property(x => x.PrevEmployerZipCode).HasMaxLength(10);
        builder.Property(x => x.Item22_PrevTaxableCompensation).HasColumnType("numeric(18,2)");
        builder.Property(x => x.Item25B_PrevTaxWithheld).HasColumnType("numeric(18,2)");
        builder.Property(x => x.Item27_PeraTaxCredit).HasColumnType("numeric(18,2)");
        builder.Property(x => x.Item35_DeMinimis).HasColumnType("numeric(18,2)");
        builder.Property(x => x.Item33_HazardPayMwe).HasColumnType("numeric(18,2)");
        builder.Property(x => x.StatutoryMinWagePerDay).HasColumnType("numeric(18,2)");
        builder.Property(x => x.StatutoryMinWagePerMonth).HasColumnType("numeric(18,2)");

        builder.HasOne<PeopleCore.Domain.Entities.Employees.Employee>()
               .WithMany()
               .HasForeignKey(x => x.EmployeeId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
