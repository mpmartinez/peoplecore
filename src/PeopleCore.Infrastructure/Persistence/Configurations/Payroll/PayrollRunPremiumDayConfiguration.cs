using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeopleCore.Domain.Entities.Payroll;

namespace PeopleCore.Infrastructure.Persistence.Configurations.Payroll;

public class PayrollRunPremiumDayConfiguration : IEntityTypeConfiguration<PayrollRunPremiumDay>
{
    public void Configure(EntityTypeBuilder<PayrollRunPremiumDay> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DayType).HasConversion<string>().HasMaxLength(64);
        builder.Property(x => x.Days).HasColumnType("numeric(6,2)");
        builder.Property(x => x.Hours).HasColumnType("numeric(6,2)");
        builder.Property(x => x.OvertimeHours).HasColumnType("numeric(6,2)");
        builder.Property(x => x.NightDiffHours).HasColumnType("numeric(6,2)");
    }
}
