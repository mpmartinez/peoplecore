using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Payroll;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Payroll;

public class PayrollSettingsConfiguration : IEntityTypeConfiguration<PayrollSettings>
{
    public void Configure(EntityTypeBuilder<PayrollSettings> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.PhilHealthRate).HasColumnType("numeric(8,4)");
        builder.Property(x => x.PhilHealthMinShare).HasColumnType("numeric(18,2)");
        builder.Property(x => x.PhilHealthMaxShare).HasColumnType("numeric(18,2)");

        builder.Property(x => x.PagIbigEmployeeRate).HasColumnType("numeric(8,4)");
        builder.Property(x => x.PagIbigLowEmployeeRate).HasColumnType("numeric(8,4)");
        builder.Property(x => x.PagIbigLowRateThreshold).HasColumnType("numeric(18,2)");
        builder.Property(x => x.PagIbigEmployerRate).HasColumnType("numeric(8,4)");
        builder.Property(x => x.PagIbigMaxFundSalary).HasColumnType("numeric(18,2)");

        builder.Property(x => x.DailyRateFactor).HasColumnType("numeric(8,2)");

        builder.Property(x => x.SSSEmployeeRate).HasColumnType("numeric(8,4)");
        builder.Property(x => x.SSSEmployerRate).HasColumnType("numeric(8,4)");

        // Only one settings row per company - and, until PayrollRun carries a CompanyId (see
        // IPayrollSettingsRepository.GetDefaultAsync), only one row at all is actually usable.
        // The database enforces the per-company half of that even if application code does not.
        builder.HasIndex(x => x.CompanyId).IsUnique();

        builder.HasOne(x => x.Company)
               .WithMany()
               .HasForeignKey(x => x.CompanyId);
    }
}
